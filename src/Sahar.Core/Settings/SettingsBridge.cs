using System.Globalization;
using System.Text;
using System.Text.Json;
using Sahar.Core.Menu;
using Sahar.Core.Prayer;

namespace Sahar.Core.Settings;

/// <summary>A place the engine's geocoder found.</summary>
public sealed record GeoPlace(string Name, double Lat, double Lon);

/// <summary>The geocoder's answer: a place, or the reason in her words (plan 0.10c Task 3: the reason travels too).</summary>
public sealed record CityResult(GeoPlace? Place, string? Reason);

/// <summary>What erasing did: the names still there (in use; they go at her next start), and whether she came back.</summary>
public sealed record EraseOutcome(IReadOnlyList<string> Left, bool Restarted);

/// <summary>The engine, seen from the settings app (the "sahar" pipe). Every call fails quietly when she is not running.</summary>
public interface IEngineLink
{
    bool Running { get; }
    /// <summary>settings-reload; <paramref name="askHello"/>: she asks your name again (after «نبلّش من جديد»).</summary>
    Task<bool> ReloadAsync(bool askHello = false);
    Task<(bool Ok, string? Reason)> TryShortcutAsync(string value);
    Task<CityResult> FindCityAsync(string text);
    Task<bool> ClearClipboardAsync();
    /// <summary>She erases her data and quits, and is started again; null when she could not be asked.</summary>
    Task<EraseOutcome?> EraseAllAsync();
}

/// <summary>Windows location (an OS service, not a page request).</summary>
public interface ILocator
{
    Task<(double Lat, double Lon)?> LocateAsync();
}

/// <summary>
/// The settings page's messages (plan 0.10c Task 3): JSON in, JSON out, every reply echoes the request's id. Reads and
/// writes settings.json only through <see cref="SettingsStore"/> (field by field: <see cref="SettingsStore.Update"/>),
/// validates every edit with <see cref="SettingsPatch"/>, and tells the engine. Never throws.
/// </summary>
public sealed class SettingsBridge(string settingsPath, IEngineLink engine, ILocator locator, Func<DateTimeOffset> now, string dataFolder)
{
    /// <summary>How the file is read for its health (tests hold it "in use").</summary>
    public Func<string, string>? Read { get; init; }

    public const string NoEngineCity = "شغّلني أول لدوّر عالمدينة.";
    public const string WindowsLocation = "موقع الويندوز";
    public const string Unreadable = "unreadable";

    /// <summary>The pages shown (plan Ruling 1: الأصوات and Claude Code stay hidden until they work).</summary>
    public static IReadOnlyList<string> Pages { get; } = ["sahar", "menu", "prayer", "listen", "activities", "general", "privacy", "about"];

    /// <summary>0.13: the engine's exe (Claude Code's hooks run it) and Claude Code's settings file.</summary>
    public string? EnginePath { get; init; }
    public string ClaudeSettingsPath { get; init; } = Activities.ClaudeLinkFile.DefaultPath;

    private static readonly Prayer.Prayer[] Five =
        [Prayer.Prayer.Fajr, Prayer.Prayer.Dhuhr, Prayer.Prayer.Asr, Prayer.Prayer.Maghrib, Prayer.Prayer.Isha];

    public async Task<string> HandleAsync(string requestJson)
    {
        JsonElement req;
        try { req = JsonDocument.Parse(requestJson).RootElement.Clone(); }
        catch (JsonException) { return Reply(null, "error", w => Fail(w, "bad-request")); }
        if (req.ValueKind != JsonValueKind.Object) return Reply(null, "error", w => Fail(w, "bad-request"));
        long? id = req.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var n) ? n : null;
        string type = req.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "";
        try
        {
            return type switch
            {
                "get" => Get(id),
                "set" => Set(id, req),
                "city" => await City(id, req),
                "locate" => await Locate(id),
                "prayer-today" => PrayerToday(id),
                "shortcut" => await Shortcut(id, req),
                "clipboard-clear" => Reply(id, "clipboard-clear", w => { }, await engine.ClearClipboardAsync()),
                "erase" => await Erase(id),
                "reset" => await Reset(id),
                "claude-status" => Claude(id, "claude-status", null),
                "claude-link" => Claude(id, "claude-link", j => Activities.ClaudeLink.Install(j, EnginePath!)),
                "claude-unlink" => Claude(id, "claude-unlink", j => Activities.ClaudeLink.Remove(j, EnginePath!)),
                "weather-prayer" => await WeatherPrayer(id),
                _ => Reply(id, "error", w => Fail(w, "bad-type")),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Reply(id, type, w => Fail(w, Unreadable));
        }
    }

    // ---- messages ----

    private string Get(long? id)
    {
        // Only a file that is not settings JSON is "broken"; one held by another program for a moment is not.
        var health = SettingsStore.Health(settingsPath, Read);
        bool broken = health == SettingsHealth.Unreadable;
        SaharSettings s;
        try { s = health == SettingsHealth.Ok ? SettingsStore.Load(settingsPath) : new SaharSettings(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { s = new SaharSettings(); }
        return Reply(id, "settings", w =>
        {
            Value(w, s);
            w.WriteStartObject("caps");
            w.WriteBoolean("engine", engine.Running);
            w.WriteBoolean("broken", broken);
            w.WriteBoolean("busy", health == SettingsHealth.Busy);   // held for a moment: the page keeps what it knew
            w.WriteStartArray("pages");
            foreach (var p in Pages) w.WriteStringValue(p);
            w.WriteEndArray();
            w.WriteStartArray("reciters");
            foreach (var r in Quran.Quran.Reciters)
            {
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteString("name", r.Name);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("methods");
            foreach (var m in Enum.GetValues<PrayerMethod>())
            {
                w.WriteStartObject();
                w.WriteString("id", Camel(m.ToString()));
                w.WriteString("name", PrayerNames.Method(m));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("items");
            foreach (var i in MenuCatalog.ItemIds) w.WriteStringValue(i);
            w.WriteEndArray();
            w.WriteStartObject("prayerNames");
            foreach (var p in Five) w.WriteString(Camel(p.ToString()), PrayerNames.Arabic(p));
            w.WriteEndObject();
            w.WriteString("version", CoreInfo.Version);
            w.WriteEndObject();
            w.WriteStartArray("pairs");
            foreach (var (male, female) in Address.Pairs)
            {
                w.WriteStartArray();
                w.WriteStringValue(male);
                w.WriteStringValue(female);
                w.WriteEndArray();
            }
            w.WriteEndArray();
        });
    }

    private string Set(long? id, JsonElement req)
    {
        string key = req.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : "";
        if (!req.TryGetProperty("value", out var value)) return Saved(id, key, false, SettingsPatch.BadValue, null);
        if (!SettingsStore.Readable(settingsPath)) return Saved(id, key, false, Unreadable, null);
        // Checked first against the file as it is, so a refused edit never writes; then applied to a fresh read.
        SettingsPatch.Apply(SettingsStore.Load(settingsPath), key, value, out var error);
        if (error is not null) return Saved(id, key, false, error, null);
        if (!SettingsStore.TryUpdate(settingsPath, s => SettingsPatch.Apply(s, key, value, out _), out var saved))
            return Saved(id, key, false, Unreadable, null);
        _ = engine.ReloadAsync();
        return Saved(id, key, true, null, saved);
    }

    private async Task<string> City(long? id, JsonElement req)
    {
        string text = req.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString()! : "";
        bool weather = req.TryGetProperty("for", out var f) && f.ValueKind == JsonValueKind.String && f.GetString() == "weather";
        if (!engine.Running) return Reply(id, "city", w => w.WriteString("reason", NoEngineCity), false);
        if (string.IsNullOrWhiteSpace(text)) return Reply(id, "city", w => w.WriteString("reason", "اكتب اسم مدينتك."), false);
        var found = await engine.FindCityAsync(text);
        if (found.Place is not { } p) return Reply(id, "city", w => w.WriteString("reason", found.Reason ?? "ما لقيت هالمدينة."), false);
        if (!SettingsStore.TryUpdate(settingsPath, s => weather
                ? s with { WeatherCity = p.Name, WeatherLatitude = p.Lat, WeatherLongitude = p.Lon, WeatherEnabled = true }
                : s with { PrayerCity = p.Name, PrayerLatitude = p.Lat, PrayerLongitude = p.Lon }, out var saved))
            return Reply(id, "city", w => Fail(w, Unreadable));
        _ = engine.ReloadAsync();
        return Reply(id, "city", w =>
        {
            w.WriteString("name", p.Name);
            Value(w, saved!);
        }, true);
    }

    /// <summary>«اربطها / افصلها» (0.13 §2 Install): Claude Code's settings.json, backed up, ours only; the status is
    /// read from the file, never stored.</summary>
    private string Claude(long? id, string type, Func<string?, Activities.LinkResult>? change)
    {
        if (EnginePath is null) return Reply(id, type, w => Fail(w, "no-engine"));
        if (change is not null)
        {
            var r = Activities.ClaudeLinkFile.Apply(ClaudeSettingsPath, change);
            if (!r.Ok) return Reply(id, type, w => Fail(w, r.Error ?? "invalid"));
        }
        bool linked = Activities.ClaudeLinkFile.Linked(ClaudeSettingsPath, EnginePath);
        return Reply(id, type, w => w.WriteBoolean("linked", linked), true);
    }

    /// <summary>The weather goes back to the prayer place (owner ruling 4): its own city is forgotten.</summary>
    private async Task<string> WeatherPrayer(long? id)
    {
        if (!SettingsStore.TryUpdate(settingsPath, s => s with { WeatherCity = null, WeatherLatitude = null, WeatherLongitude = null }, out var saved))
            return Reply(id, "weather-prayer", w => Fail(w, Unreadable));
        await engine.ReloadAsync();
        return Reply(id, "weather-prayer", w => Value(w, saved!), true);
    }

    private async Task<string> Locate(long? id)
    {
        var at = await locator.LocateAsync();
        if (at is not { } where) return Reply(id, "located", w => { }, false);
        if (!SettingsStore.TryUpdate(settingsPath, s => s with { PrayerCity = WindowsLocation, PrayerLatitude = where.Lat, PrayerLongitude = where.Lon }, out var saved))
            return Reply(id, "located", w => Fail(w, Unreadable));
        _ = engine.ReloadAsync();
        return Reply(id, "located", w => Value(w, saved!), true);
    }

    private string PrayerToday(long? id)
    {
        var s = SettingsStore.Readable(settingsPath) ? SettingsStore.Load(settingsPath) : new SaharSettings();
        if (s.PrayerLatitude is not { } lat || s.PrayerLongitude is not { } lon) return Reply(id, "prayer", w => { }, false);
        var at = now();
        var schedule = new PrayerSchedule(lat, lon, s.PrayerMethod, s.PrayerHanafiAsr, _ => at.Offset);
        var day = schedule.Day(DateOnly.FromDateTime(at.Date), at);
        var next = schedule.Next(at);
        return Reply(id, "prayer", w =>
        {
            w.WriteStartArray("times");
            foreach (var p in Five)
            {
                w.WriteStartObject();
                w.WriteString("prayer", Camel(p.ToString()));
                w.WriteString("at", day[p].ToOffset(at.Offset).ToString("HH:mm", CultureInfo.InvariantCulture));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (next is { } x)
            {
                w.WriteStartObject("next");
                w.WriteString("prayer", Camel(x.Prayer.ToString()));
                w.WriteNumber("seconds", (int)Math.Ceiling((x.At - at).TotalSeconds));
                w.WriteEndObject();
            }
            w.WriteString("city", s.PrayerCity);
        }, true);
    }

    private async Task<string> Shortcut(long? id, JsonElement req)
    {
        string value = req.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var check = Shortcuts.Check(value);
        if (!check.Ok) return Reply(id, "shortcut", w => w.WriteString("reason", check.Reason), false);
        string text = check.Value.ToString();
        if (engine.Running)
        {
            var (ok, reason) = await engine.TryShortcutAsync(text);
            if (!ok) return Reply(id, "shortcut", w => w.WriteString("reason", reason ?? Shortcuts.Taken), false);
        }
        if (!SettingsStore.TryUpdate(settingsPath, s => s with { MenuShortcut = text }, out var saved))
            return Reply(id, "shortcut", w => Fail(w, Unreadable));
        _ = engine.ReloadAsync();
        return Reply(id, "shortcut", w => Value(w, saved!), true);
    }

    private async Task<string> Reset(long? id)
    {
        // Asked again first: a file that reads fine now (the other program fixed it, a lock passed) is never reset.
        if (SettingsStore.Health(settingsPath, Read) == SettingsHealth.Ok)
            return Reply(id, "reset", w => Value(w, SettingsStore.Load(settingsPath)), true);
        var fresh = SettingsStore.Reset(settingsPath);
        await engine.ReloadAsync(askHello: true);
        return Reply(id, "reset", w => Value(w, fresh), true);
    }

    /// <summary>«امسح كل شي عني»: she erases and is started again; without her, the app erases the folder itself. The
    /// reply names what is still there (in use: it goes at her next start).</summary>
    private async Task<string> Erase(long? id)
    {
        EraseOutcome? outcome = engine.Running ? await engine.EraseAllAsync() : null;
        outcome ??= new EraseOutcome(DataEraser.Erase(dataFolder, [CleanStart.MarkerName]).Left, Restarted: false);
        return Reply(id, "erase", w =>
        {
            w.WriteStartArray("left");
            foreach (var name in outcome.Left) w.WriteStringValue(name);
            w.WriteEndArray();
            w.WriteBoolean("restarted", outcome.Restarted);
        }, true);
    }

    // ---- writing replies ----

    private static string Saved(long? id, string key, bool ok, string? error, SaharSettings? value) => Reply(id, "saved", w =>
    {
        w.WriteString("key", key);
        if (error is not null) w.WriteString("error", error);
        if (value is not null) Value(w, value);
    }, ok);

    private static void Fail(Utf8JsonWriter w, string error)
    {
        w.WriteBoolean("ok", false);
        w.WriteString("error", error);
    }

    private static void Value(Utf8JsonWriter w, SaharSettings s)
    {
        w.WritePropertyName("value");
        w.WriteRawValue(SettingsStore.ToJson(s));
    }

    /// <summary>{"id":…,"type":…,"ok":…, …}. <paramref name="ok"/> null: the body writes "ok" itself.</summary>
    private static string Reply(long? id, string type, Action<Utf8JsonWriter> body, bool? ok = null)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            if (id is { } i) w.WriteNumber("id", i);
            else w.WriteNull("id");
            w.WriteString("type", type);
            if (ok is { } o) w.WriteBoolean("ok", o);
            body(w);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Camel(string pascal) => pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
}
