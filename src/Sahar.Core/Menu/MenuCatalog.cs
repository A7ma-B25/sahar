namespace Sahar.Core.Menu;

/// <summary>One thing on the menu card: what it does (id), its label in her voice, and its icon (a <see cref="MenuIcons"/> key).</summary>
public sealed record MenuEntry(string Id, string Label, string Icon);

/// <summary>How a drill-in shows its options: a segmented track (short choices) or full-width rows.</summary>
public enum DrillStyle { Segmented, List }

/// <summary>The card's second view for an item (spec §5): a title row with "back", the options, and an optional note.</summary>
public sealed record MenuDrill(string Id, string Title, DrillStyle Style, IReadOnlyList<MenuEntry> Options, string? Note);

/// <summary>What the drills depend on.</summary>
public sealed record MenuState(bool TimerRunning, bool HushOn, bool QuranPlaying);

/// <summary>
/// The menu card's items (spec §5; plan 0.10b Ruling 1: the ids are the radial menu's action ids, so the App's
/// switch and the UI stages keep them). The card shows her three tiles, four quiet tools, the chat row and a footer.
/// </summary>
public static class MenuCatalog
{
    /// <summary>The items the settings app can switch on or off, in the card's order.</summary>
    public static IReadOnlyList<string> ItemIds { get; } = ["chat", "play", "dnd", "hide", "timer", "note", "listen", "clipboard"];

    public static IReadOnlyList<MenuEntry> Tiles(bool hushOn) =>
    [
        new("play", "نلعب؟", "ball"),
        new("dnd", hushOn ? "رجّعيني احكي" : "بسكت شوي", "bud"),
        new("hide", "بغفى شوي", "moonZ"),
    ];

    public static IReadOnlyList<MenuEntry> Tools { get; } =
    [
        new("timer", "مؤقت", "clock"),
        new("note", "ملاحظة", "page"),
        new("listen", "استماع", "headphones"),
        new("clipboard", "الحافظة", "board"),
    ];

    public static MenuEntry Settings { get; } = new("settings", "إعداداتي", "gear");
    public static MenuEntry Back { get; } = new("back", "رجوع", "back");

    /// <summary>The items shown: the chosen ones (null = all; plan Ruling 2) whose feature is available; unknown ids are ignored.</summary>
    public static IReadOnlySet<string> Visible(IReadOnlyList<string>? chosen, Func<string, bool> available)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ItemIds)
            if ((chosen is null || chosen.Contains(id)) && available(id)) set.Add(id);
        return set;
    }

    /// <summary>The drill an item opens, or null when the item acts at once (the hush tile while hushed turns it off).</summary>
    public static MenuDrill? Drill(string id, MenuState s) => id switch
    {
        "play" => new("play", "شو بدك نلعب؟", DrillStyle.List,
        [
            new("game-chase", "الحقني", "ball"), new("game-hide", "غمّضلي", "ball"), new("game-ball", "الطابة", "ball"),
            new("game-stars", "لقط النجوم", "ball"), new("play-random", "حركة عشوائية", "ball"),
        ], null),
        "dnd" when s.HushOn => null,
        "dnd" => new("dnd", "قديش بدك ياني اسكت؟", DrillStyle.Segmented,
        [
            new("dnd-30", "نص ساعة", "bud"), new("dnd-60", "ساعة", "bud"), new("dnd-120", "ساعتين", "bud"), new("dnd-on", "لحد ما تقلي", "bud"),
        ], "رح وقّف تعليقاتي، بس بضل ذكّرك بالصلاة والمؤقت."),
        "timer" when s.TimerRunning => new("timer", "المؤقت شغّال", DrillStyle.List, [new("timer-cancel", "وقّف المؤقت", "clock")], null),
        "timer" => new("timer", "قديش المؤقت؟", DrillStyle.Segmented,
        [
            new("timer-5", "٥ د", "clock"), new("timer-15", "١٥ د", "clock"), new("timer-25", "٢٥ د", "clock"), new("timer-45", "٤٥ د", "clock"),
            new("timer-custom", "وقت تاني", "clock"),
        ], null),
        "listen" when s.QuranPlaying => new("listen", "شو بدك نسمع؟", DrillStyle.List,
            [new("quran-stop", "وقّف القرآن", "headphones"), new("music", "موسيقى", "headphones")], null),
        "listen" => new("listen", "شو بدك نسمع؟", DrillStyle.List,
            [new("music", "موسيقى", "headphones"), new("quran", "قرآن", "headphones")], null),
        "music" => new("music", "الموسيقى", DrillStyle.Segmented,
            [new("music-prev", "السابق", "headphones"), new("music-toggle", "تشغيل/إيقاف", "headphones"), new("music-next", "التالي", "headphones")], null),
        _ => null,
    };
}
