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

/// <summary>Sahar's drawable parts and face variants, from assets/art/sahar-rig.json.</summary>
public sealed record Rig(
    int Canvas, float OffsetX, float OffsetY, float BodyWidth, string[] LeafGradient,
    Dictionary<string, RigShape[]> Parts, Dictionary<string, RigShape[]> Eyes, Dictionary<string, RigShape[]> Mouths)
{
    public static Rig Load() => Assets.Read("sahar-rig.json", CoreJson.Default.Rig);

    public IReadOnlyList<RigShape> EyesFor(Expressions.Eyes e) => Eyes[e.ToString()];

    public IReadOnlyList<RigShape> MouthFor(Mouth m) => Mouths[m.ToString()];
}
