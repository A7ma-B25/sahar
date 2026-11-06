using Sahar.Core.Geometry;

namespace Sahar.Core.Brain;

public static class Fullscreen
{
    private static readonly string[] ShellClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    /// <summary>A borderless, non-shell window that covers the whole monitor.</summary>
    public static bool IsFullscreen(RectI window, RectI monitor, string windowClass, bool hasCaption) =>
        !hasCaption
        && Array.IndexOf(ShellClasses, windowClass) < 0
        && window.Left <= monitor.Left && window.Top <= monitor.Top
        && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
}
