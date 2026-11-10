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

    /// <summary>Her canvas plus the margins, rounded outward.</summary>
    public static RectI Follow(RectF canvas, float dpiScale) =>
        Outward(new RectF(canvas.X - MarginXDip * dpiScale, canvas.Y - MarginYDip * dpiScale,
            canvas.Width + 2 * MarginXDip * dpiScale, canvas.Height + 2 * MarginYDip * dpiScale));

    /// <summary>The overlay rect: <see cref="Follow"/> joined with every area needed now, never covering a monitor.</summary>
    /// <summary>While a feature needs an area, her own part is snapped outward to this grid (px): flying inside it
    /// never resizes the window every frame.</summary>
    public const int NeedGridPx = 128;

    public static RectI Plan(RectF canvas, float dpiScale, IEnumerable<RectF> needs, IReadOnlyList<RectI> monitors)
    {
        var follow = Follow(canvas, dpiScale);
        var r = follow;
        bool any = false;
        foreach (var n in needs) { r = Union(r, Outward(n)); any = true; }
        if (any) r = Union(r, Snap(follow, NeedGridPx));
        return Uncover(r, monitors);
    }

    private static RectI Snap(RectI r, int g) =>
        new(FloorTo(r.Left, g), FloorTo(r.Top, g), -FloorTo(-r.Right, g), -FloorTo(-r.Bottom, g));

    private static int FloorTo(int v, int g) => (int)Math.Floor(v / (double)g) * g;

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

    /// <summary>The rect contains the whole monitor (what the shell calls a fullscreen window).</summary>
    public static bool Covers(RectI r, RectI monitor) =>
        r.Left <= monitor.Left && r.Top <= monitor.Top && r.Right >= monitor.Right && r.Bottom >= monitor.Bottom;

    private static RectI Outward(RectF f) =>
        new((int)MathF.Floor(f.X), (int)MathF.Floor(f.Y), (int)MathF.Ceiling(f.Right), (int)MathF.Ceiling(f.Bottom));

    private static RectI Union(RectI a, RectI b) =>
        new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
}
