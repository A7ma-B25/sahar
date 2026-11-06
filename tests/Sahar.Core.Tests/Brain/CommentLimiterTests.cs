using Sahar.Core.Brain;

namespace Sahar.Core.Tests.Brain;

public class CommentLimiterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommentLimiter_45MinGap()
    {
        var l = new CommentLimiter();
        Assert.True(l.TryComment(T0));
        Assert.False(l.TryComment(T0.AddMinutes(44)));
        Assert.True(l.TryComment(T0.AddMinutes(45)));
    }

    [Fact]
    public void CommentLimiter_BackoffDoubles_CapsAt4h()
    {
        var l = new CommentLimiter();
        Assert.True(l.TryComment(T0));
        l.Dismissed(T0);                                        // gap 90 min
        Assert.False(l.TryComment(T0.AddMinutes(89)));
        Assert.True(l.TryComment(T0.AddMinutes(90)));
        var t = T0.AddMinutes(90);
        l.Dismissed(t); l.Dismissed(t); l.Dismissed(t);         // 180, 360→cap 240, 240
        Assert.Equal(TimeSpan.FromHours(4), l.Gap);
        Assert.False(l.TryComment(t.AddMinutes(239)));
        Assert.True(l.TryComment(t.AddMinutes(240)));
    }

    [Fact]
    public void CommentLimiter_AcceptResets()
    {
        var l = new CommentLimiter();
        l.Dismissed(T0); l.Dismissed(T0);
        l.Accepted();
        Assert.Equal(TimeSpan.FromMinutes(45), l.Gap);
    }
}
