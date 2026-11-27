using Sahar.Core.Surfaces;
using Xunit;

namespace Sahar.Core.Tests.Surfaces;

/// <summary>Review C2 (0.16): what her end does when the surface host is slow, gone, or never comes.</summary>
public class HostLinkTests
{
    private DateTimeOffset _now = new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero);
    private HostLink Link() => new(() => _now);

    [Fact]
    public void ColdStart_ShowsWaitForTheHost_NoEightSecondCliff()
    {
        var l = Link();
        Assert.Equal(HostAction.QueueAndStart, l.Show());
        l.Starting();
        _now = _now.AddSeconds(30);   // a cold WebView2 on a busy PC, below normal priority
        Assert.Equal(HostAction.Queue, l.Show());
        Assert.Equal(HostAction.SendQueued, l.Connected());
        Assert.Equal(HostAction.Send, l.Show());
    }

    [Fact]
    public void SurfaceClient_HostGone_FailsFast()
    {
        var l = Link();
        l.Show(); l.Starting(); l.Connected();
        Assert.Equal(HostAction.Respawn, l.Lost());   // it died once: started again and warmed
        Assert.Equal(HostState.Starting, l.State);
        Assert.Equal(HostAction.Queue, l.Show());
        l.Connected();
        Assert.Equal(HostAction.FailQueued, l.Lost());   // twice: no more respawns
        Assert.Equal(HostState.Failed, l.State);
        Assert.Equal(HostAction.Fail, l.Show());          // at once, not after seconds
        Assert.False(l.Up);
    }

    [Fact]
    public void HostExitsBeforeConnecting_FailsTheQueueAtOnce()
    {
        var l = Link();
        l.Show(); l.Starting();
        Assert.Equal(HostAction.FailQueued, l.StartFailed());   // no WebView2: exit 2
        Assert.Equal(HostAction.Fail, l.Show());
    }

    [Fact]
    public void NeverConnects_FailsAfterTheDeadline()
    {
        var l = Link();
        l.Show(); l.Starting();
        _now = _now.AddSeconds(HostLink.ConnectDeadline.TotalSeconds - 1);
        Assert.False(l.Overdue);
        _now = _now.AddSeconds(2);
        Assert.True(l.Overdue);
        Assert.Equal(HostAction.FailQueued, l.StartFailed());
    }

    [Fact]
    public void AfterAFailure_ALaterShowTriesAgain()
    {
        var l = Link();
        l.Show(); l.Starting(); l.StartFailed();
        _now = _now.AddMinutes(1);
        Assert.Equal(HostAction.Fail, l.Show());
        _now = _now.AddMinutes(5);
        Assert.Equal(HostAction.QueueAndStart, l.Show());
    }

    [Fact]
    public void PipeName_IsPerSession()
    {
        Assert.Equal("sahar-surfaces-1", SurfacePipeName.For(1));
        Assert.NotEqual(SurfacePipeName.For(1), SurfacePipeName.For(2));
    }
}
