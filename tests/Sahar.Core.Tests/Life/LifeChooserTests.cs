using Sahar.Core.Life;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Life;

public class LifeChooserTests
{
    private static LifeContext Ctx(bool typing = false, bool video = false, bool music = false, bool away = false,
        bool hidden = false, bool busy = false, int hour = 14, bool perch = true) =>
        new(typing, video, music, away, hidden, busy, hour, perch);

    private static Dictionary<LifeAction, int> Draw(LifeContext c, int n, Func<LifeAction, float>? w = null, int seed = 1)
    {
        var rng = new Random(seed);
        var counts = Enum.GetValues<LifeAction>().ToDictionary(a => a, _ => 0);
        for (int i = 0; i < n; i++) counts[LifeChooser.Choose(c, w ?? (_ => 1f), rng)]++;
        return counts;
    }

    [Theory]
    [InlineData(ActivityLevel.Calm, 300, 600)]
    [InlineData(ActivityLevel.Medium, 60, 180)]
    [InlineData(ActivityLevel.Lively, 30, 90)]
    public void NextGap_InRange_ForEachLevel(ActivityLevel level, double min, double max)
    {
        var rng = new Random(3);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(LifeTiming.NextGap(level, rng).TotalSeconds, min, max);
    }

    [Fact]
    public void NextGap_RoutineFactor_StretchesAndClamps()
    {
        var rng = new Random(4);
        for (int i = 0; i < 500; i++)
        {
            Assert.InRange(LifeTiming.NextGap(ActivityLevel.Medium, rng, 1.5f).TotalSeconds, 90, 270);
            Assert.InRange(LifeTiming.NextGap(ActivityLevel.Medium, rng, 5f).TotalSeconds, 120, 360);
            Assert.InRange(LifeTiming.NextGap(ActivityLevel.Medium, rng, 0.2f).TotalSeconds, 60, 180);
        }
    }

    [Fact]
    public void Choose_Typing_Away_Hidden_Busy_None()
    {
        var rng = new Random(5);
        foreach (var c in new[] { Ctx(typing: true), Ctx(away: true), Ctx(hidden: true), Ctx(busy: true) })
            for (int i = 0; i < 50; i++) Assert.Equal(LifeAction.None, LifeChooser.Choose(c, _ => 1f, rng));
    }

    [Fact]
    public void Choose_Normal_PicksSomething()
    {
        var d = Draw(Ctx(), 500);
        Assert.Equal(0, d[LifeAction.None]);
        Assert.True(d[LifeAction.Moment] > d[LifeAction.PeekTaskbar]);
    }

    [Fact]
    public void Choose_NoPerchableWindow_NeverPerch() => Assert.Equal(0, Draw(Ctx(perch: false), 2000)[LifeAction.PerchWindow]);

    [Fact]
    public void Choose_LateNight_SleepsNeverCelebrates()
    {
        var d = Draw(Ctx(hour: 2), 2000);
        Assert.True(d[LifeAction.SleepInCorner] > 0);
        Assert.Equal(0, d[LifeAction.CelebrateLoop]);
        Assert.Equal(0, Draw(Ctx(hour: 14), 2000)[LifeAction.SleepInCorner]);
    }

    [Fact]
    public void Choose_Music_CelebratesMore()
    {
        float with = Draw(Ctx(music: true), 4000)[LifeAction.CelebrateLoop];
        float without = Draw(Ctx(), 4000)[LifeAction.CelebrateLoop];
        Assert.True(with > 1.8f * without, $"{with} vs {without}");
    }

    [Fact]
    public void Choose_LearnedWeight_ShiftsChoice()
    {
        float liked = Draw(Ctx(), 4000, a => a == LifeAction.Wander ? 3f : 1f)[LifeAction.Wander];
        float disliked = Draw(Ctx(), 4000, a => a == LifeAction.Wander ? 0.25f : 1f)[LifeAction.Wander];
        Assert.True(liked > 4 * disliked, $"{liked} vs {disliked}");
    }

    private static readonly LifeAction[] Living =
    [
        LifeAction.WalkTaskbar, LifeAction.Stretch, LifeAction.Yawn, LifeAction.ScratchHead, LifeAction.WatchYou,
        LifeAction.HopWindows, LifeAction.MoonToss, LifeAction.ChaseButterfly,
    ];

    [Fact]
    public void Choose_LivingMoves_AtLeastAsOftenAsFlying()
    {
        var d = Draw(Ctx(), 4000);
        int living = Living.Sum(a => d[a]);
        Assert.All(Living, a => Assert.True(d[a] > 0, $"{a} never chosen"));
        Assert.True(living >= d[LifeAction.Flight] + d[LifeAction.Wander], $"living {living} vs flying {d[LifeAction.Flight] + d[LifeAction.Wander]}");
    }

    [Fact]
    public void Choose_VideoInFront_NoLivingMoves() => Assert.Equal(0, Living.Sum(a => Draw(Ctx(video: true), 2000)[a]));

    [Fact]
    public void Choose_NoWindow_NoWatchingOrHopping()
    {
        var d = Draw(Ctx(perch: false), 3000);
        Assert.Equal(0, d[LifeAction.WatchYou]);
        Assert.Equal(0, d[LifeAction.HopWindows]);
    }

    [Theory]
    [InlineData(0.39, null, true)]       // 40% of typing sessions
    [InlineData(0.40, null, false)]
    [InlineData(0.10, 9.0, false)]       // at most once per 10 minutes
    [InlineData(0.10, 10.5, true)]
    public void TypeAlong_SometimesWhenYouStartTyping(double roll, double? minutesSinceLast, bool expected)
    {
        var now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset? last = minutesSinceLast is { } m ? now.AddMinutes(-m) : null;
        Assert.Equal(expected, TypeAlongPolicy.Should(now, last, roll));
    }

    [Fact]
    public void PlaysAloneOff_NeverPicksPlayMoves()
    {
        var c = Ctx() with { PlaysAlone = false };
        var counts = Draw(c, 10_000);
        foreach (var a in new[] { LifeAction.PlayWithMoon, LifeAction.HopWindows, LifeAction.MoonToss, LifeAction.ChaseButterfly })
            Assert.Equal(0, counts[a]);
        var on = Draw(Ctx(), 10_000);
        Assert.True(on[LifeAction.MoonToss] > 0 && on[LifeAction.ChaseButterfly] > 0);
        Assert.True(new LifeContext(false, false, false, false, false, false, 14, true).PlaysAlone);
    }
}
