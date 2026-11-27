using System.Diagnostics;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;

namespace Sahar.Mind;

/// <summary>
/// The Mind's online brain (spec 0.17 §4): the sealed keys, today's quota, the persisted turns, the chain from settings
/// (read each time: no restart), the key test, the short bench, and the status the engine and the page show. Online
/// requests go through the system proxy (his VPN); «محلي فقط» sends nothing at all. Logs provider ids, codes and
/// numbers only — never a key, a word, or a provider's body.
/// </summary>
internal sealed class OnlineHost(Func<SaharSettings> settings, Action<MindMsg> send)
{
    // The system proxy (UseProxy defaults to the system's settings); a fresh connection now and then (his VPN may change).
    // Review: no redirects (a key never follows one), and a short connect (his VPN down must not cost 3 × 10 s).
    private readonly SocketsHttpHandler _http = new() { UseProxy = true, AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(6) };
    private readonly Dictionary<string, OpenAiClient> _clients = [];
    private ISecretBox? _box;
    private OnlineState? _state;
    private TurnHistory? _history;
    private string? _test, _testProvider;
    private OnlineBench? _bench;

    private ISecretBox Box => _box ??= MindParts.SecretBox(MindPaths.Personal);
    public OnlineState State => _state ??= new OnlineState(Box, () => DateTimeOffset.Now);
    public TurnHistory History => _history ??= new TurnHistory(Box, () => DateTimeOffset.Now, () => settings().MindEpisodeDays);
    private OnlineKeys Keys => new(Box);

    /// <summary>«انسي كل شي» or a store change: everything is read again from the (maybe new) box.</summary>
    public void Reset() { _box = null; _state = null; _history = null; }

    /// <summary>The keyed providers of his chain, in order, with their keys (empty with «محلي فقط» or no consent).</summary>
    public IReadOnlyList<(ProviderProfile P, string Key)> Chain()
    {
        var s = settings();
        var keys = Keys;
        var keyed = keys.Providers();
        return OnlineChain.From(s, keyed).Where(c => c.HasKey).Select(c => (c.P, keys.Get(c.P.Id)!)).ToList();
    }

    public bool AnyUsable(IReadOnlyList<(ProviderProfile P, string Key)> chain) => chain.Any(c => State.Usable(c.P.Id));

    /// <summary>One client per provider, model and key (review: reused, the old one disposed when any of them changes).</summary>
    public ILlm Client(ProviderProfile p, string key)
    {
        lock (_clients)
        {
            var tag = p.Model + "\n" + p.BaseUrl + "\n" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
            if (_clients.TryGetValue(p.Id, out var c) && c.Tag == tag) return c;
            c?.Dispose();
            return _clients[p.Id] = new OpenAiClient(_http, p, key) { Tag = tag };
        }
    }

    public OnlineTurn Turn(IPromptParts prompts, IMindStore store) => new(Client, prompts, store, State, History)
    {
        Log = kind => MindLog.Write($"online: {kind}"),
        PartialEvery = TimeSpan.FromMilliseconds(60),
    };

    /// <summary>engine → mind <c>online</c>: key | forget | test | status | bench (Model = the provider id; Text = the key, never logged).</summary>
    public async Task HandleAsync(MindMsg m, CancellationToken ct)
    {
        var provider = m.Model ?? "";
        switch (m.Op)
        {
            case "key" when !settings().MindOnlineConsent:
                _testProvider = provider;
                _test = OnlineErrors.NoConsent;   // review: no key before his «موافق»
                break;
            case "key":
                bool kept = Keys.Set(provider, m.Text);
                MindLog.Write($"online: key {(kept ? "sealed" : "refused")} ({provider})");
                if (kept) { _test = null; await TestAsync(provider, ct); }
                break;
            case "forget":
                Keys.Remove(provider);
                MindLog.Write($"online: key removed ({provider})");
                _test = null;
                break;
            case "test":
                await TestAsync(provider, ct);
                break;
            case "bench":
                await BenchAsync(ct);
                break;
            case "status":
                await RefreshOpenRouterAsync(ct);
                break;
        }
        SendStatus();
    }

    public void SendStatus() => send(new MindMsg { T = "online-status", Online = Info() });

    public OnlineInfo Info()
    {
        var s = settings();
        var keyed = Keys.Providers();
        var providers = ProviderProfiles.OnlineIds.Select(id =>
        {
            var p = OnlineChain.For(id, s);
            var (used, limit) = State.Usage(id);
            return new OnlineProvider(id, p?.Model ?? "", keyed.Contains(id), used, limit ?? p?.DailyLimit, State.Exhausted(id));
        }).ToList();
        return new OnlineInfo(providers, _test, _testProvider, _bench, s.MindLocalOnly);
    }

    private async Task TestAsync(string provider, CancellationToken ct)
    {
        _testProvider = provider;
        var s = settings();
        if (s.MindLocalOnly) { _test = OnlineErrors.LocalOnly; return; }
        if (!s.MindOnlineConsent) { _test = OnlineErrors.NoConsent; return; }
        if (Keys.Get(provider) is not { } key) { _test = OnlineErrors.NoKey; return; }
        if (OnlineChain.For(provider, s) is not { } p) { _test = OnlineErrors.BadOutput; return; }
        var r = await KeyCheck.RunAsync(_http, p, key, ct);
        _test = r.Code;
        if (r.Used is { } u && r.Limit is { } l) State.SetRemote(provider, u, l);
        else if (r.Code == OnlineErrors.Quota) State.Exhaust(provider);
        MindLog.Write($"online: key test {provider} {r.Code}");
    }

    /// <summary>OpenRouter's own counter (GET key costs no quota): at start, on the page, after its calls.</summary>
    public async Task RefreshOpenRouterAsync(CancellationToken ct)
    {
        var s = settings();
        if (s.MindLocalOnly || !s.MindOnlineConsent || Keys.Get(ProviderProfiles.OpenRouterId) is not { } key) return;
        try
        {
            var r = await KeyCheck.RunAsync(_http, ProviderProfiles.OpenRouter(s.MindOpenRouterModel), key, ct);
            if (r.Used is { } u && r.Limit is { } l) State.SetRemote(ProviderProfiles.OpenRouterId, u, l);
        }
        catch (Exception e) when (e is not OperationCanceledException) { MindLog.Write($"online: openrouter counter failed ({e.GetType().Name})"); }
    }

    /// <summary>«جرّب عقلي» (spec §9): at most 5 short requests to the first usable provider (they count in today's quota;
    /// the page says so before), first-word times and one sample line.</summary>
    private async Task BenchAsync(CancellationToken ct)
    {
        var chain = Chain().Where(c => State.Usable(c.P.Id)).ToList();
        if (settings().MindLocalOnly || chain.Count == 0) { _bench = new OnlineBench("", [], 0, 0, null, OnlineErrors.NoKey); return; }
        var (p, key) = chain[0];
        string[] asks = ["احكيلي جملة وحدة عن القمر.", "شو أحلى شي بالشتي؟ جملة وحدة.", "قوليلي نكتة قصيرة.", "كيف بتنصحيني أبلّش يومي؟ جملتين.", "اشرحيلي شو يعني ذكاء اصطناعي بسطرين."];
        var times = new List<int>();
        int ok = 0;
        string? sample = null, error = null;
        foreach (var a in asks)
        {
            State.Count(p.Id);
            var sw = Stopwatch.StartNew();
            int first = -1;
            try
            {
                var reply = await Client(p, key).CompleteAsync(new LlmRequest([new ChatMessage("user", a)], null, Sampling.Online with { MaxTokens = 120 }),
                    _ => { if (first < 0) first = (int)sw.ElapsedMilliseconds; }, ct);
                if (p.Quota == QuotaKind.Headers) State.Seen(p.Id, reply.Rate);
                times.Add(first < 0 ? (int)sw.ElapsedMilliseconds : first);
                ok++;
                sample ??= reply.Text.Trim() is var t && t.Length > 200 ? t[..200] + "…" : reply.Text.Trim();
            }
            catch (MindRuntimeException e)
            {
                error = e.Code;
                break;   // review: the first failure ends the bench (a 429 is never "done for the day" here)
            }
        }
        _bench = new OnlineBench(p.Id, times, ok, asks.Length, sample, error);
        MindLog.Write($"online: bench {p.Id} {ok}/{asks.Length} first={string.Join(',', times)}ms{(error is null ? "" : " " + error)}");
    }
}
