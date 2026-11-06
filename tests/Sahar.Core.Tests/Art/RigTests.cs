using Sahar.Core.Art;
using Sahar.Core.Expressions;

namespace Sahar.Core.Tests.Art;

public class RigTests
{
    private static readonly Rig R = Rig.Load();

    private static IEnumerable<RigShape> AllShapes() =>
        R.Parts.Values.SelectMany(x => x)
            .Concat(R.Eyes.Values.SelectMany(x => x))
            .Concat(R.Mouths.Values.SelectMany(x => x));

    [Fact]
    public void Rig_AllPaths_Parse()
    {
        foreach (var s in AllShapes())
        {
            if (s.Kind == "path") Assert.NotEmpty(PathData.Parse(s.D!));
            else Assert.Contains(s.Kind, new[] { "ellipse", "circle" });
        }
    }

    [Fact]
    public void Rig_Body_MatchesDesign() => Assert.Equal(
        "M40 176 C 34 112, 72 58, 120 58 C 168 58, 206 112, 200 176 C 200 196, 184 206, 170 196 C 158 210, 136 212, 120 200 C 104 212, 82 210, 70 196 C 56 206, 40 196, 40 176 Z",
        R.Parts["body"].Single().D);

    [Fact]
    public void Rig_HasEveryEyesAndMouthVariant()
    {
        foreach (var e in Enum.GetValues<Eyes>()) Assert.True(R.EyesFor(e).Count > 0, $"eyes {e}");
        foreach (var m in Enum.GetValues<Mouth>().Where(m => m != Mouth.None)) Assert.True(R.MouthFor(m).Count > 0, $"mouth {m}");
        Assert.Empty(R.MouthFor(Mouth.None));
    }

    [Fact]
    public void Rig_Geometry_Constants() => Assert.Equal((300, 30, 30, 160), (R.Canvas, R.OffsetX, R.OffsetY, R.BodyWidth));
}
