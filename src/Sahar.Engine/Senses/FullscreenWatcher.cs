using Sahar.Core.Brain;
using Sahar.Engine.Host;

namespace Sahar.Engine.Senses;

/// <summary>
/// Is something fullscreen in front (a video, a game, a presentation)? The foreground window covering its
/// whole monitor without a caption, or Windows reporting a D3D fullscreen app or presentation mode.
/// A maximized window is not fullscreen; our own windows and the desktop never count.
/// </summary>
internal static unsafe class FullscreenWatcher
{
    private const int GWL_STYLE = -16;
    private const long WS_CAPTION = 0x00C00000;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    /// <summary>What the check sees, for test diagnostics.</summary>
    public static string Describe()
    {
        int state = -1;
        Native.SHQueryUserNotificationState(&state);
        nint fg = Native.GetForegroundWindow();
        RECT r = default;
        Native.GetWindowRect(fg, &r);
        var m = Monitors.Describe(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST));
        bool caption = (Native.GetWindowLongPtr(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        return $"fg 0x{fg:X} class '{Native.ClassName(fg)}' rect {r.Left},{r.Top},{r.Right},{r.Bottom} " +
               $"monitor {m.Bounds} caption {caption} own {Win.Own.Contains(fg)} quns {state} => {Check()}";
    }

    public static bool Check()
    {
        int state = 0;
        if (Native.SHQueryUserNotificationState(&state) == 0 && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            return true;

        nint fg = Native.GetForegroundWindow();
        if (fg == 0 || fg == Native.GetShellWindow() || Win.Own.Contains(fg)) return false;
        RECT r;
        if (Native.GetWindowRect(fg, &r) == 0) return false;
        var monitor = Monitors.Describe(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST));
        bool hasCaption = (Native.GetWindowLongPtr(fg, GWL_STYLE) & WS_CAPTION) == WS_CAPTION;
        return Fullscreen.IsFullscreen(Monitors.ToRect(r), monitor.Bounds, Native.ClassName(fg), hasCaption);
    }
}
