using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class ActivityStackTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static Activity A(string id, ActivityKind k) => new(id, k, id);

    [Fact]
    public void Rank_FollowsTheSpec()
    {
        ActivityKind[] order =
        [
            ActivityKind.ClaudeAsk, ActivityKind.WeatherAlert, ActivityKind.Copy, ActivityKind.ClaudeWorking, ActivityKind.Music,
            ActivityKind.CameraMic, ActivityKind.Shelf, ActivityKind.MusicPaused, ActivityKind.WeatherToday,
        ];
        for (int i = 1; i < order.Length; i++) Assert.True(ActivityRank.Of(order[i - 1]) < ActivityRank.Of(order[i]), $"{order[i - 1]} before {order[i]}");
        foreach (var m in new[] { ActivityKind.Screenshot, ActivityKind.CopyDone, ActivityKind.ClaudeDone, ActivityKind.Back,
                     ActivityKind.BatteryLow, ActivityKind.Charging, ActivityKind.Volume })
        {
            Assert.True(ActivityRank.IsMoment(m));
            Assert.Equal(ActivityRank.Of(ActivityKind.WeatherAlert), ActivityRank.Of(m));
        }
        Assert.Equal(ActivityRank.Of(ActivityKind.Copy), ActivityRank.Of(ActivityKind.Timer));
        Assert.False(ActivityRank.IsMoment(ActivityKind.Music));
    }

    [Fact]
    public void View_EqualRank_NewestFirst()
    {
        var s = new ActivityStack();
        s.Set(A("copy", ActivityKind.Copy), T0);
        s.Set(A("timer", ActivityKind.Timer), T0.AddSeconds(5));
        Assert.Equal("timer", s.View(T0.AddSeconds(6))!.Front.Id);
    }

    [Fact]
    public void View_HigherRankWins()
    {
        var s = new ActivityStack();
        s.Set(A("claude", ActivityKind.ClaudeWorking), T0.AddSeconds(5));
        s.Set(A("music", ActivityKind.Music), T0.AddSeconds(9));
        var v = s.View(T0.AddSeconds(10))!;
        Assert.Equal("claude", v.Front.Id);
        Assert.Equal(["music"], v.Beads.Select(b => b.Id));
    }

    [Fact]
    public void Set_SameId_KeepsItsPlaceInTime()
    {
        var s = new ActivityStack();
        s.Set(A("a", ActivityKind.Copy), T0);
        s.Set(A("b", ActivityKind.Copy), T0.AddSeconds(1));
        s.Set(new Activity("a", ActivityKind.Copy, "a 50%"), T0.AddSeconds(2));   // an update is not news
        var v = s.View(T0.AddSeconds(3))!;
        Assert.Equal("b", v.Front.Id);
        Assert.Equal("a 50%", v.Beads[0].Text);
    }

    [Fact]
    public void Moment_TakesFront_ThenGivesPickBack()
    {
        var s = new ActivityStack();
        s.Set(A("claude", ActivityKind.ClaudeWorking), T0);
        s.Set(A("music", ActivityKind.Music), T0);
        s.Pick("music", T0.AddSeconds(1));
        Assert.Equal("music", s.View(T0.AddSeconds(2))!.Front.Id);
        s.Moment(A("shot", ActivityKind.Screenshot), T0.AddSeconds(3));
        Assert.Equal("shot", s.View(T0.AddSeconds(8.9))!.Front.Id);
        Assert.Equal("music", s.View(T0.AddSeconds(9.1))!.Front.Id);
    }

    [Fact]
    public void Volume_Moment_LastsOneAndAHalfSeconds()
    {
        var s = new ActivityStack();
        s.Moment(A("volume", ActivityKind.Volume), T0);
        Assert.Equal(TimeSpan.FromSeconds(1.5), ActivityRank.MomentLength(ActivityKind.Volume));
        Assert.Equal(TimeSpan.FromSeconds(6), ActivityRank.MomentLength(ActivityKind.ClaudeDone));
        Assert.NotNull(s.View(T0.AddSeconds(1.4)));
        Assert.Null(s.View(T0.AddSeconds(1.6)));
    }

    [Fact]
    public void Moment_SameId_Extends()
    {
        var s = new ActivityStack();
        s.Moment(A("volume", ActivityKind.Volume), T0);
        s.Moment(new Activity("volume", ActivityKind.Volume, "70"), T0.AddSeconds(1));
        var v = s.View(T0.AddSeconds(2))!;
        Assert.Equal("70", v.Front.Text);
        Assert.Empty(v.Beads);
    }

    [Fact]
    public void Ask_OverridesAnOlderPick_ThenAPickAfterItWins()
    {
        var s = new ActivityStack();
        s.Set(A("music", ActivityKind.Music), T0);
        s.Set(A("claude", ActivityKind.ClaudeWorking), T0);
        s.Pick("music", T0.AddSeconds(1));
        s.Set(new Activity("ask", ActivityKind.ClaudeAsk, "ask", Asking: true), T0.AddSeconds(2));
        Assert.Equal("ask", s.View(T0.AddSeconds(3))!.Front.Id);
        s.Pick("music", T0.AddSeconds(4));
        Assert.Equal("music", s.View(T0.AddSeconds(5))!.Front.Id);
    }

    [Fact]
    public void Beads_CappedAtThree_ThenKnot()
    {
        var s = new ActivityStack();
        foreach (var (id, k) in new[] { ("a", ActivityKind.Copy), ("b", ActivityKind.ClaudeWorking), ("c", ActivityKind.Music),
                     ("d", ActivityKind.CameraMic), ("e", ActivityKind.Shelf) }) s.Set(A(id, k), T0);
        var v = s.View(T0)!;
        Assert.Equal("a", v.Front.Id);
        Assert.Equal(["b", "c", "d"], v.Beads.Select(b => b.Id));
        Assert.True(v.Knot);
        s.Remove("e");
        Assert.False(s.View(T0)!.Knot);
    }

    [Fact]
    public void Wheel_CyclesThroughAll()
    {
        var s = new ActivityStack();
        s.Set(A("a", ActivityKind.Copy), T0);
        s.Set(A("b", ActivityKind.Music), T0);
        s.Set(A("c", ActivityKind.Shelf), T0);
        var seen = new List<string>();
        for (int i = 0; i < 4; i++) { seen.Add(s.View(T0)!.Front.Id); s.Wheel(1, T0.AddSeconds(i)); }
        Assert.Equal(["a", "b", "c", "a"], seen);
        // The loop's last turn brought "b" forward: two back is "c" (the cycle wraps).
        s.Wheel(-1, T0.AddSeconds(9));
        s.Wheel(-1, T0.AddSeconds(10));
        Assert.Equal("c", s.View(T0.AddSeconds(10))!.Front.Id);
    }

    [Fact]
    public void Pick_UnknownOrGone_FallsBackToRank()
    {
        var s = new ActivityStack();
        s.Set(A("a", ActivityKind.Copy), T0);
        s.Set(A("b", ActivityKind.Music), T0);
        s.Pick("b", T0);
        s.Remove("b");
        Assert.Equal("a", s.View(T0)!.Front.Id);
    }

    [Fact]
    public void WeatherToday_OnlyWhenAlone()
    {
        var s = new ActivityStack();
        s.Set(A("weather", ActivityKind.WeatherToday), T0);
        Assert.Equal("weather", s.View(T0)!.Front.Id);
        s.Set(A("music", ActivityKind.Music), T0);
        var v = s.View(T0)!;
        Assert.Equal("music", v.Front.Id);
        Assert.Empty(v.Beads);
    }

    [Fact]
    public void ScreenTime_NeverHangsAlone_ButIsInTheCycle()
    {
        var s = new ActivityStack();
        s.Set(A("time", ActivityKind.ScreenTime), T0);
        Assert.Null(s.View(T0));
        s.Set(A("music", ActivityKind.Music), T0);
        Assert.Equal("music", s.View(T0)!.Front.Id);
        s.Wheel(1, T0);
        Assert.Equal("time", s.View(T0)!.Front.Id);
    }

    [Fact]
    public void Empty_NoView() => Assert.Null(new ActivityStack().View(T0));

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    public void TagShown_FalseWhileCardMenuHiddenOrDragged(bool card, bool menu, bool hidden, bool dragged, bool shown) =>
        Assert.Equal(shown, ActivityStack.TagShown(card, menu, hidden, dragged));
}
