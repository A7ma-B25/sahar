using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Motion;

public class PlacementTests
{
    private static readonly RectF Laptop = new(0, 0, 1920, 1032);
    private static readonly RectF External = new(1920, 0, 2560, 1400);

    [Fact]
    public void Reclamp_AfterUndock_MovesOntoRemainingMonitor()
    {
        var body = new RectF(3000, 600, 80, 77);
        var r = Placement.Reclamp(body, [Laptop]);
        Assert.True(Laptop.Contains(r));
        Assert.Equal((80f, 77f), (r.Width, r.Height));
    }

    [Fact]
    public void Reclamp_InsideStays() =>
        Assert.Equal(new RectF(500, 300, 80, 77), Placement.Reclamp(new RectF(500, 300, 80, 77), [Laptop, External]));

    [Fact]
    public void Reclamp_StraddlingTwoMonitors_StaysWhereMostOfHerIs()
    {
        var r = Placement.Reclamp(new RectF(1900, 1300, 80, 77), [Laptop, External]);
        Assert.True(External.Contains(r));
    }

    [Fact]
    public void Reclamp_ResolutionShrinks_ClampsIntoSmallerArea()
    {
        var r = Placement.Reclamp(new RectF(1800, 900, 80, 77), [new RectF(0, 0, 1024, 728)]);
        Assert.Equal(new RectF(1024 - 80, 728 - 77, 80, 77), r);
    }

    [Fact]
    public void ChooseMonitor_SwitchesOnlyPast60Percent_NoOscillation()
    {
        RectF[] monitors = [Laptop, External];
        var half = new RectF(1880, 500, 80, 77);
        Assert.Equal(0, Placement.ChooseMonitor(half, monitors, current: 0));
        Assert.Equal(1, Placement.ChooseMonitor(half, monitors, current: 1));
        Assert.Equal(1, Placement.ChooseMonitor(new RectF(1896, 500, 80, 77), monitors, current: 0));
        Assert.Equal(1, Placement.ChooseMonitor(new RectF(1884, 500, 80, 77), monitors, current: 1));
    }

    [Fact]
    public void ChooseMonitor_UnknownCurrent_PicksLargestOverlap() =>
        Assert.Equal(1, Placement.ChooseMonitor(new RectF(1900, 500, 80, 77), [Laptop, External], current: -1));
}
