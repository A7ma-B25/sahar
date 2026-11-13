namespace Sahar.Core.Update;

/// <summary>Which release she reads: the "test-build" pre-release (for now), or the repo's latest release.</summary>
public enum UpdateChannel { TestBuild, Stable }

/// <summary>What to do after a check: nothing, ask (the bubble), say she is up to date (a check you asked for), or
/// not now (snoozed: asked within the last day; later: she is busy, try again soon).</summary>
public enum UpdateStep { Nothing, Ask, UpToDate, Snoozed, Later }

/// <summary>
/// When she looks for a new version: 2 minutes after start, then every 12 hours; a failed check backs off
/// (15 min, 30 min, … at most 12 h). Asking snoozes the question for a day, so "later" — or closing the bubble —
/// means she asks again the next day. The tray item («دوّر على تحديث») always answers.
/// </summary>
public sealed class UpdateSchedule(DateTimeOffset started)
{
    public static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Every = TimeSpan.FromHours(12);
    public static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromHours(24);
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMinutes(15);

    private int _failures;

    public DateTimeOffset NextCheck { get; private set; } = started + FirstDelay;

    public bool Due(DateTimeOffset now) => now >= NextCheck;

    public void Checked(DateTimeOffset now)
    {
        _failures = 0;
        NextCheck = now + Every;
    }

    public void Failed(DateTimeOffset now)
    {
        _failures++;
        var gap = FirstBackoff * Math.Pow(2, Math.Min(_failures - 1, 10));
        NextCheck = now + (gap < Every ? gap : Every);
    }

    public void Busy(DateTimeOffset now) => NextCheck = now + BusyRetry;

    /// <summary>The time before which she does not ask again, set when she asks.</summary>
    public static DateTimeOffset AskedAt(DateTimeOffset now) => now + AskAgainAfter;

    public static UpdateStep Decide(UpdateOffer? offer, bool manual, bool busy, DateTimeOffset now, DateTimeOffset? askAfter)
    {
        if (manual) return offer is null ? UpdateStep.UpToDate : UpdateStep.Ask;
        if (offer is null) return UpdateStep.Nothing;
        if (askAfter is { } after && now < after) return UpdateStep.Snoozed;
        return busy ? UpdateStep.Later : UpdateStep.Ask;
    }
}
