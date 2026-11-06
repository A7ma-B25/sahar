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
