using System.Net;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;
using Sahar.Core.Text;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>0.17 review (fresh fable review of 2b0893b): C1, I1–I5 and the minors, each pinned.</summary>
public sealed class ReviewFixTests
{
    private DateTimeOffset _now = new(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(3));
    private readonly MemBox _box = new();
    private readonly Dictionary<string, Queue<Func<Action<string>?, LlmReply>>> _script = [];
    private readonly List<(string Provider, LlmRequest Req)> _asked = [];
    private Queue<Func<Action<string>?, LlmReply>> Q(string p) => _script.TryGetValue(p, out var q) ? q : _script[p] = new();
    private void Say(string p, string text) => Q(p).Enqueue(part => { part?.Invoke(text); return new LlmReply(text, 1, 1, TimeSpan.Zero, TimeSpan.Zero); });
    private void Fail(string p, string code, RateInfo? rate = null) => Q(p).Enqueue(_ =>
    {
        var e = new MindRuntimeException(code, "x");
        if (rate is not null) e.Data["rate"] = rate;
        throw e;
    });

    private sealed class Scripted(ReviewFixTests t, string id) : ILlm
    {
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            t._asked.Add((id, request));
            return Task.FromResult(t.Q(id).Dequeue()(partial));
        }
    }

    private OnlineState? _state;
    private OnlineState State => _state ??= new OnlineState(_box, () => _now);
    private TurnHistory? _history;
    private TurnHistory History => _history ??= new TurnHistory(_box, () => _now, () => 30);
    private OnlineTurn Turn() => new((p, _) => new Scripted(this, p.Id), new FakePrompts(), new FakeStore(), State, History) { Delay = (_, _) => Task.CompletedTask, Now = () => _now };
    private static readonly MindMsg Hello = new() { T = "hello", Address = new MindAddress(Gender.Male, null) };
    private static MindMsg Line(string text) => new() { T = "turn", Id = "c1", Source = "chat", Window = true, Text = text };
    private static IReadOnlyList<(ProviderProfile, string)> Chain(params string[] ids) =>
        ids.Select(i => (i == "groq" ? ProviderProfiles.Groq() : ProviderProfiles.OpenRouter(), "k")).ToList();

    // ---- C1: a 429 is a cooldown, not "done for the day" ----

    [Fact]
    public async Task C1_Plain429_CoolsDown_NotExhausted_NoTell()
    {
        Fail("groq", "quota", new RateInfo(null, null, 20));
        Fail("groq", "quota", new RateInfo(null, null, 20));
        Say("openrouter", "هلا");
        var r = await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal("openrouter", r.Provider);
        Assert.False(State.Exhausted("groq"));
        Assert.True(State.Cooling("groq"));
        _now = _now.AddSeconds(25);
        Assert.False(State.Cooling("groq"));   // Retry-After passed: usable again
    }

    [Fact]
    public async Task C1_CoolingOnly_FallsThroughWithoutTelling()
    {
        Fail("groq", "quota"); Fail("groq", "quota");
        Fail("openrouter", "quota"); Fail("openrouter", "quota");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Null(r.Note);   // not «خلّصت طلبات اليوم»: only a provider's own count says that
        Assert.True(State.Cooling("groq"));
        Assert.True(State.Usage("groq").Used == 2);
    }

    [Fact]
    public async Task C1_GroqSaysNoneLeft_DailyExhausted_Told()
    {
        Fail("groq", "quota", new RateInfo(0, 1000, 3));
        Fail("groq", "quota", new RateInfo(0, 1000, 3));
        State.SetRemote("openrouter", 50, 50);
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.True(State.Exhausted("groq"));
        Assert.Equal(OnlineTurn.ToldLine, r.Note);
    }

    [Fact]
    public async Task C1_OpenRouterHeadersAreNotItsDailyCount()
    {
        Q("openrouter").Enqueue(_ => new LlmReply("x", 1, 1, TimeSpan.Zero, TimeSpan.Zero) { Rate = new RateInfo(0, 20, null) });   // a per-minute header
        await Turn().RunAsync(Line("x"), Hello, Chain("openrouter"), new FakeIO(), default);
        Assert.False(State.Exhausted("openrouter"));
    }

    [Fact]
    public async Task C1_GroqPromptIsCappedSmaller()
    {
        for (int i = 0; i < 30; i++) History.Add("سؤال " + i + new string('ا', 300), "جواب " + new string('ب', 300), "online");
        Say("groq", "x");
        await Turn().RunAsync(Line("x"), Hello, Chain("groq"), new FakeIO(), default);
        Assert.True(_asked[0].Req.Messages.Sum(m => PromptBuilder.Tokens(m.Content)) <= ProviderProfiles.Groq().PromptCap);
        Assert.True(ProviderProfiles.Groq().PromptCap <= 2600);
    }

    [Fact]
    public void C1_OpenRouterDay_IsUtc()
    {
        _now = new DateTimeOffset(2026, 11, 28, 2, 0, 0, TimeSpan.FromHours(3));   // 23:00 UTC the 27th
        State.SetRemote("openrouter", 50, 50);
        State.SetRemote("groq", 1000, 1000);
        _now = _now.AddHours(1.5);   // 00:30 UTC the 28th: OpenRouter's day turned, the local day did not
        Assert.False(State.Exhausted("openrouter"));
        Assert.True(State.Exhausted("groq"));
    }

    // ---- I1: only online pairs go online, and «حكي جديد» clears them ----

    [Fact]
    public async Task I1_LocalPairs_NeverSentOnline()
    {
        History.Add("شو عم يصير", "عم تسمع فيروز SENTINELSONG بـ Code.exe", "local");
        History.Add("سؤال أونلاين", "جواب أونلاين", "online");
        Say("groq", "x");
        await Turn().RunAsync(Line("x"), Hello, Chain("groq"), new FakeIO(), default);
        var all = string.Join("\n", _asked[0].Req.Messages.Select(m => m.Content));
        Assert.DoesNotContain("SENTINELSONG", all);
        Assert.Contains("جواب أونلاين", all);
    }

    [Fact]
    public void I1_ClearOnline_KeepsLocal()
    {
        History.Add("a", "b", "local");
        History.Add("c", "d", "online");
        History.Clear("online");
        Assert.Equal(["a"], History.Recent().Select(t => t.User));
    }

    [Fact]
    public void I1_BusyBox_NeverWipesHistory()
    {
        History.Add("a", "b", "online");
        var busy = new BusyBox(_box);
        new TurnHistory(busy, () => _now, () => 30).Add("c", "d", "online");
        Assert.Single(History.Recent());   // the read failed: nothing was written over it
    }

    // ---- I2: distress goes through the local runner (it remembers it) ----

    [Fact]
    public void I2_Distress_RoutesLocal()
    {
        var r = BrainRouter.Decide(new RouteInput("chat", true, "online", false, true, "بدي موت", true, Distress: true));
        Assert.Equal((BrainChoice.Local, "distress"), r);
    }

    // ---- I3 (owner decision 2026-11-27: the output word filter is removed; a risk-high say still becomes the fixed reply) ----

    [Fact]
    public async Task I3_TechnicalAnswer_KeptWhole_AndInHistory()
    {
        const string text = "شغّل أداة تشخيص المشكلة، وبعدين شوف الـ diagnostics.";
        Say("groq", text);
        var io = new FakeIO();
        await Turn().RunAsync(Line("x"), Hello, Chain("groq"), io, default);
        Assert.Equal(text, io.Final.Text);
        Assert.Equal(text, History.Recent().Single().Her);
    }

    // ---- I4: digits stay as written in code, links and numbers mixed with Latin ----

    [Theory]
    [InlineData("بعد 5 دقايق", "بعد ٥ دقايق")]
    [InlineData("استعمل `range(10)` هون", "استعمل `range(10)` هون")]
    [InlineData("شوف https://x.io/v2/a1 بكرا 3", "شوف https://x.io/v2/a1 بكرا ٣")]
    [InlineData("نسخة v1.2.3 و gpt-4o", "نسخة v1.2.3 و gpt-4o")]
    [InlineData("```\nfor i in range(3):\n```", "```\nfor i in range(3):\n```")]
    public void I4_ChatDigits(string text, string want) => Assert.Equal(want, Digits.ForChat(text));

    // ---- I5: offline: skip online for a while, say it once, cap the wait ----

    [Fact]
    public async Task I5_AllOffline_SkipsOnlineForMinutes_NoteOnce()
    {
        Fail("groq", "offline"); Fail("openrouter", "offline");   // 0.17.2: a timeout alone is not offline (Fix0172Tests)
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal(OnlineTurn.OfflineLine, r.Note);
        Assert.True(State.Offline);
        var r2 = await Turn().RunAsync(Line("y"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Null(r2.Note);
        Assert.Equal(2, _asked.Count);   // nothing asked while offline
        _now = _now.AddSeconds(61);   // 0.17.2: a minute's rest (was 3)
        Assert.False(State.Offline);
    }

    [Fact]
    public async Task I5_FirstWordCap_StopsOnline()
    {
        var slow = new SlowLlm();
        var t = new OnlineTurn((_, _) => slow, new FakePrompts(), new FakeStore(), State, History) { FirstWordCap = TimeSpan.FromMilliseconds(200), Now = () => _now };
        var r = await t.RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Equal(1, slow.Calls);   // the cap ends the online turn, not just this provider
        Assert.Null(r.Note);   // 0.17.2: slow is not «ما في نت»
        Assert.False(State.Offline);
    }

    private sealed class SlowLlm : ILlm
    {
        public int Calls;
        public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            Calls++;
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new LlmReply("", 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    private sealed class BusyBox(MemBox inner) : Sahar.Core.Mind.Memory.ISecretBox
    {
        public byte[]? Read(string name) => null;
        public bool TryRead(string name, out byte[]? data) { data = null; return false; }
        public void Write(string name, ReadOnlySpan<byte> data) => inner.Write(name, data);
        public void Delete(string name) => inner.Delete(name);
    }

    // ---- minors ----

    [Theory]
    [InlineData("اكتبلي شعر عن المطر", false)]
    [InlineData("اكتبيلي قصيدة عن الصلاة", false)]
    [InlineData("اكتبلي ملاحظة إنو بكرا عندي اجتماع", true)]
    [InlineData("شغليلي موسيقى واكتبيلي قصيدة", true)]
    [InlineData("كيف الطقس بكرا", true)]
    public void Router_WritingVerbBeatsATopicNoun(string line, bool action) => Assert.Equal(action, Intents.Action(line));

    [Fact]
    public async Task KeyCheck_UnknownModel_IsModelBad()
    {
        var f = new FakeProvider(new FakeProvider.Answer(HttpStatusCode.OK, """{"data":[{"id":"llama-3.3-70b-versatile"}]}"""));
        Assert.Equal("model-bad", (await KeyCheck.RunAsync(f, ProviderProfiles.Groq("no/such-model"), "k", default)).Code);
        Assert.Equal("اسم الموديل غلط", OnlineErrors.Line("model-bad"));
        Assert.Equal("model-bad", OnlineErrors.FromStatus(404));
    }

    [Fact]
    public void Keys_BusyBox_RefusesInsteadOfWiping()
    {
        new OnlineKeys(_box).Set("groq", "gsk_1");
        Assert.False(new OnlineKeys(new BusyBox(_box)).Set("openrouter", "sk-2"));
        Assert.Equal("gsk_1", new OnlineKeys(_box).Get("groq"));
    }
}
