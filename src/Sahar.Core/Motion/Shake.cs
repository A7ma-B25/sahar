namespace Sahar.Core.Motion;

/// <summary>
/// Are you shaking her while you drag her? A reversal counts when her horizontal direction flips after a swing of at
/// least <see cref="MinSwingDip"/>; a shake is <see cref="Reversals"/> reversals within <see cref="WindowSeconds"/>.
/// A slow drag back and forth (placing her) and a shaky hand are not shakes.
/// </summary>
public sealed class ShakeDetector
{
    public const int Reversals = 4;
    public const double WindowSeconds = 2.0;
    public const float MinSwingDip = 40f;

    private readonly List<double> _reversals = [];
    private float? _anchor;      // where the current swing started
    private float _extreme;      // the furthest point of the current swing
    private int _dir;            // +1 right, -1 left, 0 unknown

    public int Strength { get; private set; }
    public bool Shaken => Strength >= Reversals;

    public void Add(double seconds, float xDip)
    {
        if (_anchor is not float anchor) { _anchor = _extreme = xDip; return; }
        if (_dir == 0)
        {
            if (MathF.Abs(xDip - anchor) >= MinSwingDip) { _dir = MathF.Sign(xDip - anchor); _extreme = xDip; }
            return;
        }
        if ((xDip - _extreme) * _dir > 0) { _extreme = xDip; return; }   // still going the same way
        // Coming back: a reversal once it has come back a whole swing from the extreme.
        if (MathF.Abs(xDip - _extreme) < MinSwingDip || MathF.Abs(_extreme - anchor) < MinSwingDip) return;
        _reversals.Add(seconds);
        _anchor = _extreme;
        _extreme = xDip;
        _dir = -_dir;
        int inWindow = _reversals.Count(t => seconds - t <= WindowSeconds);
        Strength = Math.Max(Strength, inWindow);
    }

    public void Reset()
    {
        _reversals.Clear();
        _anchor = null;
        _dir = 0;
        Strength = 0;
    }
}

public enum ShakeReaction { None, Dizzy, Sick, Angry }

/// <summary>What she does after a shake: mostly dizzy, sick after a hard one, angry when you do it again soon.</summary>
public static class ShakeMood
{
    public static ShakeReaction Pick(int strength, int recentShakes, double roll)
    {
        if (strength < ShakeDetector.Reversals) return ShakeReaction.None;
        if (recentShakes >= 1) return roll < 0.7 ? ShakeReaction.Angry : ShakeReaction.Dizzy;
        if (strength >= 8) return roll < 0.6 ? ShakeReaction.Sick : ShakeReaction.Dizzy;
        return roll < 0.15 ? ShakeReaction.Angry : ShakeReaction.Dizzy;
    }
}
