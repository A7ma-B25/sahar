namespace Sahar.Core.Brain;

public sealed record BrainOutput(string ExpressionKey, bool Hidden, bool SoundAllowed, bool CommentsAllowed);

/// <summary>Picks the one expression to show: highest priority wins, lower ones wait or expire.</summary>
public sealed class Brain
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(8);

    private readonly List<Active> _active = [];
    private QuietReason _quiet;
    private long _seq;

    private sealed record Active(EventKind Kind, DateTimeOffset? Expires, long Seq);

    public void Post(EventKind k, DateTimeOffset now, TimeSpan? ttl = null)
    {
        if (EventNames.Ends(k) is { } ended) _active.RemoveAll(a => a.Kind == ended);
        if (EventNames.IsEndEvent(k)) return;
        _active.RemoveAll(a => a.Kind == k);
        var expires = EventNames.IsSticky(k) ? (DateTimeOffset?)null : now + (ttl ?? DefaultTtl);
        _active.Add(new Active(k, expires, ++_seq));
    }

    public void SetQuiet(QuietReason r) => _quiet = r;

    /// <summary>Ends an active event now (a poke dismisses the timer's face).</summary>
    public void End(EventKind k) => _active.RemoveAll(a => a.Kind == k);

    public BrainOutput Tick(DateTimeOffset now)
    {
        _active.RemoveAll(a => a.Expires is { } e && now >= e);
        Active? best = null;
        foreach (var a in _active)
        {
            if (best is null) { best = a; continue; }
            int pa = EventPriorities.Of(a.Kind), pb = EventPriorities.Of(best.Kind);
            if (pa > pb || (pa == pb && a.Seq < best.Seq)) best = a;   // only a more important event interrupts
        }
        var quiet = _quiet != QuietReason.None;
        return new BrainOutput(
            best is null ? "idle" : ExpressionKeyFor(best.Kind),
            Hidden: _quiet.HasFlag(QuietReason.Fullscreen),
            SoundAllowed: !quiet,
            CommentsAllowed: !quiet);
    }

    public static string ExpressionKeyFor(EventKind k) => k switch
    {
        EventKind.UserAway => "sleep",
        EventKind.UserBack => "back",
        EventKind.BatteryCharging => "charging",
        EventKind.BatteryLow => "battery-low",
        EventKind.DownloadDone => "download",
        EventKind.WeatherRain => "rain",
        EventKind.NetOffline => "offline",
        EventKind.NetOnline => "online",
        EventKind.ClipboardCopied => "copied",
        EventKind.CpuHot => "hot",
        EventKind.TimerDone => "timer-done",
        _ => EventNames.ToWire(k).Replace('.', '-'),
    };
}
