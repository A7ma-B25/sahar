using Sahar.Core.Settings;

namespace Sahar.Core.Menu;

/// <summary>One button of the radial menu: what it does (id), its icon and its label.</summary>
public sealed record MenuItem(string Id, string Emoji, string Label);

/// <summary>
/// The radial menu's buttons (spec §7.1). Only the ones that work so far are shown (the user's choice,
/// 2026-11-06): Claude, listening, prayer, clipboard and settings join when their plans land.
/// </summary>
public static class MenuItems
{
    public static IReadOnlyList<MenuItem> Main { get; } =
    [
        new("timer", "⏱️", "مؤقت"),
        new("note", "📝", "ملاحظة سريعة"),
        new("activity", "🌗", "مستوى نشاطها"),
        new("dnd", "🔕", "لا تزعجني"),
        new("hide", "😴", "خبّيها ساعة"),
        new("play", "🎲", "العبي"),
    ];

    public static MenuItem Back { get; } = new("back", "↩️", "رجوع");

    /// <summary>The second ring a button opens; empty for buttons that act at once.</summary>
    public static IReadOnlyList<MenuItem> Options(string id, bool timerRunning, bool dndOn, ActivityLevel level) => id switch
    {
        "timer" when timerRunning => [new("timer-cancel", "⏹️", "وقّف المؤقت"), Back],
        "timer" =>
        [
            new("timer-5", "⏱️", "5 د"), new("timer-15", "⏱️", "15 د"), new("timer-25", "⏱️", "25 د"),
            new("timer-45", "⏱️", "45 د"), new("timer-custom", "✏️", "وقت تاني"), Back,
        ],
        "activity" =>
        [
            Level("activity-calm", "🌙", "هادية", level == ActivityLevel.Calm),
            Level("activity-medium", "🌗", "متوسطة", level == ActivityLevel.Medium),
            Level("activity-lively", "☀️", "نشيطة", level == ActivityLevel.Lively),
            Back,
        ],
        "dnd" when dndOn => [new("dnd-off", "🔔", "رجّعي"), Back],
        "dnd" =>
        [
            new("dnd-30", "🔕", "نص ساعة"), new("dnd-60", "🔕", "ساعة"), new("dnd-120", "🔕", "ساعتين"),
            new("dnd-on", "♾️", "لحد ما وقّفها"), Back,
        ],
        _ => [],
    };

    private static MenuItem Level(string id, string emoji, string label, bool current) => new(id, emoji, current ? "✓ " + label : label);
}
