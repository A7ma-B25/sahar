using System.Numerics;
using Sahar.Core.Art;

namespace Sahar.Engine.Render;

/// <summary>
/// Draws Sahar's parts with Direct2D, matching the approved design generator (tools/art/design.py).
/// Coordinates are design units: the 300×300 canvas; rig shapes are offset by (rig.OffsetX, rig.OffsetY).
/// SVG blur filters are approximated with soft radial gradients.
/// </summary>
internal static class PartRenderer
{
    private static readonly Rgba Star = Rgba.Parse("#FFE7A8");
    private static readonly Rgba MoonLight = Rgba.Parse("#FFF6D8");
    private static readonly Rgba MoonDark = Rgba.Parse("#F2C86A");
    private static readonly Rgba MoonCrater = Rgba.Parse("#E8B85A");
    private static readonly Rgba MoonFace = Rgba.Parse("#8A6A2A");
    private static readonly Rgba ShadowInk = Rgba.Parse("#1E2350");

    public const float MoonUnits = 48f;  // moon surface: glow r20 plus soft edge
    public const float StarUnits = 18f;  // star surface: star r8

    /// <summary>Draws rig shapes (body, face, leaf…) in canvas units.</summary>
    public static void Shapes(D2DContext dc, float scale, Rig rig, MoodColors mood, IEnumerable<RigShape> shapes)
    {
        dc.SetTransform(Matrix3x2.CreateTranslation(rig.OffsetX, rig.OffsetY) * Matrix3x2.CreateScale(scale));
        foreach (var s in shapes) Shape(dc, rig, mood, s);
    }

    private static void Shape(D2DContext dc, Rig rig, MoodColors mood, RigShape s)
    {
        if (s.Kind is "ellipse" or "circle")
        {
            var c = new Vector2(s.Cx, s.Cy);
            var r = s.Kind == "circle" ? new Vector2(s.R) : new Vector2(s.Rx, s.Ry);
            if (s.Fill == "$blush")
            {
                // feGaussianBlur stdDeviation 3
                SoftEllipse(dc, c, r, mood.Blush, s.Opacity, 6f);
                return;
            }
            if (s.Fill != null) dc.FillEllipse(c, r, Brush(dc, rig, mood, s.Fill, s.Opacity, c - r, c + r));
            if (s.Stroke != null) dc.DrawEllipse(c, r, Brush(dc, rig, mood, s.Stroke, s.Opacity, c - r, c + r), s.StrokeWidth, dc.StrokeStyle(s.LineCap));
            return;
        }

        var geometry = dc.Path(PathData.Parse(s.D!));
        var b = dc.Bounds(geometry);
        var min = new Vector2(b.Left, b.Top);
        var max = new Vector2(b.Right, b.Bottom);
        if (s.Fill != null) dc.FillGeometry(geometry, Brush(dc, rig, mood, s.Fill, s.Opacity, min, max));
        if (s.Stroke != null) dc.DrawGeometry(geometry, Brush(dc, rig, mood, s.Stroke, s.Opacity, min, max), s.StrokeWidth, dc.StrokeStyle(s.LineCap));
    }

    /// <summary>A colour, or a mood slot. Gradients use SVG objectBoundingBox units over [min, max].</summary>
    private static nint Brush(D2DContext dc, Rig rig, MoodColors mood, string paint, float opacity, Vector2 min, Vector2 max)
    {
        var size = max - min;
        switch (paint)
        {
            case "$body":
                // radialGradient cx 38% cy 26% r 90%: top → mid (0.55) → bottom
                return dc.RadialBrush(
                    [Stop(0, mood.Top), Stop(0.55f, mood.Mid), Stop(1, mood.Bottom)],
                    min + size * new Vector2(0.38f, 0.26f), size * 0.9f, opacity);
            case "$leaf":
                return dc.LinearBrush(
                    [Stop(0, Rgba.Parse(rig.LeafGradient[0])), Stop(1, Rgba.Parse(rig.LeafGradient[1]))], min, max, opacity);
            case "$bottom": return dc.SolidBrush(new D2DColor(mood.Bottom), opacity);
            case "$blush": return dc.SolidBrush(new D2DColor(mood.Blush), opacity);
            case "$bud": return dc.SolidBrush(new D2DColor(mood.Bud), opacity);
            case "$glow": return dc.SolidBrush(new D2DColor(mood.Glow), opacity);
            default: return dc.SolidBrush(new D2DColor(Rgba.Parse(paint)), opacity);
        }
    }

    private static D2DGradientStop Stop(float at, Rgba c, float alpha = 1f) => new() { Position = at, Color = new D2DColor(c, alpha) };

    /// <summary>An ellipse blurred by about <paramref name="soft"/>/2 (stdDeviation): full colour inside, fading out past the edge.</summary>
    private static void SoftEllipse(D2DContext dc, Vector2 c, Vector2 r, Rgba color, float opacity, float soft)
    {
        var outer = r + new Vector2(soft);
        float inner = Math.Clamp((r.X - soft) / outer.X, 0f, 0.95f);
        var brush = dc.RadialBrush([Stop(0, color), Stop(inner, color), Stop(1, color, 0)], c, outer, opacity);
        dc.FillEllipse(c, outer, brush);
    }

    /// <summary>The halo behind her: circle r120 at the canvas centre, glow 0.75 → 0. Opacity is animated on the visual.</summary>
    public static void Halo(D2DContext dc, float scale, MoodColors mood)
    {
        dc.SetTransform(Matrix3x2.CreateScale(scale));
        var c = new Vector2(150, 150);
        dc.FillEllipse(c, new Vector2(120), dc.RadialBrush([Stop(0, mood.Glow, 0.75f), Stop(1, mood.Glow, 0)], c, new Vector2(120)));
    }

    /// <summary>Ground shadow: ellipse at (150,268) rx62 ry8, #1E2350 at 0.16.</summary>
    public static void Shadow(D2DContext dc, float scale)
    {
        dc.SetTransform(Matrix3x2.CreateScale(scale));
        dc.FillEllipse(new Vector2(150, 268), new Vector2(62, 8), dc.SolidBrush(new D2DColor(ShadowInk), 0.16f));
    }

    /// <summary>The moon's orbit: dashed ellipse rx112 ry26 tilted −10°, glow colour at 0.55.</summary>
    public static void Orbit(D2DContext dc, float scale, MoodColors mood)
    {
        dc.SetTransform(Matrix3x2.CreateRotation(-10f * MathF.PI / 180f, new Vector2(150, 150)) * Matrix3x2.CreateScale(scale));
        const float width = 1.2f;
        dc.DrawEllipse(new Vector2(150, 150), new Vector2(112, 26), dc.SolidBrush(new D2DColor(mood.Glow), 0.55f), width,
            dc.StrokeStyle("butt", [2f / width, 6f / width]));
    }

    /// <summary>The little moon, centred in a <see cref="MoonUnits"/> square: soft glow, gradient disc, craters, sleepy face.</summary>
    public static void Moon(D2DContext dc, float scale, Mood moodKind, MoodColors mood)
    {
        dc.SetTransform(Matrix3x2.CreateTranslation(MoonUnits / 2, MoonUnits / 2) * Matrix3x2.CreateScale(scale));
        var glow = moodKind == Mood.Calm ? Star : mood.Glow;
        SoftEllipse(dc, Vector2.Zero, new Vector2(20), glow, 0.35f, 4f);
        dc.FillEllipse(Vector2.Zero, new Vector2(11),
            dc.RadialBrush([Stop(0, MoonLight), Stop(1, MoonDark)], new Vector2(-11 + 22 * 0.35f, -11 + 22 * 0.3f), new Vector2(22 * 0.8f)));
        var crater = dc.SolidBrush(new D2DColor(MoonCrater), 0.6f);
        dc.FillEllipse(new Vector2(-3, -2), new Vector2(2.2f), crater);
        dc.FillEllipse(new Vector2(4, 3), new Vector2(1.5f), crater);
        var face = dc.Path(PathData.Parse("M-4 1 q1.5 1.5 3 0 M2 1 q1.5 1.5 3 0"));
        dc.DrawGeometry(face, dc.SolidBrush(new D2DColor(MoonFace)), 1f, dc.StrokeStyle("round"));
    }

    /// <summary>One four-point star (r8), centred in a <see cref="StarUnits"/> square.</summary>
    public static void StarShape(D2DContext dc, float scale)
    {
        dc.SetTransform(Matrix3x2.CreateTranslation(StarUnits / 2, StarUnits / 2) * Matrix3x2.CreateScale(scale));
        dc.FillGeometry(dc.Path(PathData.Parse("M0 -8 L2 -2 L8 0 L2 2 L0 8 L-2 2 L-8 0 L-2 -2Z")), dc.SolidBrush(new D2DColor(Star)));
    }

    /// <summary>The leaf bud's glow: circle r9 at the bud, blurred; opacity is animated on the visual.</summary>
    public static void BudGlow(D2DContext dc, float scale, Rig rig, MoodColors mood)
    {
        dc.SetTransform(Matrix3x2.CreateTranslation(rig.OffsetX, rig.OffsetY) * Matrix3x2.CreateScale(scale));
        SoftEllipse(dc, new Vector2(124, 27), new Vector2(9), mood.Bud, 1f, 8f);
    }
}
