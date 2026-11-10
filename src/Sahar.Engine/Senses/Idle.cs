using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Senses;

/// <summary>
/// Are you away? From real input only (<see cref="Presence"/>): Windows' last input, the keyboard hook and the pointer's
/// position, or a locked session — never from her face. Away and back post UserAway/UserBack (her sleepy face) and
/// stop her own actions. On a test machine (no human) only the pipe sets it.
/// </summary>
internal sealed unsafe class Idle
{
    private readonly Action<EventKind> _post;
    private readonly Func<TimeSpan> _sinceKeyboard;
    private bool _locked;
    private Vector2I _lastCursor;
    private long _cursorTick = Environment.TickCount64;

    public Idle(Action<EventKind> post, Func<TimeSpan> sinceKeyboard)
    {
        _post = post;
        _sinceKeyboard = sinceKeyboard;
    }

    public bool Away { get; private set; }

    /// <summary>Real input, for the log and tests: how long since anything you did.</summary>
    public TimeSpan SinceInput { get; private set; }

    /// <summary>Call every second.</summary>
    public void Check()
    {
        var info = new LASTINPUTINFO { CbSize = (uint)sizeof(LASTINPUTINFO) };
        var sinceSystem = Native.GetLastInputInfo(&info) != 0
            ? TimeSpan.FromMilliseconds(unchecked(Native.GetTickCount() - info.DwTime))
            : TimeSpan.MaxValue;
        var c = Native.CursorPos();
        var cursor = new Vector2I(c.X, c.Y);
        if (cursor != _lastCursor) { _lastCursor = cursor; _cursorTick = Environment.TickCount64; }
        var sinceCursor = TimeSpan.FromMilliseconds(Environment.TickCount64 - _cursorTick);
        var sinceKeys = _sinceKeyboard();
        SinceInput = Min(sinceSystem, Min(sinceKeys, sinceCursor));
        Set(Presence.Away(sinceSystem, sinceKeys, sinceCursor, _locked), _locked ? "locked" : "no input for 5 min");
    }

    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (locked) Set(true, "locked");
        else { _cursorTick = Environment.TickCount64; Set(false, "unlocked"); }
    }

    /// <summary>The pipe on a test machine, or the lock screen: away or back now.</summary>
    public void SetAway(bool away) => Set(away, "set");

    public string Describe() =>
        $"away={(Away ? "true" : "false")} locked={(_locked ? "true" : "false")} sinceInput={(SinceInput == TimeSpan.MaxValue ? -1 : (long)SinceInput.TotalSeconds)}";

    private void Set(bool away, string why)
    {
        if (away == Away) return;
        Away = away;
        Log.Write(away ? $"presence: away ({why})" : $"presence: back ({why})");
        _post(away ? EventKind.UserAway : EventKind.UserBack);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private readonly record struct Vector2I(int X, int Y);
}
