using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Menu;

/// <summary>Where a button sits, in physical pixels (screen coordinates).</summary>
public sealed record ButtonSlot(string Id, Vector2 Centre, float Radius)
{
    public RectF Circle => new(Centre.X - Radius, Centre.Y - Radius, 2 * Radius, 2 * Radius);
    public RectF Label { get; init; }
    public RectF Bounds
    {
        get
        {
            var c = Circle;
            float x = MathF.Min(c.X, Label.X), y = MathF.Min(c.Y, Label.Y);
            return new RectF(x, y, MathF.Max(c.Right, Label.Right) - x, MathF.Max(c.Bottom, Label.Bottom) - y);
        }
    }
}

/// <summary>
/// Lays the menu's buttons around her (spec §7.1): a full ring, clockwise from the top; near an edge a half
/// ring facing the screen's middle; and never off her monitor's work area (labels included).
/// </summary>
public static class RadialLayout
{
    public const float ButtonRadiusDip = 22f, LabelBandDip = 24f, SpacingDip = 70f, MinRingDip = 90f;

    public const float LabelTopDip = 30f, LabelHeightDip = 22f;
    public static float LabelWidthDip(string label) => MathF.Min(112, label.Length * 7f + 18);

    public static IReadOnlyList<ButtonSlot> Place(IReadOnlyList<MenuItem> items, RectF body, RectF work, float dpiScale)
    {
        int n = items.Count;
        if (n == 0) return [];
        float dip = dpiScale, r = ButtonRadiusDip * dip, grow = 6 * dip, max = 300 * dip;
        var c = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        float ring = MathF.Max(MinRingDip, body.Width / dip / 2 + 50) * dip;

        // Full ring, clockwise from the top (screen y grows downward, so a growing angle turns clockwise),
        // wide enough that no label touches another button.
        List<ButtonSlot> slots;
        float radius = ring;
        do slots = Ring(items, c, radius, r, -MathF.PI / 2, 2 * MathF.PI / n, 0);
        while (Overlap(slots, 4 * dip) && (radius += grow) < max);
        if (AllInside(slots, work)) return slots;

        // Half ring facing the work area's middle: n equal sectors over 180°.
        var toward = new Vector2(work.X + work.Width / 2, work.Y + work.Height / 2) - c;
        float facing = toward.LengthSquared() < 1 ? -MathF.PI / 2 : MathF.Atan2(toward.Y, toward.X);
        float step = MathF.PI / n;
        radius = MathF.Max(ring, SpacingDip * dip * n / MathF.PI);
        do slots = Ring(items, c, radius, r, facing - MathF.PI / 2, step, step / 2);
        while (Overlap(slots, 4 * dip) && (radius += grow) < max);
        if (AllInside(slots, work)) return slots;

        // Still out (a corner, a small screen): slide the whole ring in, labels included.
        float minX = slots.Min(s => s.Bounds.X), maxX = slots.Max(s => s.Bounds.Right);
        float minY = slots.Min(s => s.Bounds.Y), maxY = slots.Max(s => s.Bounds.Bottom);
        float dx = minX < work.X ? work.X - minX : maxX > work.Right ? work.Right - maxX : 0;
        float dy = minY < work.Y ? work.Y - minY : maxY > work.Bottom ? work.Bottom - maxY : 0;
        return slots.Select(s => s with
        {
            Centre = s.Centre + new Vector2(dx, dy),
            Label = s.Label with { X = s.Label.X + dx, Y = s.Label.Y + dy },
        }).ToList();
    }

    /// <summary>The button under a point: its disc or its label.</summary>
    public static string? HitTest(IReadOnlyList<ButtonSlot> slots, Vector2 p)
    {
        foreach (var s in slots)
            if (Vector2.Distance(s.Centre, p) <= s.Radius + 2 ||
                (p.X >= s.Label.X && p.X <= s.Label.Right && p.Y >= s.Label.Y && p.Y <= s.Label.Bottom))
                return s.Id;
        return null;
    }

    private static bool Overlap(IReadOnlyList<ButtonSlot> slots, float margin)
    {
        for (int i = 0; i < slots.Count; i++)
            for (int j = 0; j < slots.Count; j++)
            {
                if (i == j) continue;
                var a = Grow(slots[i].Label, margin);
                if (a.Intersects(slots[j].Label) || a.Intersects(slots[j].Circle)) return true;
                if (j > i && Grow(slots[i].Circle, margin).Intersects(slots[j].Circle)) return true;
            }
        return false;
    }

    private static RectF Grow(RectF r, float m) => new(r.X - m / 2, r.Y - m / 2, r.Width + m, r.Height + m);

    private static List<ButtonSlot> Ring(IReadOnlyList<MenuItem> items, Vector2 c, float radius, float r, float start, float step, float offset)
    {
        var list = new List<ButtonSlot>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            float a = start + offset + step * i;
            var centre = c + radius * new Vector2(MathF.Cos(a), MathF.Sin(a));
            float dip = r / ButtonRadiusDip, lw = LabelWidthDip(items[i].Label) * dip;
            list.Add(new ButtonSlot(items[i].Id, centre, r)
            {
                Label = new RectF(centre.X - lw / 2, centre.Y + LabelTopDip * dip, lw, LabelHeightDip * dip),
            });
        }
        return list;
    }

    private static bool AllInside(IReadOnlyList<ButtonSlot> slots, RectF work)
    {
        foreach (var s in slots)
            if (!work.Contains(s.Bounds)) return false;
        return true;
    }
}
