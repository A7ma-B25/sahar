namespace Sahar.Core.Mind.Initiative;

/// <summary>
/// When she may start a line on her own (spec §3.8; Ruling 2; the owner's never-when list). Every rule that closes
/// the window has a stable key, logged as is; the first one that closes it is named. Levels: Often (12 a day, base
/// gap 20 min), Normal (6, 45 min), Rare (3, 45 min), Off. The gap doubles with each dismissal in a row, up to 4 h.
/// </summary>
public sealed class InitiativeGate : IInitiativeGate
{
    public const double TypingSeconds = 20, BeforePrayerMinutes = 20, AfterPrayerMinutes = 10;
    public const int BatteryFloor = 40;
    public static readonly TimeSpan MaxGap = TimeSpan.FromHours(4);

    public int Cap(InitiativeLevel level) => level switch
    {
        InitiativeLevel.Often => 12,
        InitiativeLevel.Normal => 6,
        InitiativeLevel.Rare => 3,
        _ => 0,
    };

    public static TimeSpan Gap(InitiativeLevel level, int dismissedInARow)
    {
        var gap = TimeSpan.FromMinutes(level == InitiativeLevel.Often ? 20 : 45);
        for (int i = 0; i < dismissedInARow && gap < MaxGap; i++) gap *= 2;
        return gap < MaxGap ? gap : MaxGap;
    }

    public InitiativeVerdict Check(InitiativeInputs i)
    {
        string? closed =
            i.Level == InitiativeLevel.Off ? "off"
            : i.PausedUntil is { } p && p > i.Now ? "paused"
            : i.Away ? "away"
            : i.Locked ? "locked"
            : i.SecondsSinceTyping < TypingSeconds ? "typing"
            : i.Busy ? "busy"
            : i.Fullscreen ? "fullscreen"
            : i.CameraMic ? "camera"
            : i.Dnd ? "dnd"
            : i.QuranPlaying ? "quran"
            : i.AdhanQuiet ? "adhan"
            : i.QuietHours ? "quiet-hours"
            : i.MenuOpen ? "menu"
            : i.Hidden ? "hidden"
            : i.GameOrShowRunning ? "game"
            : i.ClaudeWaiting ? "claude"
            : i.TimerDoneShowing ? "timer-done"
            : i.MinutesToPrayer is { } to && to <= BeforePrayerMinutes ? "prayer-soon"
            : i.MinutesSincePrayer is { } since && since < AfterPrayerMinutes ? "prayer"
            : !i.OnAc && i.BatteryPercent <= BatteryFloor ? "battery"
            : i.LinesToday >= Cap(i.Level) ? "cap"
            : i.LastLine is { } last && i.Now - last < Gap(i.Level, i.DismissedInARow) ? "gap"
            : null;
        return closed is null ? InitiativeVerdict.Opened : new(false, closed);
    }
}
