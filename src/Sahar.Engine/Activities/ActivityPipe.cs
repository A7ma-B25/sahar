using Sahar.Core.Activities;
using Sahar.Core.Protocol;
using Sahar.Engine.Pipe;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Activities;

/// <summary>
/// Test mode's activity commands: activity-sim {kind, moment?}, activity-clear, activity-card {id?}, activity-card-close,
/// activity-card-press {id}, activity-wheel {steps}, activity-bead {index}, activity-knock {kind}, debug-activities.
/// Replies carry key=value data in the error slot, as the other debug commands do.
/// </summary>
internal static class ActivityPipe
{
    public static void Register(PipeServer pipe, ActivityHub hub, ClaudeSource claude, Func<List<MonitorInfo>> monitors)
    {
        string Arg(PipeRequest r, string key) => r.Args?.TryGetValue(key, out var v) == true ? v : "";
        pipe.Register("debug-activities", _ => new PipeReply(true, hub.Describe() + $" claudeSessions={claude.Sessions}"));
        pipe.Register("activity-sim", r =>
        {
            if (ActivitySim.Make(Arg(r, "kind"), DateTimeOffset.Now) is not { } made) return new PipeReply(false, "unknown kind");
            var (a, card) = made;
            if (Arg(r, "moment") == "true") hub.Moment(a, card);
            else hub.Set(a, card);
            return new PipeReply(true, hub.Describe());
        });
        pipe.Register("activity-clear", _ =>
        {
            hub.CloseCard();
            hub.RemoveWhere(_ => true);
            return new PipeReply(true, hub.Describe());
        });
        pipe.Register("activity-card", r =>
        {
            var id = Arg(r, "id");
            hub.OpenCard(id.Length > 0 ? id : null);
            return new PipeReply(true, hub.Describe());
        });
        pipe.Register("activity-card-close", _ => { hub.CloseCard(); return new PipeReply(true, hub.Describe()); });
        pipe.Register("activity-card-press", r => new PipeReply(hub.Card.Press(Arg(r, "id")), hub.Describe()));
        pipe.Register("activity-wheel", r =>
        {
            hub.Wheel(int.TryParse(Arg(r, "steps"), out var s) ? s : 1);
            return new PipeReply(true, hub.Describe());
        });
        pipe.Register("activity-bead", r =>
        {
            hub.PickBead(int.TryParse(Arg(r, "index"), out var i) ? i : 0);
            return new PipeReply(true, hub.Describe());
        });
        pipe.Register("activity-knock", r =>
        {
            if (ActivitySim.Make(Arg(r, "kind"), DateTimeOffset.Now) is not { } made) return new PipeReply(false, "unknown kind");
            hub.TestKnock(made.Activity, monitors()[0].Bounds);
            return new PipeReply(true, hub.Describe());
        });
    }
}
