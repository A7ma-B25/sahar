using System.Net;
using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;
using static Sahar.Core.Tests.Mind.Turn.TurnRunnerTests;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>0.17.2 (his diag 2026-11-28, mind.log): a cold first request through his VPN timed out at 6 s and made her
/// say «ما في نت» and rest online for 3 minutes while the net was fine; OpenRouter's free Gemma 31B gave 429 twice.</summary>
public sealed class Fix0172Tests
{
    private DateTimeOffset _now = new(2026, 11, 28, 1, 54, 0, TimeSpan.FromHours(3));
    private readonly MemBox _box = new();
    private readonly Dictionary<string, Queue<Func<Action<string>?, LlmReply>>> _script = [];
    private readonly List<(string Provider, LlmRequest Req)> _asked = [];
    private Queue<Func<Action<string>?, LlmReply>> Q(string p) => _script.TryGetValue(p, out var q) ? q : _script[p] = new();
    private void Say(string p, string text) => Q(p).Enqueue(part => { part?.Invoke(text); return new LlmReply(text, 1, 1, TimeSpan.Zero, TimeSpan.Zero); });
    private void Fail(string p, string code, string detail = "x") => Q(p).Enqueue(_ => throw new MindRuntimeException(code, detail));

    private sealed class Scripted(Fix0172Tests t, string id) : ILlm
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

    // ---- fix 1: the timeouts ----

    [Fact]
    public void FirstByte_Is20s()
    {
        var c = new OpenAiClient(new FakeProvider(), ProviderProfiles.Groq(), "k");
        Assert.Equal(TimeSpan.FromSeconds(20), c.FirstByte);
    }

    [Fact]
    public async Task ConnectTimeout_IsOffline_NotFirstByte()
    {
        // SocketsHttpHandler's ConnectTimeout throws TaskCanceledException(inner TimeoutException): connect-level.
        var c = new OpenAiClient(new ConnectTimeoutHandler(), ProviderProfiles.Groq(), "k");
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => c.CompleteAsync(Hi, null, default));
        Assert.Equal(OnlineErrors.Offline, e.Code);
        Assert.Equal("connect-timeout", e.Detail);
    }

    private static readonly LlmRequest Hi = new([new ChatMessage("user", "مرحبا")], null, Sampling.Online);

    private sealed class ConnectTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            throw new TaskCanceledException("connect timed out", new TimeoutException());
    }

    // ---- fix 2: one provider's timeout is not "no internet" ----

    [Fact]
    public async Task FirstByteTimeout_NotOffline_ProviderCools60s_OthersTried()
    {
        Fail("groq", OnlineErrors.Timeout, "first-byte");
        Say("openrouter", "هلا");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal("openrouter", r.Provider);
        Assert.Null(r.Note);
        Assert.False(State.Offline);
        Assert.True(State.Cooling("groq"));
        _now = _now.AddSeconds(59);
        Assert.True(State.Cooling("groq"));
        _now = _now.AddSeconds(2);
        Assert.False(State.Cooling("groq"));
    }

    [Fact]
    public async Task TheLoggedTurn_TimeoutThenCooling_NeverSaysNoInternet()
    {
        // 01:55:01: Groq timed out while OpenRouter was cooling after its 429s — she said «ما في نت» and rested 3 min.
        State.Cool("openrouter", 45);
        Fail("groq", OnlineErrors.Timeout, "first-byte");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Null(r.Note);
        Assert.False(State.Offline);
    }

    [Fact]
    public async Task Quota429_IsNeverOffline()
    {
        Fail("groq", OnlineErrors.Quota); Fail("groq", OnlineErrors.Quota);
        Fail("openrouter", OnlineErrors.Quota); Fail("openrouter", OnlineErrors.Quota);
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Null(r.Note);
        Assert.False(State.Offline);
    }

    [Fact]
    public async Task OneUnreachable_OneTimeout_NotOffline()
    {
        Fail("groq", OnlineErrors.Offline, "NameResolutionError");
        Fail("openrouter", OnlineErrors.Timeout, "first-byte");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Null(r.Note);
        Assert.False(State.Offline);
        Assert.True(State.Cooling("groq") && State.Cooling("openrouter"));
    }

    [Fact]
    public async Task OnlyOneTried_OtherCooling_Unreachable_NotOffline()
    {
        State.Cool("openrouter", 30);
        Fail("groq", OnlineErrors.Offline, "ConnectionError");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Null(r.Note);   // OpenRouter was not tried: nobody knows the net is down
        Assert.False(State.Offline);
    }

    [Fact]
    public async Task EveryProviderUnreachable_Offline_Rests60s_ThenTriesAgainByItself()
    {
        Fail("groq", OnlineErrors.Offline, "NameResolutionError");
        Fail("openrouter", OnlineErrors.Offline, "connect-timeout");
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal(OnlineTurn.OfflineLine, r.Note);
        Assert.True(State.Offline);
        Assert.Equal(TimeSpan.FromSeconds(60), OnlineState.OfflineRest);
        _now = _now.AddSeconds(59);
        Assert.True(State.Offline);
        _now = _now.AddSeconds(2);
        Assert.False(State.Offline);
        Assert.True(State.Usable("groq") && State.Usable("openrouter"));   // the next line goes online again, by itself
        Say("groq", "رجعت");
        var r2 = await Turn().RunAsync(Line("y"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.Equal("groq", r2.Provider);
    }

    [Fact]
    public async Task FirstWordCap_NotOffline_ProviderCools()
    {
        var slow = new SlowLlm();
        var t = new OnlineTurn((_, _) => slow, new FakePrompts(), new FakeStore(), State, History) { FirstWordCap = TimeSpan.FromMilliseconds(150), Now = () => _now };
        var r = await t.RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Null(r.Note);
        Assert.False(State.Offline);
        Assert.True(State.Cooling("groq"));
    }

    [Fact]
    public void BackOnline_EndsTheRest()
    {
        State.GoOffline();
        Assert.True(State.Offline);
        State.BackOnline();
        Assert.False(State.Offline);
    }

    private sealed class SlowLlm : ILlm
    {
        public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new LlmReply("", 0, 0, TimeSpan.Zero, TimeSpan.Zero);
        }
    }

    // ---- fix 3: OpenRouter's free default and one alternate on a 429 ----

    [Fact]
    public void OpenRouter_Default_And_Alternates()
    {
        Assert.Equal("google/gemma-4-26b-a4b-it:free", ProviderProfiles.OpenRouterDefault);
        Assert.Equal(ProviderProfiles.OpenRouterDefault, ProviderProfiles.OpenRouterModels[0]);
        Assert.Contains("google/gemma-4-31b-it:free", ProviderProfiles.OpenRouterModels);
        Assert.Contains("nvidia/nemotron-3.5-super-vl:free", ProviderProfiles.OpenRouterModels);
        Assert.Contains("baidu/ernie-5.5-flash-preview:free", ProviderProfiles.OpenRouterModels);
        Assert.Equal("google/gemma-4-31b-it:free", ProviderProfiles.OpenRouter().Alternate);
        Assert.Equal(ProviderProfiles.OpenRouterDefault, ProviderProfiles.OpenRouter("google/gemma-4-31b-it:free").Alternate);
        Assert.Null(ProviderProfiles.OpenRouter("openrouter/free").Alternate);   // not a :free model id: no alternate
        Assert.Null(ProviderProfiles.OpenRouter("anthropic/claude-x").Alternate);   // a paid model: never swapped
        Assert.Null(ProviderProfiles.Groq().Alternate);
    }

    [Fact]
    public async Task OpenRouter429_TriesOneAlternateFreeModel_NoWait_TwoRequestsAtMost()
    {
        Fail("openrouter", OnlineErrors.Quota);
        Say("openrouter", "هلا");
        var waits = new List<TimeSpan>();
        var t = new OnlineTurn((p, _) => new Scripted(this, p.Id), new FakePrompts(), new FakeStore(), State, History) { Delay = (d, _) => { waits.Add(d); return Task.CompletedTask; }, Now = () => _now };
        var r = await t.RunAsync(Line("x"), Hello, Chain("openrouter"), new FakeIO(), default);
        Assert.Equal("openrouter", r.Provider);
        Assert.Equal(2, _asked.Count);
        Assert.Null(_asked[0].Req.Model);
        Assert.Equal("google/gemma-4-31b-it:free", _asked[1].Req.Model);
        Assert.Empty(waits);   // another model: no Retry-After wait
        Assert.Equal(2, State.Usage("openrouter").Used);   // each counts (OpenRouter counts failed ones too)
    }

    [Fact]
    public async Task OpenRouter429Twice_FallsThrough_AfterTwoRequests()
    {
        Fail("groq", OnlineErrors.Timeout, "first-byte");
        Fail("openrouter", OnlineErrors.Quota); Fail("openrouter", OnlineErrors.Quota);
        var r = await Turn().RunAsync(Line("x"), Hello, Chain("groq", "openrouter"), new FakeIO(), default);
        Assert.False(r.Answered);
        Assert.Equal(2, _asked.Count(a => a.Provider == "openrouter"));
        Assert.True(State.Cooling("openrouter"));
    }

    [Fact]
    public async Task RequestModel_OverridesTheProfileModel_InTheBody()
    {
        var f = new FakeProvider(FakeProvider.Ok("x"));
        await new OpenAiClient(f, ProviderProfiles.OpenRouter(), "k").CompleteAsync(Hi with { Model = "google/gemma-4-31b-it:free" }, null, default);
        Assert.Equal("google/gemma-4-31b-it:free", JsonDocument.Parse(f.Seen[0].Body).RootElement.GetProperty("model").GetString());
    }

    // ---- fix 1: the warm-up spends no quota ----

    [Fact]
    public async Task Warm_GroqGetsModels_OpenRouterGetsKey_NoChat()
    {
        var f = new FakeProvider { Always = new FakeProvider.Answer(HttpStatusCode.OK, "{}") };
        Assert.True(await KeyCheck.WarmAsync(f, ProviderProfiles.Groq(), "k", default));
        Assert.True(await KeyCheck.WarmAsync(f, ProviderProfiles.OpenRouter(), "k", default));
        Assert.EndsWith("/models", f.Seen[0].Url);
        Assert.EndsWith("/key", f.Seen[1].Url);
        Assert.All(f.Seen, s => Assert.Equal(HttpMethod.Get, s.Req.Method));
    }

    [Fact]
    public async Task Warm_AnyAnswerIsReachable_TransportFailureIsNot()
    {
        Assert.True(await KeyCheck.WarmAsync(new FakeProvider(FakeProvider.Status(401)), ProviderProfiles.Groq(), "k", default));
        Assert.False(await KeyCheck.WarmAsync(new FakeProvider(FakeProvider.Fail(HttpRequestError.NameResolutionError)), ProviderProfiles.Groq(), "k", default));
        Assert.False(await KeyCheck.WarmAsync(new ConnectTimeoutHandler(), ProviderProfiles.Groq(), "k", default));
    }
}
