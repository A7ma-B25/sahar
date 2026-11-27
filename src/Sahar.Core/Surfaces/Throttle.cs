namespace Sahar.Core.Surfaces;

/// <summary>Review I8: at most one send per <paramref name="every"/>; the caller schedules a trailing send after
/// <see cref="Wait"/> so the last value always arrives.</summary>
public sealed class Throttle(TimeSpan every)
{
    private DateTimeOffset? _last;

    /// <summary>True (and counted) when a send may go now.</summary>
    public bool Take(DateTimeOffset now)
    {
        if (_last is { } l && now - l < every) return false;
        _last = now;
        return true;
    }

    /// <summary>How long until the next send may go.</summary>
    public TimeSpan Wait(DateTimeOffset now) => _last is { } l && now - l < every ? every - (now - l) : TimeSpan.Zero;

    public void Reset() => _last = null;
}
