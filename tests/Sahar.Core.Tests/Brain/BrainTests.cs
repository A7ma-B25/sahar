using Sahar.Core.Brain;
using Sahar.Core.Expressions;
using B = Sahar.Core.Brain.Brain;

namespace Sahar.Core.Tests.Brain;

public class BrainTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);

    [Fact]
    public void Nothing_Active_IsIdle() => Assert.Equal("idle", new B().Tick(T0).ExpressionKey);

    [Fact]
    public void HigherPriority_Preempts()
    {
        var b = new B();
        b.Post(EventKind.Music, At(0));
        Assert.Equal("music", b.Tick(At(0.1)).ExpressionKey);
        b.Post(EventKind.ClaudePermission, At(1));
        Assert.Equal("claude-permission", b.Tick(At(1.1)).ExpressionKey);
    }

    [Fact]
    public void LowerPriority_Waits_ThenShows()
    {
        var b = new B();
        b.Post(EventKind.ClaudePermission, At(0));
        b.Post(EventKind.DownloadDone, At(1));
        Assert.Equal("claude-permission", b.Tick(At(2)).ExpressionKey);
        Assert.Equal("download", b.Tick(At(8.5)).ExpressionKey);
    }

    [Fact]
    public void Expired_Event_Dropped()
    {
        var b = new B();
        b.Post(EventKind.ClaudePermission, At(0), TimeSpan.FromSeconds(20));
        b.Post(EventKind.DownloadDone, At(1));
        Assert.Equal("idle", b.Tick(At(21)).ExpressionKey);
    }

    [Fact]
    public void Sticky_Until_EndEvent()
    {
        var b = new B();
        b.Post(EventKind.ClaudeWorking, At(0));
        Assert.Equal("claude-working", b.Tick(At(600)).ExpressionKey);
        b.Post(EventKind.ClaudeDone, At(601));
        Assert.Equal("claude-done", b.Tick(At(601.5)).ExpressionKey);
        Assert.Equal("idle", b.Tick(At(615)).ExpressionKey);
    }

    [Fact]
    public void EqualPriority_Waits()
    {
        var b = new B();
        b.Post(EventKind.Screenshot, At(0));
        b.Post(EventKind.ClipboardCopied, At(0.2));
        Assert.Equal("screenshot", b.Tick(At(0.3)).ExpressionKey);
        b.Post(EventKind.Screenshot, At(9), TimeSpan.FromSeconds(1));
        b.Post(EventKind.ClipboardCopied, At(9.1));
        Assert.Equal("screenshot", b.Tick(At(9.5)).ExpressionKey);
        Assert.Equal("copied", b.Tick(At(10.2)).ExpressionKey);
    }

    [Fact]
    public void Music_Returns_After_Permission()
    {
        var b = new B();
        b.Post(EventKind.Music, At(0));
        b.Post(EventKind.ClaudePermission, At(1));
        Assert.Equal("music", b.Tick(At(9.5)).ExpressionKey);
        b.Post(EventKind.MusicStop, At(10));
        Assert.Equal("idle", b.Tick(At(10.1)).ExpressionKey);
    }

    [Fact]
    public void Fullscreen_Hides()
    {
        var b = new B();
        b.SetQuiet(QuietReason.Fullscreen);
        var o = b.Tick(T0);
        Assert.True(o.Hidden);
        Assert.False(o.SoundAllowed);
    }

    [Fact]
    public void Quran_Silences_But_Shows()
    {
        var b = new B();
        b.SetQuiet(QuietReason.Quran);
        var o = b.Tick(T0);
        Assert.Equal((false, false, false), (o.Hidden, o.SoundAllowed, o.CommentsAllowed));
    }

    [Fact]
    public void NoQuiet_AllowsSoundAndComments()
    {
        var o = new B().Tick(T0);
        Assert.Equal((false, true, true), (o.Hidden, o.SoundAllowed, o.CommentsAllowed));
    }

    [Fact]
    public void ExpressionKeyFor_EveryEvent_IsInCatalog()
    {
        foreach (var k in Enum.GetValues<EventKind>().Where(k => !EventNames.IsEndEvent(k)))
            Assert.Equal(B.ExpressionKeyFor(k), ExpressionCatalog.Get(B.ExpressionKeyFor(k)).Key);
    }

    [Fact]
    public void EventNames_RoundTrip_And_Unknown_Throws()
    {
        foreach (var k in Enum.GetValues<EventKind>()) Assert.Equal(k, EventNames.Parse(EventNames.ToWire(k)));
        Assert.Equal(EventKind.ClaudePermission, EventNames.Parse("claude.permission"));
        Assert.Throws<FormatException>(() => EventNames.Parse("nope"));
    }

    [Fact]
    public void Priorities_MatchPlan()
    {
        Assert.Equal(100, EventPriorities.Of(EventKind.ClaudePermission));
        Assert.Equal(80, EventPriorities.Of(EventKind.BatteryLow));
        Assert.Equal(60, EventPriorities.Of(EventKind.DownloadDone));
        Assert.Equal(40, EventPriorities.Of(EventKind.Music));
        Assert.Equal(20, EventPriorities.Of(EventKind.UserAway));
    }
}
