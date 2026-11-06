namespace Sahar.Core.Geometry;

/// <summary>Keeping Sahar on a real screen, and deciding which monitor's DPI she is drawn at.</summary>
public static class Placement
{
    /// <summary>Share of her body that must be on another monitor before she switches to it (no flip-flopping).</summary>
    public const float SwitchShare = 0.6f;

    /// <summary>
    /// Moves her body rect inside the work area she overlaps most, or the nearest one if she is on none
    /// (a monitor was unplugged or the resolution shrank).
    /// </summary>
    public static RectF Reclamp(RectF body, IReadOnlyList<RectF> workAreas)
    {
        if (workAreas.Count == 0) return body;
        int best = -1;
        float bestArea = 0;
        for (int i = 0; i < workAreas.Count; i++)
        {
            float a = Overlap(body, workAreas[i]);
            if (a > bestArea) { bestArea = a; best = i; }
        }
        if (best < 0)
        {
            float bestDistance = float.MaxValue;
            for (int i = 0; i < workAreas.Count; i++)
            {
                float d = DistanceSquared(Centre(body), Centre(workAreas[i]));
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
        }
        var w = workAreas[best];
        float x = Math.Clamp(body.X, w.X, MathF.Max(w.X, w.X + w.Width - body.Width));
        float y = Math.Clamp(body.Y, w.Y, MathF.Max(w.Y, w.Y + w.Height - body.Height));
        return body with { X = x, Y = y };
    }

    /// <summary>
    /// Index of the monitor whose DPI she should use: one holding at least <see cref="SwitchShare"/> of her body,
    /// otherwise the current one (or, with no valid current, the one she overlaps most).
    /// </summary>
    public static int ChooseMonitor(RectF body, IReadOnlyList<RectF> monitors, int current)
    {
        float area = MathF.Max(1, body.Width * body.Height);
        int largest = -1;
        float largestArea = 0;
        for (int i = 0; i < monitors.Count; i++)
        {
            float a = Overlap(body, monitors[i]);
            if (a / area >= SwitchShare) return i;
            if (a > largestArea) { largestArea = a; largest = i; }
        }
        return current >= 0 && current < monitors.Count ? current : Math.Max(0, largest);
    }

    private static float Overlap(RectF a, RectF b)
    {
        float w = MathF.Min(a.X + a.Width, b.X + b.Width) - MathF.Max(a.X, b.X);
        float h = MathF.Min(a.Y + a.Height, b.Y + b.Height) - MathF.Max(a.Y, b.Y);
        return w > 0 && h > 0 ? w * h : 0;
    }

    private static (float X, float Y) Centre(RectF r) => (r.X + r.Width / 2, r.Y + r.Height / 2);

    private static float DistanceSquared((float X, float Y) a, (float X, float Y) b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);
}
