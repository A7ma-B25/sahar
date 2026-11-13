using System.Text.Json;
using Sahar.Core.Menu;
using Sahar.Core.Prayer;

namespace Sahar.Core.Settings;

/// <summary>
/// One edit from the settings page (plan 0.10c Task 1): a key as in settings.json (camelCase) and a JSON value. Every
/// value is checked; a refused edit returns the settings unchanged with an error: <c>bad-key</c>, <c>bad-value</c>,
/// <c>needs-city</c> (weather on without a city), or the shortcut's own reason in her voice.
/// </summary>
public static class SettingsPatch
{
    public const string BadKey = "bad-key";
    public const string BadValue = "bad-value";
    public const string NeedsCity = "needs-city";

    /// <summary>The prayer reminder's choices on the page (٥ / ١٠ / ١٥ / ٢٠).</summary>
    public static IReadOnlyList<int> ReminderMinutes { get; } = [5, 10, 15, 20];

    public static IReadOnlyList<string> Keys { get; } =
    [
        "userName", "userGender", "size", "activity", "awarenessEnabled", "autoMood", "playsAlone", "weatherEnabled",
        "innerStars", "menuItems", "menuHeader", "menuOpen", "menuPlace", "menuShortcut", "appearance", "language",
        "prayerMethod", "prayerHanafiAsr", "prayerReminderOn", "prayerReminderMinutes", "quranReciter",
        "actMusic", "actWeather", "actClaude", "actShelf", "actSystem", "actMicCam", "actCopy", "actAway", "actScreenTime",
        "lyricsEnabled", "lyricsConsented", "musicPausedKeep", "shelfScreenshots",
        "mindEnabled", "mindUnloadMinutes", "mindInitiative", "mindSelfActions", "mindVoice", "mindBackend", "mindUseGpu",
        "mindLearnChats", "mindLearnImports", "mindLearnExamples", "mindLearnMusic", "mindEpisodeDays",
    ];

    /// <summary>«فرّغي عقلي بعد» choices, and how long chat episodes are kept (spec §8, §7.3).</summary>
    public static IReadOnlyList<int> UnloadMinutes { get; } = [1, 5, 15, 30];
    public static IReadOnlyList<int> EpisodeDays { get; } = [7, 30, 90];

    public static SaharSettings Apply(SaharSettings s, string key, JsonElement value, out string? error)
    {
        error = null;
        SaharSettings? next = key switch
        {
            "userName" => value.ValueKind == JsonValueKind.String ? s with { UserName = Address.CleanName(value.GetString()!) } : null,
            "userGender" => Enum<Gender>(value) is { } g ? s with { UserGender = g } : null,
            "size" => Enum<SaharSize>(value) is { } z ? s with { Size = z } : null,
            "activity" => Enum<ActivityLevel>(value) is { } a ? s with { Activity = a } : null,
            "awarenessEnabled" => Bool(value) is { } b1 ? s with { AwarenessEnabled = b1 } : null,
            "autoMood" => Bool(value) is { } b2 ? s with { AutoMood = b2 } : null,
            "playsAlone" => Bool(value) is { } b3 ? s with { PlaysAlone = b3 } : null,
            "weatherEnabled" => Bool(value) is { } b4 ? Weather(s, b4, out error) : null,
            "innerStars" => Bool(value) is { } b5 ? s with { InnerStars = b5 } : null,
            "menuItems" => Items(value) is { } items ? s with { MenuItems = items } : null,
            "menuHeader" => Enum<MenuHeader>(value) is { } h ? s with { MenuHeader = h } : null,
            "menuOpen" => Enum<MenuOpen>(value) is { } o ? s with { MenuOpen = o } : null,
            "menuPlace" => Enum<MenuPlace>(value) is { } p ? s with { MenuPlace = p } : null,
            "menuShortcut" => value.ValueKind == JsonValueKind.String ? Shortcut(s, value.GetString()!, out error) : null,
            "appearance" => Enum<Appearance>(value) is { } ap ? s with { Appearance = ap } : null,
            "language" => Enum<UiLanguage>(value) is { } l ? s with { Language = l } : null,
            "prayerMethod" => Enum<PrayerMethod>(value) is { } m ? s with { PrayerMethod = m } : null,
            "prayerHanafiAsr" => Bool(value) is { } b6 ? s with { PrayerHanafiAsr = b6 } : null,
            "prayerReminderOn" => Bool(value) is { } b7 ? s with { PrayerReminderOn = b7 } : null,
            "prayerReminderMinutes" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) && ReminderMinutes.Contains(n)
                ? s with { PrayerReminderMinutes = n } : null,
            "quranReciter" => value.ValueKind == JsonValueKind.String && Sahar.Core.Quran.Quran.Reciters.Any(r => r.Id == value.GetString())
                ? s with { QuranReciter = value.GetString()! } : null,
            "actMusic" => Bool(value) is { } a1 ? s with { ActMusic = a1 } : null,
            "actWeather" => Bool(value) is { } a2 ? s with { ActWeather = a2 } : null,
            "actClaude" => Bool(value) is { } a3 ? s with { ActClaude = a3 } : null,
            "actShelf" => Bool(value) is { } a4 ? s with { ActShelf = a4 } : null,
            "actSystem" => Bool(value) is { } a5 ? s with { ActSystem = a5 } : null,
            "actMicCam" => Bool(value) is { } a6 ? s with { ActMicCam = a6 } : null,
            "actCopy" => Bool(value) is { } a7 ? s with { ActCopy = a7 } : null,
            "actAway" => Bool(value) is { } a8 ? s with { ActAway = a8 } : null,
            "actScreenTime" => Bool(value) is { } a9 ? s with { ActScreenTime = a9 } : null,
            "lyricsEnabled" => Bool(value) is { } l1 ? s with { LyricsEnabled = l1 } : null,
            "lyricsConsented" => Bool(value) is { } l2 ? s with { LyricsConsented = l2 } : null,
            "musicPausedKeep" => Enum<Sahar.Core.Activities.PausedKeep>(value) is { } pk ? s with { MusicPausedKeep = pk } : null,
            "shelfScreenshots" => Bool(value) is { } sh ? s with { ShelfScreenshots = sh } : null,
            "mindEnabled" => Bool(value) is { } m1 ? s with { MindEnabled = m1 } : null,
            "mindUnloadMinutes" => Int(value) is { } um && UnloadMinutes.Contains(um) ? s with { MindUnloadMinutes = um } : null,
            "mindInitiative" => Enum<Sahar.Core.Mind.Initiative.InitiativeLevel>(value) is { } il ? s with { MindInitiative = il } : null,
            "mindSelfActions" => Bool(value) is { } m2 ? s with { MindSelfActions = m2 } : null,
            "mindVoice" => Enum<Sahar.Core.Mind.Memory.MindVoice>(value) is { } mv ? s with { MindVoice = mv } : null,
            "mindBackend" => Enum<Sahar.Core.Mind.Runtime.Backend>(value) is { } mb ? s with { MindBackend = mb } : null,
            "mindUseGpu" => Bool(value) is { } m3 ? s with { MindUseGpu = m3 } : null,
            "mindLearnChats" => Bool(value) is { } m4 ? s with { MindLearnChats = m4 } : null,
            "mindLearnImports" => Bool(value) is { } m5 ? s with { MindLearnImports = m5 } : null,
            "mindLearnExamples" => Bool(value) is { } m6 ? s with { MindLearnExamples = m6 } : null,
            "mindLearnMusic" => Bool(value) is { } m7 ? s with { MindLearnMusic = m7 } : null,
            "mindEpisodeDays" => Int(value) is { } ed && EpisodeDays.Contains(ed) ? s with { MindEpisodeDays = ed } : null,
            _ => Unknown(out error),
        };
        if (next is null)
        {
            error ??= BadValue;
            return s;
        }
        return error is null ? next : s;
    }

    private static SaharSettings? Unknown(out string? error)
    {
        error = BadKey;
        return null;
    }

    private static SaharSettings Weather(SaharSettings s, bool on, out string? error)
    {
        // Owner ruling 4 (0.13): the prayer place is enough (the weather falls back to it).
        error = on && Sahar.Core.Activities.WeatherCity.Resolve(s) is null ? NeedsCity : null;
        return s with { WeatherEnabled = on };
    }

    private static SaharSettings Shortcut(SaharSettings s, string text, out string? error)
    {
        var check = Shortcuts.Check(text);
        error = check.Ok ? null : check.Reason;
        return check.Ok ? s with { MenuShortcut = check.Value.ToString() } : s;
    }

    private static int? Int(JsonElement v) => v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;

    private static bool? Bool(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>An enum by its name, any case ("large", "muslimWorldLeague"); never a number.</summary>
    private static T? Enum<T>(JsonElement v) where T : struct, System.Enum
    {
        if (v.ValueKind != JsonValueKind.String) return null;
        var text = v.GetString();
        if (string.IsNullOrEmpty(text) || !char.IsLetter(text[0])) return null;
        return System.Enum.TryParse<T>(text, ignoreCase: true, out var e) && System.Enum.IsDefined(e) ? e : null;
    }

    /// <summary>A list of item ids: unknown ids and duplicates dropped, in the card's order kept as given.</summary>
    private static IReadOnlyList<string>? Items(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Array) return null;
        var list = new List<string>();
        foreach (var e in v.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) return null;
            var id = e.GetString()!;
            if (MenuCatalog.ItemIds.Contains(id) && !list.Contains(id)) list.Add(id);
        }
        return list;
    }
}
