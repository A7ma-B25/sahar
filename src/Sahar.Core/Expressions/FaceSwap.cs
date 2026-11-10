namespace Sahar.Core.Expressions;

/// <summary>
/// One face change as a blink (0.11, the owner's report): the eyes close from <see cref="From"/> at <see cref="Start"/>,
/// the face swaps hard at <see cref="SwapAt"/> (eyes shut), and the new eyes open by <see cref="End"/> (seconds on one
/// clock). <see cref="Retarget"/>: a newer face arrived before the swap; it only replaces what will be shown.
/// </summary>
public sealed record FaceSwapPlan(double Start, float From, double SwapAt, double End, bool Retarget = false)
{
    public bool Instant => End <= Start;
}

/// <summary>
/// Faces change like a blink, never by cross-fading two faces: eye scaleY eases in to 0 (70 ms from open), eyes and
/// mouth swap at the closed moment, the new eyes ease out 0 → 1 (90 ms). The newest face wins without stacking:
/// before the closed moment it just becomes the face that will show; after it, a new close starts from where the eyes
/// are. Reduced motion: an instant swap.
/// </summary>
public static class FaceSwap
{
    public const double CloseSeconds = 0.07, OpenSeconds = 0.09;

    public static FaceSwapPlan Plan(double now, FaceSwapPlan? running, bool reduced)
    {
        if (reduced) return new(now, 1f, now, now);
        if (running is { Instant: false } r && now < r.End)
        {
            if (now < r.SwapAt) return r with { Retarget = true };
            float from = EyeScaleAt(r, now);
            double swap = now + CloseSeconds * from;
            return new(now, from, swap, swap + OpenSeconds);
        }
        return new(now, 1f, now + CloseSeconds, now + CloseSeconds + OpenSeconds);
    }

    /// <summary>The eyes' scaleY at <paramref name="t"/>: ease-in to 0 by the swap, ease-out back to 1 by the end.</summary>
    public static float EyeScaleAt(FaceSwapPlan p, double t)
    {
        if (p.Instant || t >= p.End) return 1f;
        if (t <= p.Start) return p.From;
        if (t < p.SwapAt)
        {
            double u = (t - p.Start) / (p.SwapAt - p.Start);
            return (float)(p.From * (1 - u * u));
        }
        double v = (t - p.SwapAt) / (p.End - p.SwapAt);
        return (float)(1 - (1 - v) * (1 - v));
    }
}
