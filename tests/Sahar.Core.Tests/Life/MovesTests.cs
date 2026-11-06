using Sahar.Core.Expressions;
using Sahar.Core.Life;
using Sahar.Core.Motion;

namespace Sahar.Core.Tests.Life;

public class ShyDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    private static bool Approach(ShyDetector d, double atSeconds) =>
        d.Update(T0.AddSeconds(atSeconds), 200, false) | d.Update(T0.AddSeconds(atSeconds + 0.3), 40, false);

    [Fact]
    public void ShyDetector_ThreeQuickApproaches_Shy()
    {
        var d = new ShyDetector();
        Assert.False(Approach(d, 0));
        Assert.False(Approach(d, 2));
        Assert.True(Approach(d, 4));
    }

    [Fact]
    public void ShyDetector_AClickResets()
    {
        var d = new ShyDetector();
        Approach(d, 0);
        Approach(d, 2);
        d.Update(T0.AddSeconds(3), 30, clicked: true);
        Assert.False(Approach(d, 4));
    }

    [Fact]
    public void ShyDetector_SlowApproaches_NotShy()
    {
        var d = new ShyDetector();
        Assert.False(Approach(d, 0));
        Assert.False(Approach(d, 6));
        Assert.False(Approach(d, 12));
    }

    [Fact]
    public void ShyDetector_StayingClose_IsOneApproach()
    {
        var d = new ShyDetector();
        for (int i = 0; i < 20; i++) Assert.False(d.Update(T0.AddSeconds(i * 0.3), 30, false));
    }
}

public class AttentionPacerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AttentionPacer_StartThenEvery30s_MaxThree()
    {
        var p = new AttentionPacer();
        Assert.True(p.ShouldPlay("claude-permission", T0, false));
        Assert.False(p.ShouldPlay("claude-permission", T0.AddSeconds(10), false));
        Assert.True(p.ShouldPlay("claude-permission", T0.AddSeconds(30), false));
        Assert.True(p.ShouldPlay("claude-permission", T0.AddSeconds(60), false));
        Assert.False(p.ShouldPlay("claude-permission", T0.AddSeconds(90), false));
        Assert.False(p.ShouldPlay("claude-permission", T0.AddSeconds(300), false));
    }

    [Fact]
    public void AttentionPacer_NeverWithDnd() => Assert.False(new AttentionPacer().ShouldPlay("timer-done", T0, true));

    [Fact]
    public void AttentionPacer_OnlyImportantFaces()
    {
        var p = new AttentionPacer();
        Assert.False(p.ShouldPlay("music", T0, false));
        Assert.True(p.ShouldPlay("reminder", T0, false));
    }

    [Fact]
    public void AttentionPacer_NewKeyStartsOver()
    {
        var p = new AttentionPacer();
        p.ShouldPlay("timer-done", T0, false);
        p.ShouldPlay("idle", T0.AddSeconds(5), false);
        Assert.True(p.ShouldPlay("timer-done", T0.AddSeconds(6), false));
    }
}

public class ShowcaseTests
{
    [Fact]
    public void Showcase_CoversEveryMotionButDrag_EveryFlight_EveryExpression()
    {
        var steps = Showcase.Steps;
        var motions = steps.Where(s => s.Kind == ShowcaseKind.Motion).Select(s => s.Id).ToHashSet();
        foreach (var k in Enum.GetValues<MotionKind>())
            if (k != MotionKind.Drag) Assert.Contains(k.ToString(), motions);
        Assert.DoesNotContain(MotionKind.Drag.ToString(), motions);
        var flights = steps.Where(s => s.Kind == ShowcaseKind.Flight).Select(s => s.Id).ToHashSet();
        foreach (var f in Enum.GetValues<FlightPattern>()) Assert.Contains(f.ToString(), flights);
        var faces = steps.Where(s => s.Kind == ShowcaseKind.Expression).Select(s => s.Id).ToList();
        Assert.Equal(ExpressionCatalog.All.Where(e => e.Key != "idle").Select(e => e.Key), faces);
        Assert.All(steps, s => Assert.InRange(s.Seconds, 1.5f, 14f));
    }

    [Fact]
    public void Showcase_CaptionsAreArabicWithNumbers()
    {
        var follow = Showcase.Steps.First(s => s.Id == nameof(MotionKind.FollowCursor));
        Assert.Equal("13 · بتلحق الماوس", follow.Caption);
        var timer = Showcase.Steps.First(s => s.Id == "timer-done");
        Assert.Equal("تعبير 5 · المؤقت خلص", timer.Caption);
        Assert.All(Showcase.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.Caption)));
    }

    [Fact]
    public void Showcase_PlayPicks_AreOnlyStandaloneMoves()
    {
        MotionKind[] never = [MotionKind.Drag, MotionKind.Throw, MotionKind.FullscreenHide, MotionKind.Wake, MotionKind.Goodbye,
            MotionKind.StartupEntry, MotionKind.Hover, MotionKind.EyesFollow, MotionKind.SleepInCorner, MotionKind.NapOnIcon, MotionKind.Attention];
        Assert.NotEmpty(Showcase.PlayPicks);
        Assert.All(Showcase.PlayPicks, k => Assert.DoesNotContain(k, never));
        Assert.Contains(MotionKind.HopIcons, Showcase.PlayPicks);
        Assert.Contains(MotionKind.FollowCursor, Showcase.PlayPicks);
    }
}

public class NewMovesChooserTests
{
    private static LifeContext Ctx(bool video = false, bool perch = true) => new(false, video, false, false, false, false, 14, perch);

    private static Dictionary<LifeAction, int> Draw(LifeContext c, int n)
    {
        var rng = new Random(7);
        var d = new Dictionary<LifeAction, int>();
        for (int i = 0; i < n; i++) { var a = LifeChooser.Choose(c, _ => 1f, rng); d[a] = d.GetValueOrDefault(a) + 1; }
        return d;
    }

    [Fact]
    public void Choose_NewMoves_HaveWeight()
    {
        var d = Draw(Ctx(), 4000);
        Assert.True(d.GetValueOrDefault(LifeAction.FollowCursor) > 0);
        Assert.True(d.GetValueOrDefault(LifeAction.HopIcons) > 0);
        Assert.True(d.GetValueOrDefault(LifeAction.HideBehindWindow) > 0);
        Assert.Equal(0, Draw(Ctx(perch: false), 4000).GetValueOrDefault(LifeAction.HideBehindWindow));
    }

    [Fact]
    public void Choose_Video_StillOnlySmallThings()
    {
        var d = Draw(Ctx(video: true), 4000);
        Assert.All(d.Keys, a => Assert.Contains(a, new[] { LifeAction.Moment, LifeAction.Breathe }));
    }
}
