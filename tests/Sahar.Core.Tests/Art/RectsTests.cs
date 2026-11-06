using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Art;

public class RectsTests
{
    [Fact]
    public void RectF_ContainsAndIntersects()
    {
        var outer = new RectF(0, 0, 100, 100);
        Assert.True(outer.Contains(new RectF(10, 10, 20, 20)));
        Assert.False(outer.Contains(new RectF(90, 90, 20, 20)));
        Assert.True(outer.Intersects(new RectF(90, 90, 20, 20)));
        Assert.False(outer.Intersects(new RectF(100, 0, 10, 10)));
    }

    [Fact]
    public void RectF_FromRectI_Scales() =>
        Assert.Equal(new RectF(10, 20, 100, 50), RectF.FromRectI(new RectI(15, 30, 165, 105), 1.5f));
}
