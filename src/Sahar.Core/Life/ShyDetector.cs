namespace Sahar.Core.Life;

/// <summary>
/// Motion 17 "بتستحي": the pointer came close (within 60 DIP of her body) three separate times within 10 s
/// without a click — she gets shy and drifts away. Leaving past 120 DIP ends an approach; a click resets.
/// </summary>
public sealed class ShyDetector
{
    public const float NearDip = 60f, AwayDip = 120f;
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly List<DateTimeOffset> _approaches = [];
    private bool _away = true;

    public bool Update(DateTimeOffset now, float pointerDistanceDip, bool clicked)
    {
        if (clicked)
        {
            _approaches.Clear();
            _away = false;
            return false;
        }
        if (pointerDistanceDip > AwayDip) { _away = true; return false; }
        if (pointerDistanceDip > NearDip || !_away) return false;
        _away = false;
        _approaches.RemoveAll(t => now - t > Window);
        _approaches.Add(now);
        if (_approaches.Count < 3) return false;
        _approaches.Clear();
        return true;
    }
}
