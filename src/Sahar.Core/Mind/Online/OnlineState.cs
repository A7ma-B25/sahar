using System.Globalization;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Online;

/// <summary>
/// Today's online requests per provider (spec 0.17 §7): every attempt counts (OpenRouter counts failed ones too), the
/// provider's own numbers win when it gives them (Groq's headers, OpenRouter's key counter), a 429 after the retry marks
/// it out for the day, and "she told him the online brain is out" happens once a day. Kept in her SecretBox
/// (<c>online-state</c>; counts only, never words); a new local day starts empty. A box that refuses keeps counting in
/// memory. Thread-safe (the Mind's turn and the page's status ask may meet).
/// </summary>
public sealed class OnlineState(ISecretBox box, Func<DateTimeOffset> now)
{
    public const string File = "online-state";
    private readonly object _gate = new();
    private string? _day;
    private bool _told;
    private readonly Dictionary<string, Entry> _p = [];
    private bool _loaded;

    private sealed class Entry { public int Used; public int? Limit; public bool Out; }

    private string Today => now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Count(string provider) => Change(provider, e => e.Used++);

    /// <summary>The provider's own rate numbers (Groq: x-ratelimit-*-requests).</summary>
    public void Seen(string provider, RateInfo? rate)
    {
        if (rate is not { Limit: { } limit, Remaining: { } left }) return;
        Change(provider, e => { e.Limit = limit; e.Used = Math.Max(0, limit - left); });
    }

    /// <summary>OpenRouter's free_model_daily_requests (used / limit).</summary>
    public void SetRemote(string provider, int used, int limit) => Change(provider, e => { e.Used = used; e.Limit = limit; });

    public void Exhaust(string provider) => Change(provider, e => e.Out = true);

    public bool Exhausted(string provider)
    {
        lock (_gate)
        {
            Fresh();
            return _p.TryGetValue(provider, out var e) && (e.Out || e.Limit is { } l && e.Used >= l);
        }
    }

    public (int Used, int? Limit) Usage(string provider)
    {
        lock (_gate)
        {
            Fresh();
            return _p.TryGetValue(provider, out var e) ? (e.Used, e.Limit) : (0, null);
        }
    }

    /// <summary>True the first time today (she says the online brain is out once a day).</summary>
    public bool TellOnce()
    {
        lock (_gate)
        {
            Fresh();
            if (_told) return false;
            _told = true;
            Save();
            return true;
        }
    }

    private void Change(string provider, Action<Entry> change)
    {
        lock (_gate)
        {
            Fresh();
            if (!_p.TryGetValue(provider, out var e)) _p[provider] = e = new Entry();
            change(e);
            Save();
        }
    }

    private void Fresh()
    {
        if (!_loaded) { _loaded = true; Load(); }
        if (_day != Today) { _day = Today; _told = false; _p.Clear(); }
    }

    private void Load()
    {
        try
        {
            if (box.Read(File) is not { Length: > 0 } bytes) return;
            using var doc = JsonDocument.Parse(bytes);
            var r = doc.RootElement;
            _day = r.TryGetProperty("day", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            _told = r.TryGetProperty("told", out var t) && t.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.Object)
                foreach (var x in p.EnumerateObject())
                    _p[x.Name] = new Entry
                    {
                        Used = x.Value.TryGetProperty("used", out var u) && u.TryGetInt32(out int n) ? n : 0,
                        Limit = x.Value.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out int m) ? m : null,
                        Out = x.Value.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.True,
                    };
        }
        catch (JsonException) { _p.Clear(); }
    }

    private void Save()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("day", _day);
            w.WriteBoolean("told", _told);
            w.WriteStartObject("p");
            foreach (var (k, e) in _p)
            {
                w.WriteStartObject(k);
                w.WriteNumber("used", e.Used);
                if (e.Limit is { } l) w.WriteNumber("limit", l);
                w.WriteBoolean("out", e.Out);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        try { box.Write(File, ms.ToArray()); }
        catch (IOException) { }   // locked or busy: counted in memory, written next time
    }
}

/// <summary>
/// The online keys (spec 0.17 §4, research lesson 3): typed by him on the page, passed once to the Mind, sealed here
/// in her SecretBox (<c>online-keys</c>: AES-256-GCM under the DPAPI CurrentUser–wrapped key). Never in settings.json,
/// never logged. Only the Mind reads them.
/// </summary>
public sealed class OnlineKeys(ISecretBox box)
{
    public const string File = "online-keys";
    public const int MaxKey = 512;

    public bool Set(string provider, string? key)
    {
        if (!ProviderProfiles.OnlineIds.Contains(provider)) return false;
        var k = key?.Trim() ?? "";
        if (k.Length == 0 || k.Length > MaxKey || k.Any(char.IsControl) || k.Contains(' ')) return false;
        var all = Read();
        all[provider] = k;
        return Write(all);
    }

    public string? Get(string provider) => Read().GetValueOrDefault(provider);

    public void Remove(string provider)
    {
        var all = Read();
        if (all.Remove(provider)) Write(all);
    }

    public IReadOnlyList<string> Providers() => [.. Read().Keys];

    private Dictionary<string, string> Read()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (box.Read(File) is not { Length: > 0 } bytes) return d;
            using var doc = JsonDocument.Parse(bytes);
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String && ProviderProfiles.OnlineIds.Contains(p.Name)) d[p.Name] = p.Value.GetString()!;
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
        return d;
    }

    private bool Write(Dictionary<string, string> all)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (k, v) in all) w.WriteString(k, v);
            w.WriteEndObject();
        }
        try { box.Write(File, ms.ToArray()); return true; }
        catch (IOException) { return false; }
    }
}

/// <summary>«جرّب المفتاح» (spec §7): a request that spends no chat quota — OpenRouter's <c>GET key</c> (with today's
/// free counter), or <c>GET models</c> elsewhere. The provider's body is read only for those numbers.</summary>
public sealed record KeyResult(string Code, int? Used = null, int? Limit = null);

public static class KeyCheck
{
    public static async Task<KeyResult> RunAsync(HttpMessageHandler handler, ProviderProfile p, string key, CancellationToken ct)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = p.BaseUrl, Timeout = TimeSpan.FromSeconds(15) };
        using var req = new HttpRequestMessage(HttpMethod.Get, p.Quota == QuotaKind.OpenRouterKey ? "key" : "models");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        foreach (var (k, v) in p.Headers) req.Headers.TryAddWithoutValidation(k, v);
        try
        {
            using var r = await http.SendAsync(req, ct);
            if (OnlineErrors.FromStatus((int)r.StatusCode) is { } code) return new KeyResult(code == OnlineErrors.BadOutput ? OnlineErrors.Server : code);
            if (p.Quota != QuotaKind.OpenRouterKey) return new KeyResult(OnlineErrors.Ok);
            var (used, limit) = OpenRouterCounter(await r.Content.ReadAsByteArrayAsync(ct));
            return new KeyResult(used is { } u && limit is { } l && u >= l ? OnlineErrors.Quota : OnlineErrors.Ok, used, limit);
        }
        catch (HttpRequestException) { return new KeyResult(OnlineErrors.Offline); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new KeyResult(OnlineErrors.Timeout); }
    }

    /// <summary><c>data.free_model_daily_requests.{used,limit}</c>, or nulls.</summary>
    public static (int? Used, int? Limit) OpenRouterCounter(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var d) && d.TryGetProperty("free_model_daily_requests", out var f) && f.ValueKind == JsonValueKind.Object)
                return (f.TryGetProperty("used", out var u) && u.TryGetInt32(out int a) ? a : null, f.TryGetProperty("limit", out var l) && l.TryGetInt32(out int b) ? b : null);
        }
        catch (JsonException) { }
        return (null, null);
    }
}
