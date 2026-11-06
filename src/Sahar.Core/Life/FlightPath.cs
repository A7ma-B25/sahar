using System.Numerics;

namespace Sahar.Core.Life;

/// <summary>Flight patterns from the motion boards ("حركة سَحَر" M1–M4).</summary>
public enum FlightPattern { Zigzag, FigureEight, SpiralUp, Dive, Wave, GlideHigh, BigCircle, DashAndStop, FallingLeaf, TaskbarPatrol }

/// <summary>Where a flight puts her, in DIPs from where it started (y down). Loops end where they began.</summary>
public static class FlightPath
{
    public static Vector2 Offset(FlightPattern p, float t, float sizeDip)
    {
        t = Math.Clamp(t, 0f, 1f);
        const float Tau = MathF.PI * 2;
        return p switch
        {
            FlightPattern.Zigzag => new(240 * t, 40 * MathF.Sin(3 * Tau * t)),
            FlightPattern.FigureEight => new(160 * MathF.Sin(Tau * t), 70 * MathF.Sin(2 * Tau * t)),
            FlightPattern.SpiralUp => Spiral(t),
            FlightPattern.Dive => new(200 * t, 140 * MathF.Sin(MathF.PI * t)),
            FlightPattern.Wave => new(120 * (1 - MathF.Cos(Tau * t)), -45 * MathF.Sin(3 * Tau * t)),
            FlightPattern.GlideHigh => new(240 * t, -50 * t),
            FlightPattern.BigCircle => new(130 * MathF.Sin(Tau * t), -130 * (1 - MathF.Cos(Tau * t))),
            FlightPattern.DashAndStop => new(250 * EaseOut(MathF.Min(1, t / 0.4f)), 0),
            FlightPattern.FallingLeaf => new(60 * MathF.Sin(3 * MathF.PI * t), 200 * t),
            FlightPattern.TaskbarPatrol => new(220 * MathF.Sin(Tau * t), 0),
            _ => Vector2.Zero,
        };
    }

    /// <summary>Which way to fly a pattern from <paramref name="body"/> (physical px): (±1, ±1) mirrors, or null if it doesn't fit.</summary>
    /// Tries the board's shape first (sideways toward the side with more room), then mirrored up/down, then the other side.
    /// The whole path must stay inside <paramref name="work"/>, and a flight that ends somewhere new must not end on
    /// anything in <paramref name="avoid"/> (the pointer, where you type).</summary>
    public static Vector2? Plan(FlightPattern p, Geometry.RectF body, Geometry.RectF work, float dpiScale, IReadOnlyList<Geometry.RectF> avoid)
    {
        float toward = body.X + body.Width / 2 > work.X + work.Width / 2 ? -1 : 1;
        foreach (var dir in new Vector2[] { new(toward, 1), new(toward, -1), new(-toward, 1), new(-toward, -1) })
            if (Fits(p, body, work, dpiScale, avoid, dir)) return dir;
        return null;
    }

    private static bool Fits(FlightPattern p, Geometry.RectF body, Geometry.RectF work, float dpi, IReadOnlyList<Geometry.RectF> avoid, Vector2 dir)
    {
        const int Samples = 60;
        float sizeDip = body.Width / dpi;
        for (int i = 0; i <= Samples; i++)
        {
            var o = Offset(p, i / (float)Samples, sizeDip) * dir * dpi;
            if (!work.Contains(body with { X = body.X + o.X, Y = body.Y + o.Y })) return false;
        }
        var end = Offset(p, 1, sizeDip) * dir * dpi;
        if (end.Length() < 1) return true;   // a loop: she ends where she already was
        var endRect = body with { X = body.X + end.X, Y = body.Y + end.Y };
        foreach (var a in avoid)
            if (endRect.Intersects(a)) return false;
        return true;
    }

    /// <summary>How long a pattern takes (from the boards).</summary>
    public static float Seconds(FlightPattern p) => p switch
    {
        FlightPattern.Dive => 3.5f,
        FlightPattern.DashAndStop => 4f,
        FlightPattern.Zigzag or FlightPattern.SpiralUp or FlightPattern.FallingLeaf => 5f,
        FlightPattern.FigureEight or FlightPattern.Wave or FlightPattern.TaskbarPatrol => 6f,
        _ => 7f,
    };

    private static Vector2 Spiral(float t)
    {
        float r = 60 * (1 - t) + 20, a = 2 * MathF.PI * 2 * t;
        return new(r * MathF.Sin(a), -200 * t + r * (1 - MathF.Cos(a)) * 0.5f);
    }

    private static float EaseOut(float x) => 1 - (1 - x) * (1 - x);
}
