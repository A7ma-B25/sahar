using Sahar.Core.Mind;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>0.17 Task 4: one plain-text streamed request per provider in the chain (spec §4, §7).</summary>
public sealed class OnlineTurnTests
{
    private readonly DateTimeOffset _now = new(2026, 11, 27, 20, 0, 0, TimeSpan.FromHours(3));
    private readonly MemBox _box = new();
    private readonly Dictionary<string, Queue<Func<Action<string>?, LlmReply>>> _script = [];
    private readonly List<(string Provider, LlmRequest Req)> _asked = [];
    private readonly List<TimeSpan> _waits = [];

    private void Say(string provider, params string[] words) => Q(provider).Enqueue(p =>
    {
        foreach (var w in words) p?.Invoke(w);
        return new LlmReply(string.Concat(words), 10, words.Length, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(20));
    });
    private void Fail(string provider, string code, RateInfo? rate = null, params string[] wordsFirst) => Q(provider).Enqueue(p =>
    {
        foreach (var w in wordsFirst) p?.Invoke(w);
        var e = new MindRuntimeException(code, "x");
        if (rate is not null) e.Data["rate"] = rate;
        throw e;
    });
    private Queue<Func<Action<string>?, LlmReply>> Q(string p) => _script.TryGetValue(p, out var q) ? q : _script[p] = new();

    private sealed class Scripted(OnlineTurnTests t, string id) : ILlm
    {
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            t._asked.Add((id, request));
            return Task.FromResult(t.Q(id).Dequeue()(partial));
        }
    }

    private OnlineState State => _state ??= new OnlineState(_box, () => _now);
    private OnlineState? _state;
    private TurnHistory History => _history ??= new TurnHistory(_box, () => _now, () => 30);
    private TurnHistory? _history;

    private OnlineTurn Turn() => new((p, _) => new Scripted(this, p.Id), new FakePrompts(), new FakeStore(), State, History)
    {
        Delay = (d, _) => { _waits.Add(d); return Task.CompletedTask; },
    };

    private static readonly MindMsg Hello = new() { T = "hello", Address = new MindAddress(Gender.Male, null) };
    private static MindMsg Line(string text) => new() { T = "turn", Id = "c1", Source = "chat", Window = true, Text = text };
    private static IReadOnlyList<(ProviderProfile, string)> Chain(params string[] ids) =>
        ids.Select(i => (i == "groq" ? ProviderProfiles.Groq() : ProviderProfiles.OpenRouter(), "key-" + i)).ToList();

    [Fact]
    public async Task Answers_StreamsCumulativeWords_SaysOnline_KeepsHistory()
    {
        Say("groq", "أهلين ", "يا ", "غالي");
        var io = new FakeIO();
        var r = await Turn().RunAsync(Line("مرحبا"), Hello, Chain("groq", "openrouter"), io, default);
        Assert.True(r.Answered);
        Assert.Equal("groq", r.Provider);
        Assert.Equal(["أهلين", "أهلين يا", "أهلين يا غالي"], io.Of("partial").Select(m => m.Text!.Trim()));
        Assert.All(io.Of("partial"), m => Assert.Equal("online", m.Brain));
        Assert.Equal("أهلين يا غالي", io.Final.Text);
        Assert.Equal("online", io.Final.Brain);
        Assert.True(io.Final.Final);
        Assert.Equal("مرحبا", History.Recent().Single().User);
        Assert.Equal("online", History.Recent().Single().Brain);
        Assert.Equal(1, State.Usage("groq").Used);
        Assert.Null(_asked[0].Req.JsonSchema);
        Assert.Equal(1500, _asked[0].Req.Sampling!.MaxTokens);
        Assert.StartsWith("PERSONA", _asked[0].Req.Messages[0].Content);
    }

    [Fact]
    public async Task Groq429Twice_ThenOpenRouter_AttemptsCounted_RetryAfterCapped()
    {
        Fail("groq", "quota", new RateInfo(null, null, 30));
        Fail("groq", "quota");
        Say("openrouter", "تمام");
        var io = new FakeIO();
        var r = await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq", "openrouter"), io, default);
        Assert.Equal("openrouter", r.Provider);
        Assert.Equal(2, State.Usage("groq").Used);
        Assert.True(State.Exhausted("groq"));
        Assert.Equal([TimeSpan.FromSeconds(3)], _waits);   // Retry-After 30 s capped at 3 s
    }

    [Fact]
    public async Task ServerError_RetriedOnceAfter800ms()
    {
        Fail("groq", "server");
        Say("groq", "هلا");
        var r = await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq"), new FakeIO(), default);
        Assert.True(r.Answered);
        Assert.Equal([TimeSpan.FromMilliseconds(800)], _waits);
        Assert.Equal(2, State.Usage("groq").Used);
        Assert.False(State.Exhausted("groq"));
    }

    [Fact]
    public async Task KeyBad_NotRetried_NextProvider()
    {
        Fail("groq", "key-bad");
        Say("openrouter", "هلا");
        var r = await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal("openrouter", r.Provider);
        Assert.Empty(_waits);
    }

    [Fact]
    public async Task OnlineTurn_MidStreamError_FallsThroughAndRestartsWords()
    {
        Fail("groq", "stream-error", null, "نص ", "جواب");
        Say("openrouter", "جواب ", "كامل");
        var io = new FakeIO();
        await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq", "openrouter"), io, default);
        var partials = io.Of("partial").Select(m => m.Text).ToList();
        Assert.Contains("", partials);   // the half answer is wiped before the next provider speaks
        Assert.Equal("جواب كامل", partials[^1]);
        Assert.Equal("جواب كامل", io.Final.Text);
        Assert.Single(io.Of("say"));
    }

    [Fact]
    public async Task OnlineTurn_AllOffline_FallsToLocal_NoSay()
    {
        Fail("groq", "offline");
        Fail("openrouter", "offline");
        var io = new FakeIO();
        var r = await Turn().RunAsync(Line("سؤال"), Hello, Chain("groq", "openrouter"), io, default);
        Assert.False(r.Answered);
        Assert.False(r.Tell);   // offline is not "the quota is out"
        Assert.Empty(io.Of("say"));
        Assert.Empty(History.Recent());
    }

    [Fact]
    public async Task AllExhausted_TellsOncePerDay()
    {
        State.Exhaust("groq");
        Fail("openrouter", "quota");
        Fail("openrouter", "quota");
        var r1 = await Turn().RunAsync(Line("a"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.True(r1.Tell);
        var r2 = await Turn().RunAsync(Line("b"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r2.Tell);
        Assert.Equal(2, _asked.Count);   // one try and its retry; out providers are not asked again
    }

    [Fact]
    public async Task Distress_FixedReply_NoRequest()
    {
        var io = new FakeIO();
        var r = await Turn().RunAsync(Line("CUE"), Hello, Chain("groq"), io, default);
        Assert.True(r.Answered);
        Assert.Empty(_asked);
        Assert.StartsWith("الرد الثابت", io.Final.Text);
        Assert.Equal("distress", io.Final.Mood);
    }

    [Fact]
    public async Task EmptyChain_NoRequest()
    {
        var r = await Turn().RunAsync(Line("x"), Hello, [], new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task GroqHeaders_UpdateTheCount()
    {
        Q("groq").Enqueue(p => new LlmReply("x", 1, 1, TimeSpan.Zero, TimeSpan.Zero) { Rate = new RateInfo(900, 1000, null) });
        await Turn().RunAsync(Line("x"), Hello, Chain("groq"), new FakeIO(), default);
        Assert.Equal((100, (int?)1000), State.Usage("groq"));
    }
}
