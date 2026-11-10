using Sahar.Core.Brain;

namespace Sahar.Core.Tests.Brain;

public class FullscreenScopeTests
{
    [Fact]
    public void Where_NoFullscreen_None() => Assert.Equal(FullscreenWhere.None, FullscreenScope.Where(FullscreenScope.NoMonitor, 0));

    [Fact]
    public void Where_SameMonitor_Hers() => Assert.Equal(FullscreenWhere.Hers, FullscreenScope.Where(1, 1));

    [Fact]
    public void Where_OtherMonitor_Elsewhere() => Assert.Equal(FullscreenWhere.Elsewhere, FullscreenScope.Where(1, 0));

    [Fact]
    public void Where_UnknownMonitor_Hers() =>
        // A fullscreen window on a monitor her list does not know yet (just plugged in): when in doubt she hides, as before.
        Assert.Equal(FullscreenWhere.Hers, FullscreenScope.Where(FullscreenScope.UnknownMonitor, 0));

    [Fact]
    public void Where_HerMonitorUnknown_Hers() => Assert.Equal(FullscreenWhere.Hers, FullscreenScope.Where(0, -1));
}
