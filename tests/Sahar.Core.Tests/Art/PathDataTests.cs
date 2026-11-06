using System.Numerics;
using Sahar.Core.Art;

namespace Sahar.Core.Tests.Art;

public class PathDataTests
{
    [Fact]
    public void Parse_AbsoluteCubic()
    {
        var c = PathData.Parse("M40 176 C 34 112, 72 58, 120 58 Z");
        Assert.Equal(3, c.Count);
        Assert.Equal(PathOp.MoveTo, c[0].Op);
        Assert.Equal(new Vector2(40, 176), c[0].P3);
        Assert.Equal(PathOp.CubicTo, c[1].Op);
        Assert.Equal(new Vector2(34, 112), c[1].P1);
        Assert.Equal(new Vector2(72, 58), c[1].P2);
        Assert.Equal(new Vector2(120, 58), c[1].P3);
        Assert.Equal(PathOp.Close, c[2].Op);
    }

    [Fact]
    public void Parse_RelativeQuad()
    {
        var c = PathData.Parse("M120 62 q-9 3 -16 10");
        Assert.Equal(PathOp.QuadTo, c[1].Op);
        Assert.Equal(new Vector2(111, 65), c[1].P1);
        Assert.Equal(new Vector2(104, 72), c[1].P3);
    }

    [Fact]
    public void Parse_RelativeArc()
    {
        var c = PathData.Parse("M126 162 a9 9 0 1 0 6 -14");
        var a = c[1];
        Assert.Equal(PathOp.ArcTo, a.Op);
        Assert.Equal(new Vector2(9, 9), a.Radii);
        Assert.True(a.LargeArc);
        Assert.False(a.Sweep);
        Assert.Equal(new Vector2(132, 148), a.P3);
    }

    [Fact]
    public void Parse_SmoothQuadT()
    {
        var c = PathData.Parse("M88 122 q10 -12 20 0 t20 0");
        Assert.Equal(PathOp.QuadTo, c[2].Op);
        Assert.Equal(new Vector2(118, 134), c[2].P1);
        Assert.Equal(new Vector2(128, 122), c[2].P3);
    }

    [Fact]
    public void Parse_ImplicitRepeat()
    {
        var c = PathData.Parse("M0 0 l 1 1 2 2");
        Assert.Equal(3, c.Count);
        Assert.All(c.Skip(1), x => Assert.Equal(PathOp.LineTo, x.Op));
        Assert.Equal(new Vector2(1, 1), c[1].P3);
        Assert.Equal(new Vector2(3, 3), c[2].P3);
    }

    [Fact]
    public void Parse_HorizontalVerticalAndSmoothCubic()
    {
        var c = PathData.Parse("M10 10 h5 V20 s5 5 10 0");
        Assert.Equal(new Vector2(15, 10), c[1].P3);
        Assert.Equal(new Vector2(15, 20), c[2].P3);
        Assert.Equal(PathOp.CubicTo, c[3].Op);
        Assert.Equal(new Vector2(15, 20), c[3].P1); // previous was not a cubic: first control = current point
        Assert.Equal(new Vector2(20, 25), c[3].P2);
        Assert.Equal(new Vector2(25, 20), c[3].P3);
    }

    [Fact]
    public void Parse_CompactNumbers()
    {
        var c = PathData.Parse("M.5-1.5L2e1,3");
        Assert.Equal(new Vector2(0.5f, -1.5f), c[0].P3);
        Assert.Equal(new Vector2(20, 3), c[1].P3);
    }

    [Fact]
    public void Parse_Invalid_Throws() => Assert.Throws<FormatException>(() => PathData.Parse("M 1"));
}
