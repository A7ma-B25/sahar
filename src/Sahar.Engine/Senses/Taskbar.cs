using Sahar.Core.Geometry;
using Sahar.Core.Motion;

namespace Sahar.Engine.Senses;

/// <summary>Where the taskbar is on a monitor: its rect, which edge it sits on, and whether it auto-hides.</summary>
internal static unsafe class Taskbar
{
    private const uint ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5, ABS_AUTOHIDE = 1;

    public static (RectF Rect, Edge Edge, bool AutoHide) Get(nint monitor)
    {
        var m = Monitors.Describe(monitor);
        var abd = new APPBARDATA { CbSize = (uint)sizeof(APPBARDATA) };
        bool autoHide = (Native.SHAppBarMessage(ABM_GETSTATE, &abd) & ABS_AUTOHIDE) != 0;

        // The primary taskbar.
        abd = new APPBARDATA { CbSize = (uint)sizeof(APPBARDATA) };
        if (Native.SHAppBarMessage(ABM_GETTASKBARPOS, &abd) != 0 && Native.MonitorFromRect(&abd.Rc, Native.MONITOR_DEFAULTTONEAREST) == monitor)
            return Result(Monitors.ToRect(abd.Rc), m, autoHide);

        // Taskbars on other monitors.
        nint w = 0;
        fixed (char* cls = "Shell_SecondaryTrayWnd")
            while ((w = Native.FindWindowEx(0, w, cls, null)) != 0)
            {
                RECT r;
                if (Native.GetWindowRect(w, &r) != 0 && Native.MonitorFromRect(&r, Native.MONITOR_DEFAULTTONEAREST) == monitor)
                    return Result(Monitors.ToRect(r), m, autoHide);
            }

        // None found: treat the bottom of the monitor as a hidden taskbar.
        return (new RectF(m.Bounds.Left, m.Bounds.Bottom, m.Bounds.Right - m.Bounds.Left, 0), Edge.Bottom, true);
    }

    private static (RectF, Edge, bool) Result(RectI r, MonitorInfo m, bool autoHide)
    {
        var b = m.Bounds;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Edge edge = w >= h
            ? (r.Top <= b.Top ? Edge.Top : Edge.Bottom)
            : (r.Left <= b.Left ? Edge.Left : Edge.Right);
        // An auto-hidden bar is mostly off-screen: sit on the monitor edge instead.
        if (autoHide)
        {
            return edge switch
            {
                Edge.Top => (new RectF(b.Left, b.Top, b.Right - b.Left, 0), edge, true),
                Edge.Left => (new RectF(b.Left, b.Top, 0, b.Bottom - b.Top), edge, true),
                Edge.Right => (new RectF(b.Right, b.Top, 0, b.Bottom - b.Top), edge, true),
                _ => (new RectF(b.Left, b.Bottom, b.Right - b.Left, 0), edge, true),
            };
        }
        return (new RectF(r.Left, r.Top, w, h), edge, false);
    }
}
