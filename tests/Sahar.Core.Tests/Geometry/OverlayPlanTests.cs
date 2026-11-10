using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Geometry;

public class OverlayPlanTests
{
    private static readonly RectI Monitor = new(0, 0, 1920, 1080);
    private static readonly RectI[] One = [Monitor];

    [Fact]
    public void Follow_IsCanvasPlusMargins() =>
        Assert.Equal(new RectI(100 - 220, 100 - 140, 250 + 220, 250 + 140), OverlayPlan.Follow(new RectF(100, 100, 150, 150), 1f));

    [Fact]
    public void Follow_ScalesTheMarginsWithDpi_AndRoundsOutward() =>
        Assert.Equal(new RectI(100 - 440, 100 - 280, 251 + 440, 251 + 280), OverlayPlan.Follow(new RectF(100.4f, 100.6f, 150.2f, 150.3f), 2f));

    [Fact]
    public void Plan_WithNoNeeds_NeverCoversAMonitor()
    {
        // At rest, bottom-right of the work area (where she starts).
        var r = OverlayPlan.Plan(new RectF(1920 - 24 - 150, 1032 - 150, 150, 150), 1f, [], One);
        Assert.False(OverlayPlan.Covers(r, Monitor));
        Assert.True(r.Width < 1920 / 2 && r.Height < 1080 / 2, $"{r} is not her size");
    }

    [Fact]
    public void Plan_WithWorkAreaNeed_ContainsTheWorkArea_NotTheMonitor()
    {
        var r = OverlayPlan.Plan(new RectF(800, 400, 150, 150), 1f, [new RectF(0, 0, 1920, 1032)], One);
        Assert.Equal(new RectI(0, 0, 1920, 1032), r);
        Assert.False(OverlayPlan.Covers(r, Monitor));
    }

    [Fact]
    public void Plan_WithMonitorSizedNeed_LosesOnlyAOnePixelStrip()
    {
        var r = OverlayPlan.Plan(new RectF(1700, 880, 150, 150), 1f, [new RectF(0, 0, 1920, 1080)], One);
        Assert.False(OverlayPlan.Covers(r, Monitor));
        Assert.True(OverlayPlan.Covers(r, new RectI(1, 1, 1919, 1079)), $"{r} lost more than one pixel of the monitor");
    }

    [Fact]
    public void Plan_WithANeed_DoesNotResizeAsSheMovesALittle()
    {
        // Showcase at the bottom-right: her margins pass the screen edge; flying a few pixels must not resize the window.
        RectF work = new(0, 0, 1024, 728);
        var a = OverlayPlan.Plan(new RectF(900, 560, 150, 150), 1f, [work], [new RectI(0, 0, 1024, 768)]);
        var b = OverlayPlan.Plan(new RectF(904, 563, 150, 150), 1f, [work], [new RectI(0, 0, 1024, 768)]);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Plan_WithANeed_StillHoldsHerAndHerMargins()
    {
        var canvas = new RectF(900, 560, 150, 150);
        var r = OverlayPlan.Plan(canvas, 1f, [new RectF(0, 0, 1024, 728)], [new RectI(0, 0, 1024, 768)]);
        var f = OverlayPlan.Follow(canvas, 1f);
        Assert.True(r.Left <= f.Left && r.Top <= f.Top && r.Right >= f.Right && r.Bottom >= f.Bottom, $"{r} does not hold {f}");
    }

    [Fact]
    public void Uncover_TwoMonitors_UnionIsInsetSoNeitherIsCovered()
    {
        RectI a = new(0, 0, 1920, 1080), b = new(1920, 0, 4480, 1440);
        var r = OverlayPlan.Uncover(new RectI(0, 0, 4480, 1440), [a, b]);
        Assert.False(OverlayPlan.Covers(r, a));
        Assert.False(OverlayPlan.Covers(r, b));
        Assert.True(OverlayPlan.Covers(r, new RectI(1, 0, 4479, 1440)), $"{r} lost more than a pixel per monitor");
    }

    [Fact]
    public void Uncover_MonitorInsideTheRect_IsUncoveredByTheCheapestSide()
    {
        RectI middle = new(1000, 1000, 2000, 2000);
        var r = OverlayPlan.Uncover(new RectI(0, 0, 3000, 2000), [middle]);
        Assert.False(OverlayPlan.Covers(r, middle));
        Assert.Equal(new RectI(0, 0, 3000, 1999), r);
    }

    [Fact]
    public void Uncover_NothingCovered_Unchanged() =>
        Assert.Equal(new RectI(10, 10, 500, 400), OverlayPlan.Uncover(new RectI(10, 10, 500, 400), One));

    [Fact]
    public void Covers_LargerRect_True() => Assert.True(OverlayPlan.Covers(new RectI(-1, -1, 1921, 1081), Monitor));

    [Fact]
    public void Covers_InsetRect_False() => Assert.False(OverlayPlan.Covers(new RectI(0, 0, 1920, 1079), Monitor));
}
