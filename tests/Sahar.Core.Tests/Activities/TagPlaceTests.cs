using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Activities;

public class TagPlaceTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);
    private static Vector2 Tag(float dpi) => new(180 * dpi, TagPlace.HeightDip * dpi);

    [Fact]
    public void Place_BelowWhenRoom()
    {
        var body = new RectF(900, 400, 80, 77);
        var r = TagPlace.Place(body, Work, Tag(1), 1, out var side);
        Assert.Equal(TagSide.Below, side);
        Assert.Equal(body.Bottom + TagPlace.ThreadDip, r.Y);
        Assert.Equal(body.X + body.Width / 2, r.X + r.Width / 2, 1f);
    }

    [Fact]
    public void Place_SideWithMoreRoom_NearTaskbar()
    {
        var right = TagPlace.Place(new RectF(200, 960, 80, 77), Work, Tag(1), 1, out var s1);
        Assert.Equal(TagSide.Right, s1);
        Assert.True(right.X >= 280);
        var left = TagPlace.Place(new RectF(1700, 960, 80, 77), Work, Tag(1), 1, out var s2);
        Assert.Equal(TagSide.Left, s2);
        Assert.True(left.Right <= 1700);
    }

    [Theory]
    [InlineData(900, 400)]
    [InlineData(900, 960)]
    [InlineData(10, 960)]
    [InlineData(1830, 0)]
    public void Place_NeverAbove_AndInsideTheWorkArea(float x, float y)
    {
        var body = new RectF(x, y, 80, 77);
        var r = TagPlace.Place(body, Work, Tag(1), 1, out _);
        Assert.True(r.Y >= body.Y, "never above her: the bubble lives there");
        Assert.True(Work.Contains(r), $"{r} inside the work area");
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(1.25f)]
    public void Place_WholePixels(float dpi)
    {
        var r = TagPlace.Place(new RectF(900.37f, 400.61f, 80.3f * dpi, 77.2f * dpi), Work, Tag(dpi) + new Vector2(0.3f), dpi, out _);
        Assert.Equal(MathF.Round(r.X), r.X);
        Assert.Equal(MathF.Round(r.Y), r.Y);
        Assert.Equal(MathF.Round(r.Width), r.Width);
        Assert.Equal(MathF.Round(r.Height), r.Height);
    }

    /// <summary>Her canvas and body for a size at a DPI, the body's top-left at (x, y).</summary>
    private static (RectF Canvas, RectF Body) Her(float bodyDip, float dpi, float x, float y)
    {
        var px = RigScale.RasterSize(bodyDip, dpi).Width;
        float k = px / RigScale.CanvasUnits;
        var canvas = new RectF(x - 70 * k, y - 88 * k, px, px);
        return (canvas, new RectF(x, y, 160 * k, 154 * k));
    }

    [Theory]
    [InlineData(80f, 1f)]
    [InlineData(80f, 1.5f)]
    [InlineData(120f, 1f)]
    [InlineData(120f, 1.5f)]
    [InlineData(48f, 1f)]
    public void Place_Below_InsideFollow_NoNeed(float bodyDip, float dpi)
    {
        var (canvas, body) = Her(bodyDip, dpi, 900, 300);
        var tag = TagPlace.Place(body, new RectF(0, 0, 3840, 2100), new Vector2(TagPlace.MaxWidthDip * dpi, TagPlace.HeightDip * dpi), dpi, out var side);
        Assert.Equal(TagSide.Below, side);
        Assert.False(TagPlace.OutsideFollow(TagPlace.WithShadow(tag, dpi), OverlayPlan.Follow(canvas, dpi)));
    }

    public static IEnumerable<object[]> Layouts()
    {
        foreach (var dpi in new[] { 1f, 1.5f })
        foreach (var edge in new[] { 0, 1, 2, 3 })
        foreach (var two in new[] { false, true })
            yield return [dpi, edge, two];
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Plan_WithTagNeed_NeverCoversAMonitor(float dpi, int taskbarEdge, bool twoMonitors)
    {
        var m1 = new RectI(0, 0, (int)(1280 * dpi), (int)(720 * dpi));
        var monitors = twoMonitors ? new List<RectI> { m1, new(m1.Right, 0, m1.Right + (int)(1280 * dpi), m1.Bottom) } : [m1];
        int bar = (int)(48 * dpi);
        var work = taskbarEdge switch
        {
            0 => new RectF(bar, 0, m1.Width - bar, m1.Height),
            1 => new RectF(0, bar, m1.Width, m1.Height - bar),
            2 => new RectF(0, 0, m1.Width - bar, m1.Height),
            _ => new RectF(0, 0, m1.Width, m1.Height - bar),
        };
        foreach (var size in new[] { 48f, 80f, 120f })
        foreach (var (fx, fy) in new[] { (0.02f, 0.02f), (0.5f, 0.5f), (0.97f, 0.9f), (0.02f, 0.9f), (0.97f, 0.02f) })
        {
            var (canvas, body) = Her(size, dpi, work.X + fx * (work.Width - 160), work.Y + fy * (work.Height - 160));
            var tag = TagPlace.WithShadow(TagPlace.Place(body, work, new Vector2(TagPlace.MaxWidthDip * dpi, TagPlace.HeightDip * dpi), dpi, out _), dpi);
            var plan = OverlayPlan.Plan(canvas, dpi, [tag], monitors);
            foreach (var m in monitors) Assert.False(OverlayPlan.Covers(plan, m), $"{plan} covers {m}");
        }
    }

    [Fact]
    public void Thread_GrowsWithBeads()
    {
        Assert.Equal(16, TagPlace.Thread(0, false));
        Assert.Equal(43, TagPlace.Thread(3, false));
        Assert.Equal(43 + TagPlace.KnotDip, TagPlace.Thread(3, true));
        var body = new RectF(900, 400, 80, 77);
        Assert.Equal(body.Bottom + 43, TagPlace.Place(body, Work, Tag(1), 1, out _, TagPlace.Thread(3, false)).Y);
    }
}
