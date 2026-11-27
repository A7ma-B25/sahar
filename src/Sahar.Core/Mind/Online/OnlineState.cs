using System.Globalization;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Online;

/// <summary>
/// Today's online requests per provider (spec 0.17 §7): every attempt counts (OpenRouter counts failed ones too).
/// Review C1: "out for the day" comes ONLY from the provider's own daily numbers (Groq's x-ratelimit-*-requests for
/// <see cref="QuotaKind.Headers"/> providers; OpenRouter's key counter, whose day is UTC); any other 429 is a short
/// cooldown (Retry-After, else 45 s) held in memory. Review I5: after every provider failed to connect, online rests for
/// a minute (0.17.2: was 3; a timed-out or unreachable provider alone cools for <see cref="ProviderCooldown"/>). "She told him" happens once a day. Counts live in her SecretBox (<c>online-state</c>; numbers only); a
/// box that cannot be read now is never written over. Thread-safe.
/// </summary>
public sealed class OnlineState(ISecretBox box, Func<DateTimeOffset> now)
{
    public const string File = "online-state";
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(45), OfflineRest = TimeSpan.FromSeconds(60), ProviderCooldown = TimeSpan.FromSeconds(60);
    private readonly object _gate = new();
    private string? _day;
    private bool _told;
    private readonly Dictionary<string, Entry> _p = [];
    private readonly Dictionary<string, DateTimeOffset> _cool = [];
    private DateTimeOffset _offlineUntil = DateTimeOffset.MinValue;
    private bool _loaded, _readable;

    private sealed class Entry { public string Day = ""; public int Used; public int? Limit; public bool Out; }

    private string LocalDay => now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    /// <summary>OpenRouter's free counter resets at midnight UTC; the others follow his day.</summary>
    private string DayOf(string provider) => provider == ProviderProfiles.OpenRouterId
        ? now().UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : LocalDay;

    public void Count(string provider) => Change(provider, e => e.Used++);

    /// <summary>A provider's daily request numbers (Groq: x-ratelimit-*-requests). Only for header-quota providers.</summary>
    public void Seen(string provider, RateInfo? rate)
    {
        if (rate is not { Limit: { } limit, Remaining: { } left }) return;
        Change(provider, e => { e.Limit = limit; e.Used = Math.Max(0, limit - left); });
    }

    /// <summary>OpenRouter's free_model_daily_requests (used / limit).</summary>
    public void SetRemote(string provider, int used, int limit) => Change(provider, e => { e.Used = used; e.Limit = limit; });

    /// <summary>The provider said its day is over (a test or bench that saw it).</summary>
    public void Exhaust(string provider) => Change(provider, e => e.Out = true);

    /// <summary>A 429 that is not the day's end: skip this provider until <paramref name="retryAfter"/> (else 45 s).</summary>
    public void Cool(string provider, double? retryAfter)
    {
        lock (_gate) _cool[provider] = now() + (retryAfter is { } s && s > 0 ? TimeSpan.FromSeconds(Math.Min(s, 600)) : DefaultCooldown);
    }

    /// <summary>0.17.2: a provider that timed out or could not be reached: skipped for <paramref name="for"/> while the
    /// others are still tried (one slow provider is not "no internet").</summary>
    public void Cool(string provider, TimeSpan @for)
    {
        lock (_gate) _cool[provider] = now() + @for;
    }

    public bool Cooling(string provider)
    {
        lock (_gate) return _cool.TryGetValue(provider, out var until) && now() < until;
    }

    /// <summary>Every provider failed to connect: online rests a few minutes (no 3 × 12 s wait before each answer).</summary>
    public void GoOffline() { lock (_gate) _offlineUntil = now() + OfflineRest; }
    public bool Offline { get { lock (_gate) return now() < _offlineUntil; } }
    /// <summary>0.17.2: a provider answered (the warm-up): the rest ends now.</summary>
    public void BackOnline() { lock (_gate) _offlineUntil = DateTimeOffset.MinValue; }

    public bool Exhausted(string provider)
    {
        lock (_gate)
        {
            var e = Get(provider);
            return e is not null && (e.Out || e.Limit is { } l && e.Used >= l);
        }
    }

    /// <summary>Usable for a turn now: not out for the day, not cooling down, not resting offline.</summary>
    public bool Usable(string provider) => !Offline && !Exhausted(provider) && !Cooling(provider);

    public (int Used, int? Limit) Usage(string provider)
    {
        lock (_gate)
        {
            var e = Get(provider);
            return e is not null ? (e.Used, e.Limit) : (0, null);
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

    private Entry? Get(string provider)
    {
        Fresh();
        if (!_p.TryGetValue(provider, out var e)) return null;
        if (e.Day != DayOf(provider)) { _p.Remove(provider); return null; }
        return e;
    }

    private void Change(string provider, Action<Entry> change)
    {
        lock (_gate)
        {
            var e = Get(provider);
            if (e is null) _p[provider] = e = new Entry { Day = DayOf(provider) };
            change(e);
            Save();
        }
    }

    private void Fresh()
    {
        if (!_loaded) Load();
        if (_day != LocalDay) { _day = LocalDay; _told = false; }
    }

    private void Load()
    {
        if (!box.TryRead(File, out var bytes)) return;   // busy or locked: try again next time; never written over meanwhile
        _loaded = _readable = true;
        if (bytes is not { Length: > 0 }) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var r = doc.RootElement;
            _day = r.TryGetProperty("day", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
            _told = r.TryGetProperty("told", out var t) && t.ValueKind == JsonValueKind.True;
            if (r.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.Object)
                foreach (var x in p.EnumerateObject())
                    _p[x.Name] = new Entry
                    {
                        Day = x.Value.TryGetProperty("day", out var pd) && pd.ValueKind == JsonValueKind.String ? pd.GetString()! : _day ?? "",
                        Used = x.Value.TryGetProperty("used", out var u) && u.TryGetInt32(out int n) ? n : 0,
                        Limit = x.Value.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number && l.TryGetInt32(out int m) ? m : null,
                        Out = x.Value.TryGetProperty("out", out var o) && o.ValueKind == JsonValueKind.True,
                    };
        }
        catch (JsonException) { _p.Clear(); }
    }

    private void Save()
    {
        if (!_readable) return;   // the box could not be read: counted in memory only, the file is kept
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
                w.WriteString("day", e.Day);
                w.WriteNumber("used", e.Used);
                if (e.Limit is { } l) w.WriteNumber("limit", l);
                w.WriteBoolean("out", e.Out);
                w.WriteEndObject();
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        try { box.Write(File, ms.ToArray()); }
        catch (IOException) { }
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
        if (Read() is not { } all) return false;   // review: a busy box is never written over with only this key
        all[provider] = k;
        return Write(all);
    }

    public string? Get(string provider) => Read()?.GetValueOrDefault(provider);

    public void Remove(string provider)
    {
        if (Read() is { } all && all.Remove(provider)) Write(all);
    }

    public IReadOnlyList<string> Providers() => Read() is { } all ? [.. all.Keys] : [];

    /// <summary>The keys, or null when the box cannot be read this moment (busy, locked).</summary>
    private Dictionary<string, string>? Read()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!box.TryRead(File, out var bytes)) return null;
            if (bytes is not { Length: > 0 }) return d;
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
            if (OnlineErrors.FromStatus((int)r.StatusCode) is { } code) return new KeyResult(code is OnlineErrors.BadOutput or OnlineErrors.ModelBad ? OnlineErrors.Server : code);
            if (p.Quota != QuotaKind.OpenRouterKey)
                return new KeyResult(HasModel(await r.Content.ReadAsByteArrayAsync(ct), p.Model) == false ? OnlineErrors.ModelBad : OnlineErrors.Ok);
            var (used, limit) = OpenRouterCounter(await r.Content.ReadAsByteArrayAsync(ct));
            // Review: the model id too (OpenRouter's model list is public; a failed read leaves the answer to the key).
            using var models = new HttpRequestMessage(HttpMethod.Get, "models");
            using (var m = await http.SendAsync(models, ct))
                if (m.IsSuccessStatusCode && HasModel(await m.Content.ReadAsByteArrayAsync(ct), p.Model) == false) return new KeyResult(OnlineErrors.ModelBad, used, limit);
            return new KeyResult(used is { } u && limit is { } l && u >= l ? OnlineErrors.Quota : OnlineErrors.Ok, used, limit);
        }
        catch (HttpRequestException) { return new KeyResult(OnlineErrors.Offline); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new KeyResult(OnlineErrors.Timeout); }
    }

    /// <summary>0.17.2: opens the TLS connection before his first line (the chat window opened), on the same handler the
    /// chat uses so the pooled connection is warm. Spends no chat quota: OpenRouter's <c>GET key</c> (documented as not
    /// counted), <c>GET models</c> elsewhere (Groq's limits are per model, for its inference calls). True when the host
    /// answered at all (any status: the connection is up); false on a transport failure or after 15 s.</summary>
    public static async Task<bool> WarmAsync(HttpMessageHandler handler, ProviderProfile p, string key, CancellationToken ct)
    {
        using var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = p.BaseUrl, Timeout = TimeSpan.FromSeconds(15) };
        using var req = new HttpRequestMessage(HttpMethod.Get, p.Quota == QuotaKind.OpenRouterKey ? "key" : "models");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        foreach (var (k, v) in p.Headers) req.Headers.TryAddWithoutValidation(k, v);
        try
        {
            using var r = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            return true;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
    }

    /// <summary>Is <paramref name="model"/> in a models list (<c>data[].id</c>)? null when the body is not such a list.</summary>
    public static bool? HasModel(byte[] body, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Array || d.GetArrayLength() == 0) return null;
            foreach (var e in d.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() == model) return true;
            return false;
        }
        catch (JsonException) { return null; }
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
