using Sahar.Core.Diagnostics;

namespace Sahar.Core.Tests.Diagnostics;

public class LogRulesTests
{
    [Theory]
    [InlineData("2026-11-07 21:29:49.653 exit 0", true)]
    [InlineData("2026-11-07 21:29:49.653 exit 3", true)]
    [InlineData("2026-11-08 12:52:15.155 life: waiting: next turn at 12:57:31", false)]
    [InlineData("2026-11-10 18:21:28.145 show: start at 0", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void EndedCleanly(string? last, bool clean) => Assert.Equal(clean, LogRules.EndedCleanly(last));

    [Fact]
    public void Timestamp_IsTheLinesTimeOnly()
    {
        Assert.Equal("2026-11-08 12:52:15.155", LogRules.Timestamp("2026-11-08 12:52:15.155 life: Stretch"));
        Assert.Null(LogRules.Timestamp("garbage"));
    }

    [Theory]
    [InlineData("next turn at 18:21:43", "next turn")]
    [InlineData("next turn at 09:00:00", "next turn")]
    [InlineData("busy (Wake)", "busy (Wake)")]
    [InlineData("you are away", "you are away")]
    [InlineData("you are typing", "you are typing")]
    public void WaitKind(string reason, string kind) => Assert.Equal(kind, LogRules.WaitKind(reason));
}
