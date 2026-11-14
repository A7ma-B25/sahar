using Sahar.Core.Activities;
using Sahar.Core.Brain;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Activities;

/// <summary>
/// Claude Code (spec 0.13 §2, owner ruling 3: notify only): each hook line from <c>Sahar.Engine.exe hook</c> updates
/// the board; working sessions hang their step, an asking one hangs «Claude ناطر جوابك» first (accent) with her
/// attention face and a bubble line, and Stop is a «Claude خلّص» moment. Sahar never answers for you.
/// </summary>
internal sealed class ClaudeSource
{
    private readonly ClaudeBoard _board = new();
    private readonly ActivityHub _hub;
    private readonly Action<EventKind> _post;
    private bool _working, _asking;

    public ClaudeSource(ActivityHub hub, Action<EventKind> post)
    {
        _hub = hub;
        _post = post;
    }

    public void Apply(ClaudeSignal s)
    {
        var now = DateTimeOffset.Now;
        bool wasAsking = _board.AskingNow;
        _board.Apply(s, now);
        Log.Write($"claude: {s.Event}{(s.Asking ? " (asking)" : "")}");   // the event's name only
        Sync(now);
        foreach (var m in _board.TakeMoments())
        {
            string session = m.Id["claude-done:".Length..];
            _hub.Moment(m, () => new CardData(CardKind.Claude, Claude: _board.Sessions.FirstOrDefault(x => x.Id == session)));
        }
        if (_board.AskingNow && !wasAsking)
        {
            _post(EventKind.ClaudePermission);
            if (!_hub.Hushed()) _hub.Say(ActivityText.AskBubble);   // hushed: the tag and her face, no line
        }
    }

    /// <summary>The tick: minutes move on and silent sessions go.</summary>
    public void Sync(DateTimeOffset now)
    {
        var live = _board.Activities(now);
        _hub.RemoveWhere(id => (id.StartsWith("claude:", StringComparison.Ordinal) || id.StartsWith("claude-ask:", StringComparison.Ordinal))
            && !live.Any(a => a.Id == id));
        foreach (var a in live)
        {
            string session = a.Id[(a.Id.IndexOf(':') + 1)..];
            var kind = a.Kind == ActivityKind.ClaudeAsk ? CardKind.Ask : CardKind.Claude;
            _hub.Set(a, () => _board.Sessions.FirstOrDefault(x => x.Id == session) is { } c ? new CardData(kind, Claude: c) : null);
        }
        bool working = live.Any(a => a.Kind == ActivityKind.ClaudeWorking), asking = live.Any(a => a.Kind == ActivityKind.ClaudeAsk);
        if (working && !_working) _post(EventKind.ClaudeWorking);
        if (!working && !asking && (_working || _asking)) _post(EventKind.ClaudeDone);
        _working = working;
        _asking = asking;
    }

    public int Sessions => _board.Sessions.Count;
}
