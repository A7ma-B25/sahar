using Sahar.Core.Art;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Art;

public class RigScaleTests
{
    [Theory]
    [InlineData(80f, 1.0f, 150)]
    [InlineData(80f, 1.5f, 225)]
    [InlineData(120f, 2.0f, 450)]
    [InlineData(48f, 1.25f, 113)]
    public void Rig_RasterSize_ScalesWithDpi(float bodyDip, float dpiScale, int px) =>
        Assert.Equal(new SizeI(px, px), RigScale.RasterSize(bodyDip, dpiScale));

    [Fact]
    public void Rig_DipPerUnit_MatchesBodyWidth() => Assert.Equal(0.5f, RigScale.DipPerUnit(80f), 0.0001f);
}
