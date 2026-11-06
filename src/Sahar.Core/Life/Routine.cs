namespace Sahar.Core.Life;

/// <summary>
/// Your rhythm, learned on the machine: continuous work (two hours → time for a break), late nights, and a
/// 24-hour profile of when you are usually at the computer (she is calmer in those hours).
/// </summary>
public sealed class Routine
{
    public static readonly TimeSpan BreakAfter = TimeSpan.FromHours(2);
    public static readonly TimeSpan RestResets = TimeSpan.FromMinutes(5);
    public const float ProfileAlpha = 0.1f;

    private readonly float[] _profile = new float[24];
    private DateTimeOffset? _last;
    private TimeSpan _continuous, _inactive;
    private bool _breakDue, _lateNightDue;
    private DateOnly? _lastNight;
    private (DateOnly Day, int Hour)? _bucket;
    private int _bucketActive, _bucketTotal;

    public void Tick(DateTimeOffset now, bool active)
    {
        UpdateProfile(now, active);

        if (_last is { } last)
        {
            var dt = now - last;
            if (dt < TimeSpan.Zero || dt > RestResets)
            {
                // Sleep/resume or a clock change: never counts as work.
                _continuous = _inactive = TimeSpan.Zero;
            }
            else if (active)
            {
                _continuous += dt;
                _inactive = TimeSpan.Zero;
            }
            else
            {
                _inactive += dt;
                if (_inactive >= RestResets) _continuous = TimeSpan.Zero;
            }
        }
        _last = now;

        if (_continuous >= BreakAfter)
        {
            _breakDue = true;
            _continuous = TimeSpan.Zero;
        }

        if (active && now.Hour is >= 0 and <= 4)
        {
            // A night is named after the evening it started on.
            var night = DateOnly.FromDateTime(now.DateTime.AddHours(-5));
            if (_lastNight != night)
            {
                _lastNight = night;
                _lateNightDue = true;
            }
        }
    }

    public bool TakeBreakDue() => Take(ref _breakDue);

    public bool TakeLateNightDue() => Take(ref _lateNightDue);

    /// <summary>1 in hours you are usually away, up to 2 in hours you usually work.</summary>
    public float BusyFactor(int hour) => 1f + _profile[Math.Clamp(hour, 0, 23)];

    public float[] ProfileToState() => (float[])_profile.Clone();

    public static Routine FromState(float[]? hours)
    {
        var r = new Routine();
        if (hours is { Length: 24 })
            for (int i = 0; i < 24; i++) r._profile[i] = float.IsFinite(hours[i]) ? Math.Clamp(hours[i], 0f, 1f) : 0f;
        return r;
    }

    private void UpdateProfile(DateTimeOffset now, bool active)
    {
        var key = (DateOnly.FromDateTime(now.DateTime), now.Hour);
        if (_bucket != key)
        {
            if (_bucket is { } done && _bucketTotal > 0)
            {
                float sample = _bucketActive * 2 > _bucketTotal ? 1f : 0f;
                _profile[done.Hour] += (sample - _profile[done.Hour]) * ProfileAlpha;
            }
            _bucket = key;
            _bucketActive = _bucketTotal = 0;
        }
        _bucketTotal++;
        if (active) _bucketActive++;
    }

    private static bool Take(ref bool flag)
    {
        bool was = flag;
        flag = false;
        return was;
    }
}
