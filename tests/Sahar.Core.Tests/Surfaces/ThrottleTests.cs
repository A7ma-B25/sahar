using Sahar.Core.Surfaces;
using Xunit;

namespace Sahar.Core.Tests.Surfaces;

/// <summary>Review I8: her streamed words reach the chat at most ~10 times a second; the last one always arrives.</summary>
public class ThrottleTests
{
    [Fact]
    public void AtMostOneSendPer100ms_TheRestWaitForTheTrailingOne()
    {
        var t0 = new DateTimeOffset(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
        var th = new Throttle(TimeSpan.FromMilliseconds(100));
        Assert.True(th.Take(t0));
        Assert.False(th.Take(t0.AddMilliseconds(30)));
        Assert.Equal(TimeSpan.FromMilliseconds(70), th.Wait(t0.AddMilliseconds(30)));
        Assert.True(th.Take(t0.AddMilliseconds(100)));
        Assert.False(th.Take(t0.AddMilliseconds(150)));
        th.Reset();
        Assert.True(th.Take(t0.AddMilliseconds(151)));
    }
}
