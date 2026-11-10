using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Geometry;

public class OverlayPlanTests
{
    private static readonly RectI Monitor = new(0, 0, 1920, 1080);
    private static readonly RectI[] One = [Monitor];

    private static bool Holds(RectI r, RectF f) => r.Left <= f.X && r.Top <= f.Y && r.Right >= f.Right && r.Bottom >= f.Bottom;

    [Fact]
    public void Follow_HoldsCanvasPlusMargins()
    {
        var r = OverlayPlan.Follow(new RectF(100, 100, 150, 150), 1f);
        Assert.Equal((100 - 220, 100 - 140), (r.Left, r.Top));
        Assert.True(Holds(r, new RectF(100 - 220, 100 - 140, 150 + 440, 150 + 280)), $"{r}");
    }

    [Fact]
    public void Follow_ScalesTheMarginsWithDpi() =>
        Assert.True(Holds(OverlayPlan.Follow(new RectF(100.4f, 100.6f, 150.2f, 150.3f), 2f), new RectF(100.4f - 440, 100.6f - 280, 150.2f + 880, 150.3f + 560)));

    [Theory]
    [InlineData(100f, 100f)]
    [InlineData(100.5f, 100.2f)]
    [InlineData(100.99f, 100.7f)]
    [InlineData(-37.3f, 12.6f)]
    public void Follow_SizeNeverChangesWithTheFraction(float x, float y)
    {
        // Fixed size: a flight moves the window (position only), it never resizes it by a pixel (review minor 5).
        var r = OverlayPlan.Follow(new RectF(x, y, 150, 150), 1f);
        var rest = OverlayPlan.Follow(new RectF(0, 0, 150, 150), 1f);
        Assert.Equal((rest.Width, rest.Height), (r.Width, r.Height));
        Assert.True(Holds(r, new RectF(x - 220, y - 140, 590, 430)), $"{r}");
    }

    [Fact]
    public void Plan_WithANeed_IsConstantWhileSheIsAnywhereInsideIt()
    {
        // Review Important 2: no re-snap mid-feature (a window move and a root offset could land a frame apart).
        RectI monitor = new(0, 0, 1024, 768);
        RectF work = new(0, 0, 1024, 728);
        var first = OverlayPlan.Plan(new RectF(0, 0, 150, 150), 1f, [work], [monitor]);
        for (float x = 0; x <= 1024 - 150; x += 37.3f)
            for (float y = 0; y <= 728 - 150; y += 29.1f)
                Assert.Equal(first, OverlayPlan.Plan(new RectF(x, y, 150, 150), 1f, [work], [monitor]));
        Assert.False(OverlayPlan.Covers(first, monitor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ShortOnOneSide_LeavesOnlyThatSideOnePixelShort(int edge)
    {
        // Review Important 1: a catcher takes clicks at x=0, y=0, Right−1, Bottom−1 — except on one side (the taskbar's).
        RectI m = new(-1920, 0, 0, 1080);
        var r = OverlayPlan.ShortOnOneSide(m, edge);
        Assert.False(OverlayPlan.Covers(r, m));
        Assert.Equal(edge == 0 ? m.Left + 1 : m.Left, r.Left);
        Assert.Equal(edge == 1 ? m.Top + 1 : m.Top, r.Top);
        Assert.Equal(edge == 2 ? m.Right - 1 : m.Right, r.Right);
        Assert.Equal(edge == 3 ? m.Bottom - 1 : m.Bottom, r.Bottom);
    }

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
        Assert.True(Holds(r, new RectF(0, 0, 1920, 1032)), $"{r}");
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
        // On screen, less a 1-px strip at the monitor's edge (Uncover's price): off-screen parts draw nothing anyway.
        RectI seen = new(Math.Max(f.Left, 1), Math.Max(f.Top, 1), Math.Min(f.Right, 1023), Math.Min(f.Bottom, 767));
        Assert.True(r.Left <= seen.Left && r.Top <= seen.Top && r.Right >= seen.Right && r.Bottom >= seen.Bottom, $"{r} does not hold {seen}");
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
