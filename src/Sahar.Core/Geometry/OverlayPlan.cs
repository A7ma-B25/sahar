namespace Sahar.Core.Geometry;

/// <summary>
/// Where her drawing window (the overlay) goes, in screen pixels. It is her canvas plus room for what is drawn around
/// her (the speech bubble, drops, sparkles, the moon toss), grown to the areas a feature needs while it runs (her menu,
/// a game, her showcase, the butterfly) — and never a rect that contains a whole monitor: Windows, Now Bar and other
/// taskbar tools take a window covering a monitor for a fullscreen app (they hide, notifications go quiet).
/// </summary>
public static class OverlayPlan
{
    /// <summary>Room around her canvas, in DIPs: the 260-DIP bubble above her, kept inside the work area, still fits.</summary>
    public const float MarginXDip = 220f, MarginYDip = 140f;

    /// <summary>Her canvas plus the margins: the top-left floored, the size fixed (rounded up, plus one), so a flight only
    /// ever moves the window — a fraction never resizes it by a pixel.</summary>
    public static RectI Follow(RectF canvas, float dpiScale)
    {
        float mx = MarginXDip * dpiScale, my = MarginYDip * dpiScale;
        int left = (int)MathF.Floor(canvas.X - mx), top = (int)MathF.Floor(canvas.Y - my);
        int w = (int)MathF.Ceiling(canvas.Width + 2 * mx) + 1, h = (int)MathF.Ceiling(canvas.Height + 2 * my) + 1;
        return new RectI(left, top, left + w, top + h);
    }

    /// <summary>The overlay rect: <see cref="Follow"/> joined with every area needed now, never covering a monitor.</summary>
    /// <remarks>Each need is grown once by her margins plus half her canvas (+1 px): while her body stays inside it (her
    /// canvas, halo and all, may stick out by half its size), her own part never reaches past it, so the window holds
    /// still for the whole feature — no move of the window and the root mid-feature, which could land a frame apart.</remarks>
    public static RectI Plan(RectF canvas, float dpiScale, IEnumerable<RectF> needs, IReadOnlyList<RectI> monitors)
    {
        float mx = MarginXDip * dpiScale + canvas.Width / 2 + 1, my = MarginYDip * dpiScale + canvas.Height / 2 + 1;
        var r = Follow(canvas, dpiScale);
        foreach (var n in needs) r = Union(r, Outward(new RectF(n.X - mx, n.Y - my, n.Width + 2 * mx, n.Height + 2 * my)));
        return Uncover(r, monitors);
    }

    /// <summary>
    /// While the rect contains a monitor, pulls in the one side (of the four that reach that monitor's edges) that
    /// costs the least area, to 1 px inside that monitor's edge.
    /// </summary>
    public static RectI Uncover(RectI r, IReadOnlyList<RectI> monitors)
    {
        for (int guard = 0; guard < 4 * monitors.Count + 4; guard++)
        {
            int i = -1;
            for (int k = 0; k < monitors.Count && i < 0; k++) if (Covers(r, monitors[k])) i = k;
            if (i < 0) return r;
            var m = monitors[i];
            RectI[] options =
            [
                r with { Left = m.Left + 1 }, r with { Top = m.Top + 1 },
                r with { Right = m.Right - 1 }, r with { Bottom = m.Bottom - 1 },
            ];
            r = options.Where(o => o.Width > 0 && o.Height > 0).MaxBy(o => (long)o.Width * o.Height);
        }
        return r;
    }

    /// <summary>A catcher's area: the whole monitor but one side 1 px short (0 left, 1 top, 2 right, 3 bottom) — enough
    /// for the shell not to take it for a fullscreen app, while a click on the other three edges is still caught.</summary>
    public static RectI ShortOnOneSide(RectI monitor, int edge) => (edge & 3) switch
    {
        0 => monitor with { Left = monitor.Left + 1 },
        1 => monitor with { Top = monitor.Top + 1 },
        2 => monitor with { Right = monitor.Right - 1 },
        _ => monitor with { Bottom = monitor.Bottom - 1 },
    };

    /// <summary>The rect contains the whole monitor (what the shell calls a fullscreen window).</summary>
    public static bool Covers(RectI r, RectI monitor) =>
        r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;

    private static RectI Outward(RectF f) =>
        new((int)MathF.Floor(f.X), (int)MathF.Floor(f.Y), (int)MathF.Ceiling(f.Right), (int)MathF.Ceiling(f.Bottom));

    private static RectI Union(RectI a, RectI b) =>
        new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
}
