using Sahar.Core.Brain;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Brain;

public class FullscreenTests
{
    private static readonly RectI Monitor = new(0, 0, 1920, 1080);

    [Fact]
    public void Fullscreen_MaximizedWindow_IsNotFullscreen() =>
        Assert.False(Fullscreen.IsFullscreen(new RectI(0, 0, 1920, 1032), Monitor, "Chrome_WidgetWin_1", hasCaption: true));

    [Fact]
    public void Fullscreen_CaptionedWindowCoveringMonitor_IsNotFullscreen() =>
        Assert.False(Fullscreen.IsFullscreen(new RectI(-8, -8, 1928, 1088), Monitor, "Notepad", hasCaption: true));

    [Fact]
    public void Fullscreen_BorderlessCoveringMonitor_IsFullscreen() =>
        Assert.True(Fullscreen.IsFullscreen(new RectI(0, 0, 1920, 1080), Monitor, "UnityWndClass", hasCaption: false));

    [Fact]
    public void Fullscreen_BorderlessLargerThanMonitor_IsFullscreen() =>
        Assert.True(Fullscreen.IsFullscreen(new RectI(-1, -1, 1921, 1081), Monitor, "SDL_app", hasCaption: false));

    [Theory]
    [InlineData("WorkerW")]
    [InlineData("Progman")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("XamlExplorerHostIslandWindow")]      // Win11 Task View / Alt+Tab
    [InlineData("MultitaskingViewFrame")]             // Win10 Task View
    [InlineData("Windows.UI.Core.CoreWindow")]        // Start, search, action center
    [InlineData("ForegroundStaging")]
    [InlineData("TaskSwitcherWnd")]
    public void Fullscreen_Desktop_IsNotFullscreen(string cls) =>
        Assert.False(Fullscreen.IsFullscreen(new RectI(0, 0, 1920, 1080), Monitor, cls, hasCaption: false));
}

public class FullscreenDebounceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Debounce_HidesOnlyAfterSustainedFullscreen()
    {
        var d = new FullscreenDebounce();
        Assert.False(d.Update(true, T0));
        Assert.False(d.Update(true, T0.AddMilliseconds(500)));
        Assert.True(d.Update(true, T0.AddMilliseconds(750)));
    }

    [Fact]
    public void Debounce_BriefFlash_NeverHides()
    {
        var d = new FullscreenDebounce();
        Assert.False(d.Update(true, T0));
        Assert.False(d.Update(false, T0.AddMilliseconds(400)));
        Assert.False(d.Update(true, T0.AddMilliseconds(600)));
        Assert.False(d.Update(true, T0.AddMilliseconds(1200)));
        Assert.True(d.Update(true, T0.AddMilliseconds(1350)));
    }

    [Fact]
    public void Debounce_ShowsAgainAtOnce()
    {
        var d = new FullscreenDebounce();
        d.Update(true, T0);
        Assert.True(d.Update(true, T0.AddSeconds(1)));
        Assert.False(d.Update(false, T0.AddSeconds(1.1)));
    }
}
