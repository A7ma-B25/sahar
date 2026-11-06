using Sahar.Core.Brain;

namespace Sahar.Engine.Senses;

/// <summary>Away after 5 minutes without input (or when the session locks); back on the next input or unlock.</summary>
internal sealed unsafe class Idle
{
    public static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(5);
    private readonly Action<EventKind> _post;
    private bool _away;

    public Idle(Action<EventKind> post) => _post = post;

    /// <summary>Call every 5 s.</summary>
    public void Check()
    {
        var info = new LASTINPUTINFO { CbSize = (uint)sizeof(LASTINPUTINFO) };
        if (Native.GetLastInputInfo(&info) == 0) return;
        uint idleMs = unchecked(Native.GetTickCount() - info.DwTime);
        if (!_away && idleMs >= AwayAfter.TotalMilliseconds) SetAway(true);
        else if (_away && idleMs < 5000) SetAway(false);
    }

    public void SetAway(bool away)
    {
        if (away == _away) return;
        _away = away;
        _post(away ? EventKind.UserAway : EventKind.UserBack);
    }
}
