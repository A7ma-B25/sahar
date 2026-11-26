using Sahar.Core.Geometry;
using Sahar.Core.Surfaces;
using Xunit;

namespace Sahar.Core.Tests.Surfaces;

public class PanelPlacementTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void BesideHerOnTheRoomierSide_InsideTheWorkArea(float dpi)
    {
        var size = new SizeF(320 * dpi, 600 * dpi);
        var nearRight = PanelPlacement.Place(new RectF(1800, 900, 100 * dpi, 100 * dpi), Work, size, dpi);
        Assert.True(nearRight.Rect.Right <= 1800, "opens to her left when the right is short");
        Assert.True(nearRight.OnHerLeft);
        Assert.True(Work.Contains(nearRight.Rect));
        var nearLeft = PanelPlacement.Place(new RectF(10, 10, 100, 100), Work, size, dpi);
        Assert.True(nearLeft.Rect.X >= 110);
        Assert.True(Work.Contains(nearLeft.Rect));
    }

    [Fact]
    public void BottomAlignedWithHer_ClaspAtHerFaceInsideThePanel()
    {
        var body = new RectF(1000, 700, 100, 100);
        var p = PanelPlacement.Place(body, Work, new SizeF(320, 400), 1f);
        Assert.Equal(body.Bottom, p.Rect.Bottom);
        Assert.InRange(p.ClaspY, p.Rect.Y + 24, p.Rect.Bottom - 24);
        Assert.Equal(body.Y + body.Height * 0.45f, p.ClaspY, 1);
    }

    [Fact]
    public void TallerThanTheScreen_StaysInside()
    {
        var work = new RectF(0, 0, 800, 500);
        var p = PanelPlacement.Place(new RectF(100, 100, 80, 80), work, new SizeF(320, 620), 1f);
        Assert.Equal(0, p.Rect.Y);
        Assert.True(p.Rect.Height <= 500);
    }

    [Fact]
    public void SecondMonitorWithNegativeCoordinates()
    {
        var work = new RectF(-1280, 0, 1280, 984);
        var p = PanelPlacement.Place(new RectF(-1250, 800, 120, 120), work, new SizeF(480, 900), 1.5f);
        Assert.True(work.Contains(p.Rect));
    }

    [Fact]
    public void Above_CentredOverHer_OrBelowWhenSheIsAtTheTop()
    {
        var r = PanelPlacement.Above(new RectF(900, 600, 100, 100), Work, new SizeF(264, 90), 1f);
        Assert.Equal(950, r.X + r.Width / 2, 1);
        Assert.True(r.Bottom <= 600);
        var top = PanelPlacement.Above(new RectF(900, 0, 100, 100), Work, new SizeF(264, 90), 1f);
        Assert.True(top.Y >= 100);
        Assert.True(Work.Contains(top));
    }

    [Fact]
    public void Anchor_AboveTheTrayIcon_InsideTheWorkArea()
    {
        var r = PanelPlacement.Anchor(1900, 1060, Work, new SizeF(300, 420), 1f);
        Assert.True(Work.Contains(r));
        Assert.Equal(1040, r.Bottom);
        var left = PanelPlacement.Anchor(20, 1060, Work, new SizeF(300, 420), 1.5f);
        Assert.Equal(0, left.X);
    }
}
