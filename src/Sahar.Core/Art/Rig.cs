using Sahar.Core.Expressions;
using Sahar.Core.Json;

namespace Sahar.Core.Art;

/// <summary>
/// One drawable shape in the 300×300 design canvas. Kind is "path", "ellipse" or "circle".
/// Fill and Stroke are "#RRGGBB" or a mood slot: "$body" (top→mid→bottom gradient), "$bottom", "$blush", "$bud", "$leaf".
/// </summary>
public sealed record RigShape(
    string Kind, string? D = null, float Cx = 0, float Cy = 0, float Rx = 0, float Ry = 0, float R = 0,
    string? Fill = null, string? Stroke = null, float StrokeWidth = 0, string LineCap = "butt", float Opacity = 1);

/// <summary>A four-point star: path, fill and an optional warm edge (spec L1), in the star's own units.</summary>
public sealed record RigStarShape(string D, string Fill, string? Edge, float EdgeWidth);

/// <summary>Where a star sits, its size and its twinkle (period and start, seconds).</summary>
public sealed record RigSpot(float X, float Y, float Scale, float Period, float Begin);

/// <summary>A small white dot inside her (spec L3).</summary>
public sealed record RigDot(float X, float Y, float R, float Period, float Begin);

/// <summary>The night wash ellipse (rig units) and its radial gradient (fractions of the ellipse's box).</summary>
public sealed record RigWash(float Cx, float Cy, float Rx, float Ry, float GradCx, float GradCy, float GradR, float[] Offsets, float[] Opacities);

/// <summary>The 0.10a sky: outer stars (canvas units, 0–300) with their edge; the night inside her lower body
/// (rig units: add OffsetX/Y to place) with its stars and dots; and the wash colours per mood (inner, mid, outer).</summary>
public sealed record RigSky(RigStarShape Star, RigSpot[] OuterStars, RigStarShape InnerStar, RigSpot[] InnerStars,
    RigDot[] InnerDots, RigWash Wash, Dictionary<string, string[]> WashStops);

/// <summary>Sahar's drawable parts and face variants, from assets/art/sahar-rig.json.</summary>
public sealed record Rig(
    int Canvas, float OffsetX, float OffsetY, float BodyWidth, string[] LeafGradient,
    Dictionary<string, RigShape[]> Parts, Dictionary<string, RigShape[]> Eyes, Dictionary<string, RigShape[]> Mouths, RigSky Sky)
{
    public static Rig Load() => Assets.Read("sahar-rig.json", CoreJson.Default.Rig);

    public IReadOnlyList<RigShape> EyesFor(Expressions.Eyes e) => Eyes[e.ToString()];

    public IReadOnlyList<RigShape> MouthFor(Mouth m) => Mouths[m.ToString()];
}
