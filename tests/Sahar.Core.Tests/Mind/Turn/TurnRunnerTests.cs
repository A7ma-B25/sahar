using System.Collections.Concurrent;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Turn;

/// <summary>C3: one turn in structured steps (spec §3.5) — a fake model, a fake engine.</summary>
public sealed class TurnRunnerTests
{
    internal sealed class FakeLlm : ILlm
    {
        public readonly Queue<Func<LlmRequest, Action<string>?, CancellationToken, Task<string>>> Script = new();
        public readonly List<LlmRequest> Requests = [];
        public void Reply(string json) => Script.Enqueue((_, partial, _) =>
        {
            // streamed in small pieces, as llama-server does
            for (int i = 0; i < json.Length; i += 7) partial?.Invoke(json.Substring(i, Math.Min(7, json.Length - i)));
            return Task.FromResult(json);
        });
        public void Slow(TimeSpan delay) => Script.Enqueue(async (_, _, ct) => { await Task.Delay(delay, ct); return "{}"; });
        public void Throw(string code) => Script.Enqueue((_, _, _) => throw new MindRuntimeException(code));

        public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            Requests.Add(request);
            var next = Script.Count > 0 ? Script.Dequeue() : (_, _, _) => Task.FromResult(Decision(say: "خلص"));
            var text = await next(request, partial, ct);
            return new LlmReply(text, 10, 10, TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    internal sealed class FakeIO : ITurnIO
    {
        public readonly ConcurrentQueue<MindMsg> Sent = new();
        public Func<MindMsg, MindMsg> Engine = c => new MindMsg { T = "tool-result", Id = c.Id, Call = c.Call, Ok = true, Text = "تمام" };
        private readonly ConcurrentDictionary<int, MindMsg> _calls = new();
        public void Send(MindMsg msg)
        {
            Sent.Enqueue(msg);
            if (msg.T == "call") _calls[msg.Call!.Value] = msg;
        }
        public Task<MindMsg> ReplyAsync(string turnId, int call, CancellationToken ct) => Task.FromResult(Engine(_calls[call]));
        public IEnumerable<MindMsg> Of(string t) => Sent.Where(m => m.T == t);
        public MindMsg Final => Sent.Last(m => m.T == "say");
    }

    internal sealed class FakePrompts : IPromptParts
    {
        public string Persona(MindVoice voice, MindAddress address) => "PERSONA";
        public string PersonalBlock(string line, int maxTokens) => "PROFILE";
        public DistressReply? Distress(string line) => line.Contains("CUE", StringComparison.Ordinal) ? new("الرد الثابت", ["احكي مع حدا بتثق فيه"]) : null;
        public Sensitivity Sensitive(string text, Sensitivity modelTag) => text.Contains("دوا", StringComparison.Ordinal) ? Sensitivity.Health : modelTag;
    }

    internal sealed class FakeStore : IMindStore
    {
        public readonly List<(string Text, ProfileCategory Cat, Sensitivity S)> Proposed = [];
        public IReadOnlyList<ProfileItem> Profile() => [];
        public ProfileItem? Propose(string text, ProfileCategory category, ItemSource source, Sensitivity sensitive)
        {
            Proposed.Add((text, category, sensitive));
            return new ProfileItem("p" + Proposed.Count, text, category, [source], Confidence.Low, ItemStatus.Proposed, sensitive, source.At);
        }
        public bool Confirm(string id) => false;
        public bool Edit(string id, string text) => false;
        public bool Reject(string id) => false;
        public bool Delete(string id) => false;
        public IReadOnlyList<ProfileItem> ForPrompt(string line, int maxTokens) => [];
        public IReadOnlyList<MemoryItem> Memories(string? search = null) => [];
        public MemoryItem AddMemory(string text, MemoryKind kind, Sensitivity sensitive = Sensitivity.None) => throw new NotSupportedException();
        public bool DeleteMemory(string id) => false;
        public StyleCard Style() => new("", 0, 0, 0);
        public int AddSamples(LearnSource source, IEnumerable<string> lines) => 0;
        public IReadOnlyList<string> Samples(string topic, int count) => [];
        public void ForgetAll() { }
        public string Export() => "{}";
    }

    internal static string Decision(string actions = "[]", string next = "done", string say = "", string mood = "null", string remember = "[]") =>
        $$"""{"actions":{{actions}},"next":"{{next}}","say":{{System.Text.Json.JsonSerializer.Serialize(say)}},"mood":{{(mood == "null" ? "null" : "\"" + mood + "\"")}},"remember":{{remember}}}""";

    private static readonly IReadOnlyList<ToolSpec> Tools = ToolCatalog.All;

    private static MindMsg Hello(IReadOnlyList<ToolSpec>? tools = null) => new()
    {
        T = "hello", Version = "1", Lang = "ar", Address = new MindAddress(Gender.Male, null), Tools = tools ?? Tools,
        Caps = new MindCaps(true, true, MindVoice.Complementary),
    };

    private static MindMsg Chat(string text, string id = "t1") => new() { T = "turn", Id = id, Source = "chat", Text = text, Situation = new Situation("10:12", "fri") };

    private readonly FakeLlm _llm = new();
    private readonly FakeIO _io = new();
    private readonly FakeStore _store = new();

    private TurnRunner Runner(TimeSpan? budget = null) => new(_llm, new FakePrompts(), _store) { ChatBudget = budget ?? TimeSpan.FromSeconds(20) };

    private Task Run(MindMsg turn, TurnRunner? r = null, CancellationToken ct = default) => (r ?? Runner()).RunAsync(turn, Hello(), _io, ct);

    [Fact]
    public async Task Turn_SimpleChat_StreamsAndSays()
    {
        _llm.Reply(Decision(say: "منيحة! وإنت شو عم تعمل هلق؟", mood: "happy"));
        await Run(Chat("كيفك"));
        Assert.Equal("منيحة! وإنت شو عم تعمل هلق؟", _io.Final.Text);
        Assert.True(_io.Final.Final);
        Assert.Equal("happy", _io.Final.Mood);
        Assert.Equal("t1", _io.Final.Id);
        var partials = _io.Of("partial").Select(p => p.Text!).ToList();
        Assert.NotEmpty(partials);
        Assert.Equal("منيحة!", partials[0]);   // sentence-sized: the first chunk ends at the first sentence
        Assert.Empty(_io.Of("call"));
        Assert.Equal(["thinking", "idle"], _io.Of("state").Select(s => s.Phase));
        var req = _llm.Requests.Single();
        Assert.NotNull(req.JsonSchema);
        Assert.Equal("system", req.Messages[0].Role);
        Assert.Contains("PERSONA", req.Messages[0].Content);
        Assert.Contains("timer.start", req.Messages[0].Content);   // the tool list is in the stable prefix
        Assert.Equal("user", req.Messages[^1].Role);
        Assert.Contains("كيفك", req.Messages[^1].Content);
        Assert.Contains("PROFILE", req.Messages[^1].Content);
    }

    [Fact]
    public async Task Turn_ActionRuns_ConfirmationIsTheOutcome()
    {
        _io.Engine = c => new MindMsg { T = "tool-result", Id = c.Id, Call = c.Call, Ok = true, Text = "شغّلت مؤقت ١٥ دقيقة" };
        _llm.Reply(Decision("""[{"tool":"timer.start","args":{"minutes":15},"why":"طلب تركيز"}]""", say: "يلا نركّز! شغّلت مؤقت ربع ساعة"));
        await Run(Chat("ركّزني شي ربع ساعة"));
        var call = _io.Of("call").Single();
        Assert.Equal("timer.start", call.Tool);
        Assert.Equal("""{"minutes":15}""", call.Data);
        Assert.Equal("طلب تركيز", call.Why);
        Assert.Equal(1, call.Call);
        Assert.Equal("يلا نركّز! شغّلت مؤقت ربع ساعة", _io.Final.Text);   // all ran: her words stand
        Assert.Empty(_io.Of("partial"));   // never streamed before the outcome was known
    }

    [Fact]
    public async Task Turn_SayContradictsOutcome_UsesTemplate()
    {
        _io.Engine = c => new MindMsg { T = "tool-result", Id = c.Id, Call = c.Call, Ok = false, Error = "asked-no", Text = ToolTemplates.AskedNo };
        _llm.Reply(Decision("""[{"tool":"settings.set","args":{"key":"weatherEnabled","value":true},"why":"طلب"}]""", say: "شغّلت الطقس ☀️"));
        await Run(Chat("شغلي الطقس"));
        Assert.Equal(ToolTemplates.AskedNo, _io.Final.Text);
        // no action but a claim of one: the honest line
        _llm.Reply(Decision(say: "تمام، حفظت الملاحظة"));
        await Run(Chat("احفظي هالشي", "t2"));
        Assert.Equal(ToolTemplates.NothingDone, _io.Final.Text);
    }

    [Fact]
    public async Task Turn_UnknownTool_NeverRuns()
    {
        _llm.Reply(Decision("""[{"tool":"files.delete","args":{},"why":"x"}]""", say: "مسحتو"));
        await Run(Chat("امسحي الملف"));
        Assert.Empty(_io.Of("call"));
        Assert.Equal(ToolTemplates.BadOutput, _io.Final.Text);
        // announced list without notes: note.save is not callable either
        _llm.Reply(Decision("""[{"tool":"note.save","args":{"text":"x"},"why":"x"}]"""));
        await Runner().RunAsync(Chat("x", "t2"), Hello([ToolCatalog.Get("timer.start")!]), _io, default);
        Assert.Empty(_io.Of("call"));
        // bad args never run
        _llm.Reply(Decision("""[{"tool":"timer.start","args":{"minutes":9000},"why":"x"}]"""));
        await Run(Chat("x", "t3"));
        Assert.Empty(_io.Of("call"));
        Assert.Equal(ToolTemplates.BadOutput, _io.Final.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"say\":\"x\"}")]
    [InlineData("[1]")]
    [InlineData("")]
    public async Task Turn_BadOutput_Template(string reply)
    {
        _llm.Script.Enqueue((_, _, _) => Task.FromResult(reply));
        await Run(Chat("x"));
        Assert.Empty(_io.Of("call"));
        Assert.Equal(ToolTemplates.BadOutput, _io.Final.Text);
    }

    [Fact]
    public async Task Turn_NeedResults_SecondStep()
    {
        _io.Engine = c => new MindMsg { T = "tool-result", Id = c.Id, Call = c.Call, Ok = true, Data = """{"prayer":"maghrib","inMinutes":42}""", Text = "المغرب بعد ٤٢ دقيقة" };
        _llm.Reply(Decision("""[{"tool":"prayer.next","args":{},"why":"سأل"}]""", "need-results"));
        _llm.Reply(Decision(say: "المغرب بعد ٤٢ دقيقة، بعدك عندك وقت ☕"));
        await Run(Chat("قديش باقي عالمغرب؟"));
        Assert.Equal(2, _llm.Requests.Count);
        var second = _llm.Requests[1].Messages;
        Assert.Equal("assistant", second[^2].Role);
        Assert.Contains("maghrib", second[^1].Content);
        Assert.Contains("بيانات", second[^1].Content);   // results are quoted data, not instructions
        Assert.Equal("المغرب بعد ٤٢ دقيقة، بعدك عندك وقت ☕", _io.Final.Text);
    }

    [Fact]
    public async Task Turn_StepCapThree()
    {
        for (int i = 0; i < 5; i++) _llm.Reply(Decision("""[{"tool":"prayer.next","args":{},"why":"x"}]""", "need-results"));
        await Run(Chat("x"));
        Assert.Equal(3, _llm.Requests.Count);
        Assert.Equal(3, _io.Of("call").Count());
        Assert.Single(_io.Of("say"));
    }

    [Fact]
    public async Task Turn_ActionCapThreePerStep()
    {
        var five = "[" + string.Join(',', Enumerable.Range(1, 5).Select(i => $$"""{"tool":"move.play","args":{"move":"hop"},"why":"{{i}}"}""")) + "]";
        _llm.Reply(Decision(five));
        await Run(Chat("x"));
        Assert.Equal([1, 2, 3], _io.Of("call").Select(c => c.Call!.Value));
    }

    [Fact]
    public async Task Turn_Timeout_Says()
    {
        _llm.Slow(TimeSpan.FromSeconds(5));
        var started = DateTime.UtcNow;
        await Run(Chat("x"), Runner(TimeSpan.FromMilliseconds(150)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(3));
        Assert.Equal("timeout", _io.Of("error").Single().Error);
        Assert.Equal(ToolTemplates.Timeout, _io.Final.Text);
    }

    [Fact]
    public async Task Turn_Cancel_SaysNothing()
    {
        _llm.Slow(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource(100);
        await Run(Chat("x"), ct: cts.Token);
        Assert.Empty(_io.Of("say"));
        Assert.Empty(_io.Of("error"));
    }

    [Fact]
    public async Task Turn_RuntimeError_IsReported()
    {
        _llm.Throw("model-missing");
        await Run(Chat("x"));
        Assert.Equal("model-missing", _io.Of("error").Single().Error);
        Assert.Empty(_io.Of("say"));
    }

    [Fact]
    public async Task Turn_Distress_FixedReply_NoModel_NoTools()
    {
        await Run(Chat("CUE"));
        Assert.Empty(_llm.Requests);
        Assert.Empty(_io.Of("call"));
        Assert.Equal("الرد الثابت\nاحكي مع حدا بتثق فيه", _io.Final.Text);
        Assert.Equal("distress", _io.Final.Mood);
    }

    [Fact]
    public async Task Turn_RememberAndMindLocalTools_AreProposalsOnly()
    {
        _llm.Reply(Decision("""[{"tool":"goal.add","args":{"text":"خلّص المشروع"},"why":"x"}]""",
            remember: """[{"text":"بيحب القهوة","kind":"preference","sensitive":"none"},{"text":"بياخد دوا","kind":"fact","sensitive":"none"}]"""));
        await Run(Chat("بدي خلّص المشروع"));
        Assert.Empty(_io.Of("call"));   // the engine never sees proposals
        Assert.Contains(_store.Proposed, p => p.Text == "خلّص المشروع" && p.Cat == ProfileCategory.Goals);
        Assert.Contains(_store.Proposed, p => p.Text == "بيحب القهوة" && p.Cat == ProfileCategory.Likes);
        Assert.Contains(_store.Proposed, p => p.Text == "بياخد دوا" && p.S == Sensitivity.Health);   // the stricter tag wins
        Assert.Equal(3, _io.Of("propose").Count());
    }

    [Fact]
    public async Task Turn_Initiative_NothingIsAnAnswer()
    {
        _llm.Reply(Decision());
        await Run(new MindMsg { T = "turn", Id = "i1", Source = "initiative", Situation = new Situation("10:00", "mon") });
        Assert.Equal("", _io.Final.Text);
        Assert.Contains("مبادرة", _llm.Requests.Single().Messages[^1].Content);
    }

    [Fact]
    public async Task Turn_HistoryKeepsTheLastSixChatTurns()
    {
        var r = Runner();
        for (int i = 0; i < 8; i++)
        {
            _llm.Reply(Decision(say: "رد" + i));
            await Run(Chat("سؤال" + i, "t" + i), r);
        }
        _llm.Reply(Decision(say: "x"));
        await Run(Chat("آخر", "t9"), r);
        var msgs = _llm.Requests[^1].Messages;
        Assert.Equal(1 + 12 + 1, msgs.Count);
        Assert.DoesNotContain(msgs, m => m.Content.Contains("سؤال1", StringComparison.Ordinal));
        Assert.Contains(msgs, m => m.Content.Contains("سؤال2", StringComparison.Ordinal));
        Assert.Equal(msgs[0].Content, Runner().Prefix(Hello()));   // the stable prefix does not move between turns
    }

    [Fact]
    public async Task Turn_AskedThenNo_ViaAnswer()
    {
        _io.Engine = c => new MindMsg { T = "answer", Id = c.Id, Call = c.Call, Yes = false };
        _llm.Reply(Decision("""[{"tool":"settings.set","args":{"key":"language","value":"english"},"why":"x"}]""", say: "غيّرت اللغة"));
        await Run(Chat("احكي انكليزي"));
        Assert.Equal(ToolTemplates.AskedNo, _io.Final.Text);
    }
}
