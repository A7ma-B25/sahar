namespace Sahar.Core.Mind.Initiative;

/// <summary>How often she speaks on her own (owner answer 2026-11-13: «Often» is the default).</summary>
public enum InitiativeLevel { Often, Normal, Rare, Off }

/// <summary>Everything the gate reads (spec §3.8 rows + the owner's never-when list). Pure data from the engine.</summary>
public sealed record InitiativeInputs(
    DateTimeOffset Now,
    InitiativeLevel Level,
    bool Away, bool Locked,
    /// <summary>Seconds since his last key (TypingDetector); "typing hard" = keys in the last 20 s.</summary>
    double SecondsSinceTyping,
    /// <summary>Busy: a focus app in front, a call, a game of hers running, the menu open, a show, she is hidden.</summary>
    bool Busy,
    bool Fullscreen, bool CameraMic, bool Dnd, bool QuranPlaying, bool AdhanQuiet, bool QuietHours,
    bool MenuOpen, bool Hidden, bool GameOrShowRunning,
    bool ClaudeWaiting, bool TimerDoneShowing,
    /// <summary>Minutes to the next prayer (null = no prayer times) and since the last one.</summary>
    double? MinutesToPrayer, double? MinutesSincePrayer,
    /// <summary>Her last self-started line, and how many dismissals in a row (each doubles the gap).</summary>
    DateTimeOffset? LastLine, int DismissedInARow,
    int LinesToday,
    bool OnAc, int BatteryPercent,
    /// <summary>After a distress reply, initiative pauses for 24 h (spec §7.4.3).</summary>
    DateTimeOffset? PausedUntil = null);

/// <summary>The gate's answer: open, or the first rule that closed it (a stable key, logged as is).</summary>
public sealed record InitiativeVerdict(bool Open, string? ClosedBy = null)
{
    public static readonly InitiativeVerdict Opened = new(true);
}

/// <summary>When she may start a line on her own (stream C implements; the Mind decides what, often nothing).</summary>
public interface IInitiativeGate
{
    InitiativeVerdict Check(InitiativeInputs inputs);
    /// <summary>Daily cap for a level (Off = 0).</summary>
    int Cap(InitiativeLevel level);
}
