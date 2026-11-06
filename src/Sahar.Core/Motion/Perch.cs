using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Motion;

/// <summary>Contact points on the inner edge of a taskbar or the top of a window.</summary>
public static class Perch
{
    public static Vector2 OnTaskbar(RectF taskbar, Edge edge, float bodyWidth, float alongFraction) => edge switch
    {
        Edge.Bottom => new(Along(taskbar.X, taskbar.Width, bodyWidth, alongFraction), taskbar.Y),
        Edge.Top => new(Along(taskbar.X, taskbar.Width, bodyWidth, alongFraction), taskbar.Y + taskbar.Height),
        Edge.Left => new(taskbar.X + taskbar.Width, Along(taskbar.Y, taskbar.Height, bodyWidth, alongFraction)),
        Edge.Right => new(taskbar.X, Along(taskbar.Y, taskbar.Height, bodyWidth, alongFraction)),
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    public static Vector2 OnWindowTop(RectF window, float bodyWidth, float alongFraction) =>
        new(Along(window.X, window.Width, bodyWidth, alongFraction), window.Y);

    private static float Along(float start, float length, float bodyWidth, float f)
    {
        float lo = start + bodyWidth / 2, hi = start + length - bodyWidth / 2;
        return hi < lo ? start + length / 2 : lo + (hi - lo) * Math.Clamp(f, 0f, 1f);
    }
}
