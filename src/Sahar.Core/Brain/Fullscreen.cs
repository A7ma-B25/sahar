using Sahar.Core.Geometry;

namespace Sahar.Core.Brain;

public static class Fullscreen
{
    private static readonly string[] ShellClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow",   // Windows 11 Task View and Alt+Tab
        "MultitaskingViewFrame",          // Windows 10 Task View
        "Windows.UI.Core.CoreWindow",     // Start, search, action centre
        "ForegroundStaging", "TaskSwitcherWnd",
    ];

    /// <summary>A borderless, non-shell window that covers the whole monitor.</summary>
    /// <param name="maximized">A maximized window is never fullscreen, even one with no caption covering the
    /// monitor (a browser with the taskbar auto-hidden).</param>
    public static bool IsFullscreen(RectI window, RectI monitor, string windowClass, bool hasCaption, bool maximized = false) =>
        !hasCaption && !maximized
        && Array.IndexOf(ShellClasses, windowClass) < 0
        && window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}

/// <summary>Fullscreen must last <see cref="Delay"/> before she hides (shell flashes never hide her); she shows again at once.</summary>
public sealed class FullscreenDebounce
{
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(750);
    private DateTimeOffset? _since;

    public bool Update(bool fullscreen, DateTimeOffset now)
    {
        if (!fullscreen) { _since = null; return false; }
        _since ??= now;
        return now - _since.Value >= Delay;
    }
}
