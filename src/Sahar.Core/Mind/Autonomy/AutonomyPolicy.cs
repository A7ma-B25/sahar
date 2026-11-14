using System.Text.Json;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Mind.Autonomy;

/// <summary>
/// Spec §3.7 with the owner's rules (binding): she acts alone only in her own reversible world — timers, reminders she
/// proposes, her moves and games, music play/pause/next, tidying her shelf, her notes, her activity level (and
/// proposals for his review); she asks first for anything outside it; class C is never hers to decide. A pure
/// function; the model only proposes. Order: C → his line → her own (state, switch, world, rate).
/// </summary>
public sealed class AutonomyPolicy : IAutonomyPolicy
{
    public const string PointToSettings = "هاد بتعملو إنت من الإعدادات";
    /// <summary>Self-sourced asks per hour (spec §3.7).</summary>
    public const int SelfAsksPerHour = 3;

    /// <summary>Her own reversible world: what she may do by herself (owner's list).</summary>
    private static readonly HashSet<string> HerWorld = new(StringComparer.Ordinal)
    {
        "timer.start", "timer.cancel", "reminder.set", "move.play", "game.start", "music.control", "shelf.tidy", "note.save",
    };

    public AutonomyDecision Decide(ToolSpec spec, ToolCall call, ActionClass cls, ActSource source, AutonomyContext ctx)
    {
        if (cls == ActionClass.C || spec.Class == ActionClass.C)
            return new(Verdict.Refuse, "c-class", source == ActSource.User ? PointToSettings : null);

        if (source == ActSource.User)
            return cls == ActionClass.B ? new(Verdict.Ask, "b-class") : new(Verdict.Act);

        // Her own: never while he is away, locked, in adhan quiet, fullscreen, or on a call (spec §3.7 row C) — except
        // pausing the music the moment a call starts.
        if (ctx.CameraMic && ctx.CallStarting && call.Tool == "music.control" && Op(call) == "pause") return new(Verdict.Act);
        if (ctx.Away) return Refuse("away");
        if (ctx.Locked) return Refuse("locked");
        if (ctx.AdhanQuiet) return Refuse("adhan");
        if (ctx.Fullscreen) return Refuse("fullscreen");
        if (ctx.CameraMic) return Refuse("camera");
        if (cls == ActionClass.Read) return new(Verdict.Act);
        if (ToolCatalog.MindLocal.Contains(call.Tool)) return new(Verdict.Act);   // proposals: nothing takes effect without him

        if (cls == ActionClass.A)
        {
            if (!ctx.SelfActionsOn) return Refuse("self-off");
            if (HerWorld.Contains(call.Tool) || ctx.ForConfirmedGoalOrRoutine || IsActivity(call)) return new(Verdict.Act);
            return ctx.SelfAsksThisHour >= SelfAsksPerHour ? Refuse("rate") : new(Verdict.Ask, "self-a");
        }
        return ctx.SelfAsksThisHour >= SelfAsksPerHour ? Refuse("rate") : new(Verdict.Ask, "b-class");
    }

    private static AutonomyDecision Refuse(string reason) => new(Verdict.Refuse, reason);

    private static string? Op(ToolCall call) =>
        call.Args.ValueKind == JsonValueKind.Object && call.Args.TryGetProperty("op", out var op) && op.ValueKind == JsonValueKind.String ? op.GetString() : null;

    private static bool IsActivity(ToolCall call) =>
        call.Tool == "settings.set" && call.Args.ValueKind == JsonValueKind.Object && call.Args.TryGetProperty("key", out var k)
        && k.ValueKind == JsonValueKind.String && k.GetString() == "activity";
}
