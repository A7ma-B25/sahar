using Sahar.Core.Geometry;
using Sahar.Engine.Host;

namespace Sahar.Engine.Senses;

/// <summary>The window in front: where it is, where its text caret is (if it has a system caret), and whether she may sit on it.</summary>
internal static unsafe class ForegroundInfo
{
    public static (nint Hwnd, RectI Window, RectI? Caret, bool Perchable) Current()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0) return (0, default, null, false);
        RECT r = default;
        Native.GetWindowRect(fg, &r);
        return (fg, Monitors.ToRect(r), Caret(fg), IsPerchable(fg));
    }

    /// <summary>The caret in screen pixels, or null when the app draws its own (browsers, Electron).</summary>
    public static RectI? Caret(nint fg)
    {
        uint thread = Native.GetWindowThreadProcessId(fg, null);
        var info = new GUITHREADINFO { CbSize = (uint)sizeof(GUITHREADINFO) };
        if (thread == 0 || Native.GetGUIThreadInfo(thread, &info) == 0 || info.HwndCaret == 0) return null;
        var a = new POINT { X = info.RcCaret.Left, Y = info.RcCaret.Top };
        var b = new POINT { X = info.RcCaret.Right, Y = info.RcCaret.Bottom };
        Native.ClientToScreen(info.HwndCaret, &a);
        Native.ClientToScreen(info.HwndCaret, &b);
        return new RectI(a.X, a.Y, Math.Max(b.X, a.X + 1), Math.Max(b.Y, a.Y + 1));
    }

    /// <summary>A normal, visible, restored window that is not hers and not the shell.</summary>
    public static bool IsPerchable(nint fg)
    {
        if (fg == 0 || fg == Native.GetShellWindow() || Win.Own.Contains(fg)) return false;
        if (Native.IsWindowVisible(fg) == 0 || Native.IsIconic(fg) != 0 || Native.IsZoomed(fg) != 0) return false;
        return Native.ClassName(fg) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
}
