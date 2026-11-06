using System.Numerics;
using Sahar.Core.Art;

namespace Sahar.Engine.Render;

/// <summary>
/// The orb the moon turns into during an event (expressions board): soft glow, a gradient disc, a thin white
/// ring and the event's glyph. Emoji draw in colour with Segoe UI Emoji; marks (؟ ✓ ✕ •••) in white Segoe UI.
/// </summary>
internal static class OrbRenderer
{
    public const float Units = 96f;            // surface: glow r36 plus soft edge
    public const float Radius = 24f;
    public static readonly Vector2 Centre = new(258, 92);

    private static readonly string[] Marks = ["؟", "✓", "✕", "•••"];

    public static void Draw(D2DContext dc, float scale, string glyph, Rgba top, Rgba bottom)
    {
        dc.SetTransform(Matrix3x2.CreateTranslation(Units / 2, Units / 2) * Matrix3x2.CreateScale(scale));
        var glowBrush = dc.RadialBrush(
            [new() { Position = 0, Color = new D2DColor(top, 0.45f) }, new() { Position = 0.6f, Color = new D2DColor(top, 0.3f) },
             new() { Position = 1, Color = new D2DColor(top, 0) }],
            Vector2.Zero, new Vector2(Radius + 20));
        dc.FillEllipse(Vector2.Zero, new Vector2(Radius + 20), glowBrush);

        // radialGradient cx 35% cy 30% r 80% over the disc's box
        var box = 2 * Radius;
        dc.FillEllipse(Vector2.Zero, new Vector2(Radius), dc.RadialBrush(
            [new() { Position = 0, Color = new D2DColor(top) }, new() { Position = 1, Color = new D2DColor(bottom) }],
            new Vector2(-Radius + box * 0.35f, -Radius + box * 0.3f), new Vector2(box * 0.8f)));
        dc.DrawEllipse(Vector2.Zero, new Vector2(Radius), dc.SolidBrush(new D2DColor(Rgba.Parse("#FFFFFF")), 0.6f), 1.5f, dc.StrokeStyle("butt"));

        var area = new D2DRect { Left = -Radius, Top = -Radius, Right = Radius, Bottom = Radius };
        var white = dc.SolidBrush(new D2DColor(Rgba.Parse("#FFFFFF")));
        if (Array.IndexOf(Marks, glyph) >= 0)
            dc.Text(glyph, "Segoe UI", glyph == "•••" ? 16 : 26, 600, area, white);
        else
            dc.Text(glyph, "Segoe UI Emoji", 22, 400, area, white);
    }
}
