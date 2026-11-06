using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Menu;

/// <summary>Where a button sits, in physical pixels (screen coordinates).</summary>
public sealed record ButtonSlot(string Id, Vector2 Centre, float Radius);

/// <summary>
/// Lays the menu's buttons around her (spec §7.1): a full ring, clockwise from the top; near an edge a half
/// ring facing the screen's middle; and never off her monitor's work area (labels included).
/// </summary>
public static class RadialLayout
{
    public const float ButtonRadiusDip = 22f, LabelBandDip = 24f, SpacingDip = 70f, MinRingDip = 90f;

    public static IReadOnlyList<ButtonSlot> Place(IReadOnlyList<MenuItem> items, RectF body, RectF work, float dpiScale)
    {
        int n = items.Count;
        if (n == 0) return [];
        float dip = dpiScale, r = ButtonRadiusDip * dip;
        var c = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        float ring = MathF.Max(MinRingDip, body.Width / dip / 2 + 50) * dip;

        // Full ring, clockwise from the top (screen y grows downward, so a growing angle turns clockwise).
        var slots = Ring(items, c, ring, r, -MathF.PI / 2, 2 * MathF.PI / n, 0);
        if (AllInside(slots, work, dip)) return slots;

        // Half ring facing the work area's middle: n equal sectors over 180°.
        var toward = new Vector2(work.X + work.Width / 2, work.Y + work.Height / 2) - c;
        float facing = toward.LengthSquared() < 1 ? -MathF.PI / 2 : MathF.Atan2(toward.Y, toward.X);
        float half = MathF.Max(ring, SpacingDip * dip * n / MathF.PI);
        float step = MathF.PI / n;
        slots = Ring(items, c, half, r, facing - MathF.PI / 2, step, step / 2);
        if (AllInside(slots, work, dip)) return slots;

        // Still out (a corner, a small screen): slide the whole ring in.
        float minX = slots.Min(s => s.Centre.X) - r, maxX = slots.Max(s => s.Centre.X) + r;
        float minY = slots.Min(s => s.Centre.Y) - r, maxY = slots.Max(s => s.Centre.Y) + r + LabelBandDip * dip;
        float dx = minX < work.X ? work.X - minX : maxX > work.Right ? work.Right - maxX : 0;
        float dy = minY < work.Y ? work.Y - minY : maxY > work.Bottom ? work.Bottom - maxY : 0;
        var shift = new Vector2(dx, dy);
        return slots.Select(s => s with { Centre = s.Centre + shift }).ToList();
    }

    private static List<ButtonSlot> Ring(IReadOnlyList<MenuItem> items, Vector2 c, float radius, float r, float start, float step, float offset)
    {
        var list = new List<ButtonSlot>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            float a = start + offset + step * i;
            list.Add(new ButtonSlot(items[i].Id, c + radius * new Vector2(MathF.Cos(a), MathF.Sin(a)), r));
        }
        return list;
    }

    private static bool AllInside(IReadOnlyList<ButtonSlot> slots, RectF work, float dip)
    {
        foreach (var s in slots)
            if (!work.Contains(new RectF(s.Centre.X - s.Radius, s.Centre.Y - s.Radius, s.Radius * 2, s.Radius * 2 + LabelBandDip * dip)))
                return false;
        return true;
    }
}
