namespace Sahar.Core.Surfaces;

public enum HostState { Down, Starting, Connected, Failed }

/// <summary>What her end does with a show, or after the host connected / went away.</summary>
public enum HostAction { Send, Queue, QueueAndStart, Fail, SendQueued, Respawn, FailQueued, None }

/// <summary>
/// Review C2 (0.16): her end of the surface host as a small state. A show waits while the host warms up (a cold
/// WebView2 at below-normal priority can take long: no 8 s cliff), up to <see cref="ConnectDeadline"/>. A host that
/// exits before it connects fails what waits at once; one that dies after connecting is started again once
/// (<see cref="MaxRespawns"/>), then shows fail at once (her calm line, the native tray menu) until
/// <see cref="RetryAfter"/> has passed.
/// </summary>
public sealed class HostLink(Func<DateTimeOffset> now)
{
    public const int MaxRespawns = 1;
    public static readonly TimeSpan ConnectDeadline = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    private DateTimeOffset _since;

    public HostState State { get; private set; } = HostState.Down;
    public int Respawns { get; private set; }
    public bool Up => State == HostState.Connected;

    /// <summary>Started this long ago and still not connected.</summary>
    public bool Overdue => State == HostState.Starting && now() - _since > ConnectDeadline;

    public HostAction Show()
    {
        if (State == HostState.Failed && now() - _since >= RetryAfter) State = HostState.Down;
        return State switch
        {
            HostState.Connected => HostAction.Send,
            HostState.Starting => HostAction.Queue,
            HostState.Failed => HostAction.Fail,
            _ => HostAction.QueueAndStart,
        };
    }

    public void Starting()
    {
        State = HostState.Starting;
        _since = now();
    }

    public HostAction Connected()
    {
        State = HostState.Connected;
        return HostAction.SendQueued;
    }

    /// <summary>The host went away after it had connected.</summary>
    public HostAction Lost()
    {
        if (State != HostState.Connected) return HostAction.None;
        if (Respawns < MaxRespawns)
        {
            Respawns++;
            Starting();
            return HostAction.Respawn;
        }
        return Fail();
    }

    /// <summary>It exited before connecting, could not be started, or missed the deadline.</summary>
    public HostAction StartFailed() => Fail();

    private HostAction Fail()
    {
        State = HostState.Failed;
        _since = now();
        return HostAction.FailQueued;
    }
}

/// <summary>Review I7: the pipe's name, one per Windows session (with CurrentUserOnly on both ends).</summary>
public static class SurfacePipeName
{
    public static string For(int sessionId) => "sahar-surfaces-" + sessionId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
