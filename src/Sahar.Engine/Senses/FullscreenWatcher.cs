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
    private const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
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
               $"monitor {m.Bounds} caption {caption} maximized {Native.IsZoomed(fg) != 0} own {Win.Own.Contains(fg)} quns {state} => {CheckMonitor() != 0}";
    }

    /// <summary>Why she hides, for the log: Windows' own fullscreen state and what the window looks like (its class,
    /// never its title).</summary>
    public static string Reason()
    {
        int state = -1;
        Native.SHQueryUserNotificationState(&state);
        nint fg = Native.GetForegroundWindow();
        RECT r = default;
        Native.GetWindowRect(fg, &r);
        var m = Monitors.Describe(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST));
        long style = Native.GetWindowLongPtr(fg, GWL_STYLE);
        var w = Monitors.ToRect(r);
        bool covers = w.Left <= m.Bounds.Left && w.Top <= m.Bounds.Top && w.Right >= m.Bounds.Right && w.Bottom >= m.Bounds.Bottom;
        return $"class '{Native.ClassName(fg)}' quns {state} caption {(style & WS_CAPTION) == WS_CAPTION} " +
               $"maximized {Native.IsZoomed(fg) != 0} sizing-frame {(style & WS_THICKFRAME) != 0} covers-monitor {covers}";
    }

    /// <summary>The monitor (handle) the fullscreen app is on, or 0 when nothing is fullscreen. Windows' own D3D-fullscreen
    /// and presentation states count on the foreground window's monitor (the primary one when that is ours).</summary>
    public static nint CheckMonitor()
    {
        nint fg = Native.GetForegroundWindow();
        int state = 0;
        if (Native.SHQueryUserNotificationState(&state) == 0 && state is QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
            return fg == 0 || Win.Own.Contains(fg) ? Monitors.Primary.Handle : Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);

        if (fg == 0 || fg == Native.GetShellWindow() || Win.Own.Contains(fg)) return 0;
        RECT r;
        if (Native.GetWindowRect(fg, &r) == 0) return 0;
        nint m = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
        var monitor = Monitors.Describe(m);
        long style = Native.GetWindowLongPtr(fg, GWL_STYLE);
        return Fullscreen.IsFullscreen(Monitors.ToRect(r), monitor.Bounds, Native.ClassName(fg), (style & WS_CAPTION) == WS_CAPTION,
            Native.IsZoomed(fg) != 0, (style & WS_THICKFRAME) != 0) ? m : 0;
    }
}
