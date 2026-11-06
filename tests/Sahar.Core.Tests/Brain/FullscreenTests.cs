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
    public void Fullscreen_Desktop_IsNotFullscreen(string cls) =>
        Assert.False(Fullscreen.IsFullscreen(new RectI(0, 0, 1920, 1080), Monitor, cls, hasCaption: false));
}
