using Sahar.Core.Mind.Autonomy;

namespace Sahar.Core.Mind.Log;

public enum ActionOutcome { Done, Asked, AskedYes, AskedNo, Refused, Failed, Undone }

/// <summary>One line of «شو عملت اليوم» (spec §3.7): time, tool, her short summary, source, the model's why, the
/// outcome, and an undo token while undo is meaningful. <see cref="Feedback"/>: +1 👍, −1 👎, 0 none.</summary>
public sealed record ActionEntry(string Id, DateTimeOffset At, string Tool, string Summary, ActSource Source, string? Why,
    ActionOutcome Outcome, string? UndoToken = null, int Feedback = 0);

/// <summary>The action log (stream C), stored through <see cref="Memory.ISecretBox"/>, kept 30 days.</summary>
public interface IActionLog
{
    ActionEntry Append(string tool, string summary, ActSource source, string? why, ActionOutcome outcome, string? undoToken);
    IReadOnlyList<ActionEntry> Day(DateOnly day);
    IReadOnlyList<ActionEntry> All();
    /// <summary>Runs the inverse through the tool; false when it no longer applies.</summary>
    bool Undo(string id);
    void Feedback(string id, int value);
}
