using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Mind.Autonomy;

/// <summary>Who started the action: <c>User</c> = his chat line this turn; <c>Self</c> = initiative, routine or reflection.</summary>
public enum ActSource { User, Self }

public enum Verdict { Act, Ask, Refuse }

/// <summary>The state the policy reads (spec §3.7 ctx). Filled by the engine from its senses; pure data.</summary>
public sealed record AutonomyContext(
    bool Away = false, bool Locked = false, bool Fullscreen = false, bool CameraMic = false, bool AdhanQuiet = false,
    bool Typing = false, bool MenuOpen = false, bool OnBattery = false, int BatteryPercent = 100,
    /// <summary>«اعملي شغلاتي الصغيرة لحالي» (MindSelfActions).</summary>
    bool SelfActionsOn = true,
    /// <summary>The call serves a goal or routine he confirmed (a self-sourced timer or reminder may then act).</summary>
    bool ForConfirmedGoalOrRoutine = false,
    /// <summary>A call just started (camera/mic turned on): a self-sourced music pause may act.</summary>
    bool CallStarting = false,
    /// <summary>Self-sourced asks already made this hour (rate limit 3/h).</summary>
    int SelfAsksThisHour = 0);

/// <summary>The policy's answer. <see cref="Reason"/> for Refuse: c-class | away | locked | adhan | fullscreen |
/// camera | self-off | rate | not-allowed; for Ask: b-class | self-a. <see cref="PointTo"/>: where he can do it
/// himself («هاد بتعملو إنت من الإعدادات»), or null.</summary>
public sealed record AutonomyDecision(Verdict Verdict, string? Reason = null, string? PointTo = null);

/// <summary>Spec §3.7: a pure, table-tested function; the model only proposes (stream C implements).
/// Invariant (contract): class C never returns Act.</summary>
public interface IAutonomyPolicy
{
    AutonomyDecision Decide(ToolSpec spec, ToolCall call, ActionClass cls, ActSource source, AutonomyContext ctx);
}
