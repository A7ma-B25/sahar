namespace Sahar.Core.Mind.Tools;

/// <summary>
/// Every tool she may propose (spec §3.4), mapped to her real abilities in the engine. The engine announces the
/// subset it has now (<see cref="Core"/> always; <see cref="Activities"/> when the 0.13 sources are wired). There is
/// deliberately NO tool that answers Claude Code permission prompts, touches files, sends anything, installs, pays,
/// or changes Windows (spec §3.4 "tools that do not exist"; owner: never answers Claude Code permissions).
/// </summary>
public static class ToolCatalog
{
    /// <summary><c>settings.set</c> keys of her own world (look, liveliness, card): class A.</summary>
    public static IReadOnlyList<string> SettingsA { get; } =
        ["activity", "size", "innerStars", "autoMood", "playsAlone", "appearance", "menuHeader", "menuPlace"];

    /// <summary><c>settings.set</c> keys about privacy, network or how she reaches him: class B (always asks).</summary>
    public static IReadOnlyList<string> SettingsB { get; } =
    [
        "awarenessEnabled", "weatherEnabled", "language", "menuShortcut", "menuOpen", "menuItems", "prayerMethod",
        "prayerHanafiAsr", "prayerReminderOn", "prayerReminderMinutes", "quranReciter", "userName", "userGender",
    ];

    /// <summary>The tools handled inside the Mind (proposals into its store); the engine never runs them.</summary>
    public static IReadOnlySet<string> MindLocal { get; } = new HashSet<string>(StringComparer.Ordinal) { "memory.remember", "goal.add" };

    private static ArgSpec Int(string n, int min, int max, bool req = true) => new(n, ArgType.Int, req, min, max);
    private static ArgSpec Str(string n, int max, bool req = true) => new(n, ArgType.String, req, MaxLength: max);
    private static ArgSpec En(string n, bool req, params string[] values) => new(n, ArgType.Enum, req, Values: values);

    public static IReadOnlyList<ToolSpec> Core { get; } =
    [
        new("timer.start", "شغّلي مؤقت بالدقايق", [Int("minutes", 1, 600), Str("label", 40, false)], ActionClass.A, Undoable: true),
        new("timer.cancel", "وقّفي المؤقت", [], ActionClass.A),
        new("reminder.set", "ذكّريه بشي: بعد كم دقيقة أو بساعة محددة", [Int("inMinutes", 1, 1440, false), new("at", ArgType.Time, false), Str("text", 80)],
            ActionClass.A, Undoable: true),
        new("note.save", "احفظي ملاحظة", [Str("text", 500)], ActionClass.A, Undoable: true),
        new("note.open", "افتحي الملاحظات", [], ActionClass.A),
        new("hush.on", "اسكتي (بلا تعليقات) لمدة أو لحد ما يقلّك", [Int("minutes", 1, 600, false)], ActionClass.A, Undoable: true),
        new("hush.off", "رجعي احكي", [], ActionClass.A),
        new("sleep.hide", "اختفي شوي (افتراضي ٦٠ دقيقة)", [Int("minutes", 5, 240, false)], ActionClass.A, Undoable: true),
        new("game.start", "بلّشي لعبة", [En("game", true, "chase", "hide", "ball", "stars")], ActionClass.A),
        new("games.open", "افتحي قائمة الألعاب", [], ActionClass.A),
        new("move.play", "اعملي حركة", [En("move", true, "dance", "stretch", "hop")], ActionClass.A),
        new("prayer.next", "شو الصلاة الجاية وبعد قديش", [], ActionClass.Read, ReadOnly: true),
        new("prayer.open", "افتحي أوقات الصلاة", [], ActionClass.A),
        new("music.now", "شو عم يشتغل هلق", [], ActionClass.Read, ReadOnly: true),
        new("music.control", "تحكّمي بالموسيقى", [En("op", true, "toggle", "next", "prev", "pause")], ActionClass.A, Undoable: true),
        new("quran.open", "افتحي القرآن", [], ActionClass.A),
        new("quran.stop", "وقّفي القرآن", [], ActionClass.A),
        new("settings.set", "غيّري إعداد من إعداداتي", [new("key", ArgType.Enum, Values: [.. SettingsA, .. SettingsB]), new("value", ArgType.String)],
            ActionClass.A, Undoable: true),
        new("memory.remember", "اقترحي شي تتذكّريه عنه (هو بيأكّده)", [Str("text", 200), En("kind", true, "fact", "preference", "goal", "event")], ActionClass.A),
        new("goal.add", "اقترحي هدف (هو بيأكّده)", [Str("text", 120), En("checkEvery", false, "day", "week"), Str("until", 10, false)], ActionClass.A),
    ];

    /// <summary>0.13 activities: announced only when the engine has them (weather on, Claude Code's hooks, the shelf).</summary>
    public static IReadOnlyList<ToolSpec> Activities { get; } =
    [
        new("weather.now", "كيف الطقس هلق", [], ActionClass.Read, ReadOnly: true),
        new("claude.status", "Claude Code: كم جلسة شغّالة أو ناطرة (بلا محتوى)", [], ActionClass.Read, ReadOnly: true),
        new("shelf.open", "افتحي رفّي", [], ActionClass.A),
        new("shelf.tidy", "رتّبي رفّي", [], ActionClass.A),
        new("shelf.clear", "فضّي رفّي", [], ActionClass.B),
    ];

    public static IReadOnlyList<ToolSpec> All { get; } = [.. Core, .. Activities];

    public static ToolSpec? Get(string name) => All.FirstOrDefault(t => t.Name == name);
}
