using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Engine.Senses;

/// <summary>One monitor: its handle, full and work rects (screen pixels) and DPI scale (1.0 = 96 DPI).</summary>
internal readonly record struct MonitorInfo(nint Handle, RectI Bounds, RectI Work, float DpiScale)
{
    public RectF WorkF => new(Work.Left, Work.Top, Work.Right - Work.Left, Work.Bottom - Work.Top);
}

internal static unsafe class Monitors
{
    /// <summary>The monitor nearest to a screen point.</summary>
    public static MonitorInfo At(Vector2 screen)
    {
        var pt = new POINT { X = (int)screen.X, Y = (int)screen.Y };
        nint m = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        return Describe(m);
    }

    public static MonitorInfo Primary => Describe(Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTOPRIMARY));

    public static MonitorInfo Describe(nint m)
    {
        var info = new MONITORINFO { CbSize = (uint)sizeof(MONITORINFO) };
        Native.GetMonitorInfo(m, &info);
        uint dx = 96, dy = 96;
        if (Native.GetDpiForMonitor(m, 0 /* effective */, &dx, &dy) != 0) dx = 96;
        return new MonitorInfo(m, ToRect(info.Monitor), ToRect(info.Work), dx / 96f);
    }

    public static RectI ToRect(RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
