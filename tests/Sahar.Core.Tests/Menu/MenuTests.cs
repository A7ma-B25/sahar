using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Menu;

public class MenuTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);
    private static Vector2 Centre(RectF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static bool Inside(ButtonSlot s, RectF work, float dpi) =>
        work.Contains(new RectF(s.Centre.X - s.Radius, s.Centre.Y - s.Radius, s.Radius * 2, s.Radius * 2 + RadialLayout.LabelBandDip * dpi));

    [Fact]
    public void RadialLayout_SixButtons_FullRingAroundHer_WhenThereIsRoom()
    {
        var body = new RectF(900, 500, 80, 80);
        var slots = RadialLayout.Place(MenuItems.Main, body, Work, 1f);
        Assert.Equal(6, slots.Count);
        var c = Centre(body);
        float r0 = Vector2.Distance(slots[0].Centre, c);
        Assert.True(r0 >= 90);
        Assert.All(slots, s => Assert.InRange(Vector2.Distance(s.Centre, c), r0 - 1, r0 + 1));
        Assert.InRange(slots[0].Centre.X, c.X - 1, c.X + 1);
        Assert.True(slots[0].Centre.Y < c.Y);
        Assert.True(slots[1].Centre.X > c.X);   // clockwise: the second button is to the right
    }

    public static TheoryData<float, float, float> Corners => new()
    {
        { 0, 0, 1f }, { 1840, 0, 1f }, { 0, 960, 1f }, { 1840, 960, 1f },
        { 0, 0, 1.5f }, { 1800, 0, 1.5f }, { 0, 920, 1.5f }, { 1800, 920, 1.5f },
    };

    [Theory]
    [MemberData(nameof(Corners))]
    public void RadialLayout_EveryButtonInsideWork_FromEachCorner(float x, float y, float dpi)
    {
        var body = new RectF(x, y, 80 * dpi, 80 * dpi);
        var slots = RadialLayout.Place(MenuItems.Main, body, Work, dpi);
        Assert.Equal(6, slots.Count);
        Assert.All(slots, s => Assert.True(Inside(s, Work, dpi), $"{s.Id} at {s.Centre} is off screen"));
    }

    [Fact]
    public void RadialLayout_AtAnEdge_IsAHalfRingFacingIn()
    {
        var body = new RectF(1840, 480, 80, 80);
        var c = Centre(body);
        var slots = RadialLayout.Place(MenuItems.Main, body, Work, 1f);
        Assert.Equal(6, slots.Count);
        Assert.All(slots, s => Assert.True(s.Centre.X < c.X, $"{s.Id} is on the outer side"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void RadialLayout_ButtonsNeverOverlap(int n)
    {
        var items = Enumerable.Range(0, n).Select(i => new MenuItem($"b{i}", "•", "x")).ToList();
        foreach (var body in new[] { new RectF(900, 500, 80, 80), new RectF(1840, 480, 80, 80), new RectF(0, 960, 80, 80) })
        {
            var s = RadialLayout.Place(items, body, Work, 1f);
            Assert.Equal(n, s.Count);
            for (int i = 0; i < s.Count; i++)
                for (int j = i + 1; j < s.Count; j++)
                    Assert.True(Vector2.Distance(s[i].Centre, s[j].Centre) >= 2 * s[i].Radius + 8, $"{n} buttons overlap");
        }
    }

    [Fact]
    public void MenuItems_Main_IsExactlyTheSixWorkingButtons() =>
        Assert.Equal(["timer", "note", "activity", "dnd", "hide", "play"], MenuItems.Main.Select(i => i.Id));

    [Fact]
    public void MenuItems_Options_TimerRunning_OffersCancel()
    {
        Assert.Equal(["timer-cancel", "back"], MenuItems.Options("timer", true, false, ActivityLevel.Medium).Select(i => i.Id));
        Assert.Equal(["timer-5", "timer-15", "timer-25", "timer-45", "timer-custom", "back"],
            MenuItems.Options("timer", false, false, ActivityLevel.Medium).Select(i => i.Id));
    }

    [Fact]
    public void MenuItems_Options_Activity_ChecksTheCurrentLevel()
    {
        var o = MenuItems.Options("activity", false, false, ActivityLevel.Lively);
        Assert.Equal(["activity-calm", "activity-medium", "activity-lively", "back"], o.Select(i => i.Id));
        Assert.StartsWith("✓ ", o[2].Label);
        Assert.DoesNotContain("✓", o[0].Label);
    }

    [Fact]
    public void MenuItems_Options_Dnd_OnOffersOff()
    {
        Assert.Equal(["dnd-off", "back"], MenuItems.Options("dnd", false, true, ActivityLevel.Medium).Select(i => i.Id));
        Assert.Equal(["dnd-30", "dnd-60", "dnd-120", "dnd-on", "back"], MenuItems.Options("dnd", false, false, ActivityLevel.Medium).Select(i => i.Id));
        Assert.Empty(MenuItems.Options("hide", false, false, ActivityLevel.Medium));
    }
}

public class MenuLabelTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);

    public static TheoryData<string, float, float, float> Cases()
    {
        var d = new TheoryData<string, float, float, float>();
        var spots = new (float X, float Y)[] { (0, 0), (1840, 0), (0, 960), (1840, 960), (1700, 900), (900, 480), (1840, 480), (900, 0) };
        foreach (var ring in new[] { "main", "timer", "activity", "dnd" })
            foreach (var dpi in new[] { 1f, 1.5f })
                foreach (var (x, y) in spots)
                    d.Add(ring, x, y, dpi);
        return d;
    }

    private static IReadOnlyList<MenuItem> Items(string ring) =>
        ring == "main" ? MenuItems.Main : MenuItems.Options(ring, false, false, Sahar.Core.Settings.ActivityLevel.Medium);

    [Theory]
    [MemberData(nameof(Cases))]
    public void RadialLayout_LabelsOnScreen_AndNothingOverlaps(string ring, float x, float y, float dpi)
    {
        var body = new RectF(Math.Min(x, 1920 - 80 * dpi), Math.Min(y, 1040 - 80 * dpi), 80 * dpi, 80 * dpi);
        var slots = RadialLayout.Place(Items(ring), body, Work, dpi);
        foreach (var s in slots)
        {
            Assert.True(Work.Contains(s.Bounds), $"{ring} {s.Id} at {s.Bounds} is off screen");
            foreach (var o in slots)
            {
                if (o == s) continue;
                Assert.False(s.Label.Intersects(o.Label), $"{ring}: label {s.Id} overlaps label {o.Id}");
                Assert.False(s.Label.Intersects(o.Circle), $"{ring}: label {s.Id} overlaps button {o.Id}");
                Assert.False(s.Circle.Intersects(o.Circle), $"{ring}: button {s.Id} overlaps button {o.Id}");
            }
        }
    }

    [Fact]
    public void RadialLayout_LabelWidth_FollowsTheText()
    {
        Assert.True(RadialLayout.LabelWidthDip("لحد ما وقّفها") > RadialLayout.LabelWidthDip("5 د"));
        Assert.True(RadialLayout.LabelWidthDip(new string('x', 40)) <= 112);
    }

    [Fact]
    public void RadialLayout_HitTest_CircleOrLabel()
    {
        var slots = RadialLayout.Place(MenuItems.Main, new RectF(900, 480, 80, 80), Work, 1f);
        var s = slots[0];
        Assert.Equal(s.Id, RadialLayout.HitTest(slots, s.Centre));
        Assert.Equal(s.Id, RadialLayout.HitTest(slots, new Vector2(s.Label.X + s.Label.Width / 2, s.Label.Y + s.Label.Height / 2)));
        Assert.Null(RadialLayout.HitTest(slots, new Vector2(5, 5)));
    }
}
