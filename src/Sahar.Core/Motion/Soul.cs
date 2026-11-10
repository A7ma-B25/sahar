using System.Numerics;

namespace Sahar.Core.Motion;

/// <summary>One frame of a hop: how high she is (rig units, negative = up), her vertical scale, and her tilt.</summary>
public readonly record struct HopPose(float RiseUnits, float ScaleY, float TiltDeg);

/// <summary>One key of the baked hop: progress 0…1 over <see cref="SoulMath.HopSeconds"/>, the pose and the leaf kick.</summary>
public sealed record HopKey(float Progress, HopPose Pose, float Kick);

/// <summary>
/// The soul's motion (spec §4): float, follow-through, sway, leaf lag, the hop, look and the ground shadow. Distances
/// are rig units (0.10a Ruling 1); the engine multiplies by her pixel scale. φ = 2πt/4.2; j = seconds into a hop.
/// </summary>
public static class SoulMath
{
    public const double FloatPeriod = 4.2, BreathPeriod = 3.6, LoopSeconds = 25.2;   // 6 floats = 7 breaths = 3 sways
    public const double MoonLapSeconds = 16;
    public const float FloatUnits = 6f, FollowAmp = 0.012f, BreathAmp = 0.012f, SwayDeg = 1.6f, LeafDeg = 6f, LeafLag = 0.9f;
    public const float HopAnticipation = 0.09f, HopAir = 0.40f, HopSeconds = 1.2f, HopPeakUnits = 38f, HopSquash = 0.16f,
        HopStretch = 0.14f, HopTiltDeg = 3f, LandAmp = 0.2f, LandDecay = 7f, LandFreq = 16f;
    public const float KickDeg = -10f, KickDecay = 4f, KickFreq = 12f, HopCooldown = 0.75f, HappySeconds = 1.1f;
    public static readonly Vector2 LookUnits = new(3.2f, 2.4f);
    public const float LeanDeg = 1.8f, LeafLeanDeg = 2.4f, LookRadiusDip = 400f, LookEaseSeconds = 0.4f;

    private static double Phi(double t) => 2 * Math.PI * t / FloatPeriod;

    public static float FloatY(double t) => (float)(-FloatUnits * Math.Sin(Phi(t)));

    /// <summary>Vertical scale (horizontal = 1/this, so her volume is kept): she stretches while rising (Ruling 2) and breathes.</summary>
    public static float ScaleY(double t) => (float)(1 + FollowAmp * Math.Cos(Phi(t)) + BreathAmp * Math.Sin(2 * Math.PI * t / BreathPeriod));

    public static float Sway(double t) => (float)(SwayDeg * Math.Sin(Phi(t) / 2));

    public static float Leaf(double t) => (float)(LeafDeg * Math.Sin(Phi(t) - LeafLag));

    /// <summary>The hop at <paramref name="j"/> s: anticipation squash, a parabolic rise with stretch and tilt, then a
    /// damped landing squash. <paramref name="power"/> scales the squash, rise and stretch (an idle hop is 0.6).</summary>
    public static HopPose Hop(double j, float power)
    {
        // Phase edges in double (0.09f + 0.40f is not 0.49 in float).
        const double ant = 0.09, air = 0.40, end = 1.2;
        if (j < 0 || j >= end) return new HopPose(0, 1, 0);
        if (j < ant)
        {
            double k = j / ant;
            return new HopPose(0, (float)(1 - HopSquash * power * k), 0);
        }
        if (j < ant + air)
        {
            double k = (j - ant) / air;
            return new HopPose((float)(-HopPeakUnits * power * 4 * k * (1 - k)),
                (float)(1 + HopStretch * power * (1 - Math.Abs(2 * k - 1))),
                (float)(HopTiltDeg * Math.Sin(Math.PI * k)));
        }
        double l = j - ant - air;
        return new HopPose(0, (float)(1 - LandAmp * power * Math.Exp(-LandDecay * l) * Math.Cos(LandFreq * l)), 0);
    }

    /// <summary>The leaf's damped kick after a hop starts (degrees).</summary>
    public static float LeafKick(double j, float power) =>
        j < 0 || j >= 1.2 ? 0f : (float)(KickDeg * Math.Exp(-KickDecay * j) * Math.Sin(KickFreq * j) * power);

    /// <summary>The whole hop baked for one keyframe animation per property (Ruling 9): <paramref name="perSecond"/>
    /// samples a second, first and last keys at rest.</summary>
    public static IReadOnlyList<HopKey> HopKeys(float power, int perSecond = 60)
    {
        int n = (int)MathF.Round(HopSeconds * perSecond);
        var keys = new HopKey[n + 1];
        for (int i = 0; i <= n; i++)
        {
            float progress = i == n ? 1f : (float)i / n;
            double j = progress * HopSeconds;
            keys[i] = new HopKey(progress, Hop(j, power), LeafKick(j, power));
        }
        return keys;
    }

    public static float ShadowRx(float heightUnits) => MathF.Max(0, 62 - 0.5f * heightUnits);

    public static float ShadowOpacity(float heightUnits) => MathF.Max(0, 0.16f - 0.002f * heightUnits);

    /// <summary>Where her pupils look (rig units) and how far she leans (−1…1), toward the cursor; nothing beyond
    /// <see cref="LookRadiusDip"/> or with the cursor on her eyes.</summary>
    public static (Vector2 Units, float Lean) Look(Vector2 cursorPx, Vector2 eyesPx, float halfCanvasPx, float distanceDip)
    {
        if (distanceDip > LookRadiusDip || halfCanvasPx <= 0) return (Vector2.Zero, 0f);
        var d = cursorPx - eyesPx;
        var n = new Vector2(Math.Clamp(d.X / halfCanvasPx, -1f, 1f), Math.Clamp(d.Y / halfCanvasPx, -1f, 1f));
        return (n * LookUnits, n.X);
    }
}

/// <summary>Blinks at random 2.6–6 s gaps, drawn once into one looping keyframe set (0.10a Ruling 6).</summary>
public static class BlinkPattern
{
    public const float BlinkSeconds = 0.12f, MinGap = 2.6f, MaxGap = 6f, ClosedScale = 0.14f;

    public static IReadOnlyList<float> Gaps(int seed, int count = 8)
    {
        var rng = new Random(seed);
        var gaps = new float[count];
        for (int i = 0; i < count; i++) gaps[i] = MinGap + (float)rng.NextDouble() * (MaxGap - MinGap);
        return gaps;
    }
}

public enum SoulIdleKind { Hop, LookLeft, LookRight }

/// <summary>Every 6–12 s, at rest, a small hop or a glance to one side.</summary>
public static class SoulIdle
{
    public const float MinGap = 6f, MaxGap = 12f, HopPower = 0.6f, LookSeconds = 1.2f;

    /// <summary>An idle act only while she rests and nothing else is going on: not busy (menu, show, game, hidden,
    /// sulking, you away), not quiet (do not disturb, camera or mic, adhan, Quran, quiet hours), not while you type, and
    /// no hops or glances at all under reduced motion.</summary>
    public static bool MayAct(MotionKind current, SoulIdleKind kind, bool busy, bool quiet, bool typing, bool reduced) =>
        current is MotionKind.Hover or MotionKind.EyesFollow && !busy && !quiet && !typing && !reduced;

    public static (TimeSpan Wait, SoulIdleKind Kind) Next(Random rng)
    {
        var wait = TimeSpan.FromSeconds(MinGap + rng.NextDouble() * (MaxGap - MinGap));
        double r = rng.NextDouble();
        return (wait, r < 0.5 ? SoulIdleKind.Hop : r < 0.75 ? SoulIdleKind.LookLeft : SoulIdleKind.LookRight);
    }
}

/// <summary>Things built once per key and reused (the hop's animation set per power and pixel scale); Clear disposes
/// them (a rescale or a device change rebuilds them).</summary>
public sealed class KeyedCache<TKey, TValue>(Func<TKey, TValue> make) where TKey : notnull where TValue : IDisposable
{
    private readonly Dictionary<TKey, TValue> _items = [];

    public int Count => _items.Count;

    public TValue Get(TKey key)
    {
        if (!_items.TryGetValue(key, out var v)) _items[key] = v = make(key);
        return v;
    }

    public void Clear()
    {
        foreach (var v in _items.Values) v.Dispose();
        _items.Clear();
    }
}

public static class SoulRules
{
    /// <summary>A hop only while she rests (hovering or watching you), not held still, not grounded, with motion
    /// allowed, not busy (sulking, a game, hidden, a menu drag), and after the cooldown.</summary>
    public static bool CanHop(MotionKind current, bool still, bool grounded, bool reduced, bool busy, double secondsSinceLastHop) =>
        current is MotionKind.Hover or MotionKind.EyesFollow && !still && !grounded && !reduced && !busy
        && secondsSinceLastHop >= SoulMath.HopCooldown;
}
