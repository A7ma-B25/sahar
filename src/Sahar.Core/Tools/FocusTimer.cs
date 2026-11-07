namespace Sahar.Core.Tools;

/// <summary>Her focus timer (spec §7.1). Wall-clock based: a timer that ran out while the PC slept ends at once on resume.</summary>
public sealed class FocusTimer
{
    private DateTimeOffset _end;

    public bool Running { get; private set; }
    public TimeSpan Length { get; private set; }
    /// <summary>A long focus (25 min or more) ends with a small celebration too (spec §5 #6).</summary>
    public bool IsLong => Length >= TimeSpan.FromMinutes(25);

    public void Start(TimeSpan length, DateTimeOffset now)
    {
        Length = length;
        _end = now + length;
        Running = true;
    }

    public void Cancel() => Running = false;

    public TimeSpan Remaining(DateTimeOffset now) =>
        !Running ? TimeSpan.Zero : _end > now ? _end - now : TimeSpan.Zero;

    /// <summary>True once, the first time it's asked after the end.</summary>
    public bool TakeDone(DateTimeOffset now)
    {
        if (!Running || now < _end) return false;
        Running = false;
        return true;
    }
}
