using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Sahar.Settings.Surfaces;

/// <summary>
/// One surface window: a borderless popup with no redirection bitmap, DirectComposition on it, and a WebView2
/// CompositionController drawing into it (transparent background). Mouse input is forwarded to WebView2 (visual hosting
/// does not take it by itself); keyboard and IME go through WebView2's own focus. Created hidden and kept warm.
/// </summary>
internal sealed class VisualWindow
{
    public const string ClassName = "SaharSurface";
    private static SN.WndProc? _proc;
    private static readonly Dictionary<nint, VisualWindow> Windows = [];

    private readonly DComp.IDCompositionDevice _device;
    private readonly DComp.IDCompositionTarget _target;
    private readonly DComp.IDCompositionVisual _visual;
    private CoreWebView2CompositionController? _ctl;
    private bool _tracking;

    public string Name { get; }
    public nint Hwnd { get; }
    public CoreWebView2? View => _ctl?.CoreWebView2;
    public CoreWebView2CompositionController? Controller => _ctl;
    public event Action<bool>? Activated;
    public event Action? CloseAsked;

    public static unsafe void Register()
    {
        _proc = Proc;
        nint instance = SN.GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new SN.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(SN.WNDCLASSEXW),
                style = SN.CS_DBLCLKS,
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = instance,
                hCursor = SN.LoadCursorW(0, 32512),
                hIcon = Native.LoadIconW(instance, 32512),
                lpszClassName = cls,
            };
            SN.RegisterClassExW(&wc);
        }
    }

    public unsafe VisualWindow(string name, string title, bool tool, bool topmost)
    {
        Name = name;
        uint ex = SN.WS_EX_NOREDIRECTIONBITMAP | (tool ? SN.WS_EX_TOOLWINDOW : SN.WS_EX_APPWINDOW) | (topmost ? SN.WS_EX_TOPMOST : 0);
        Hwnd = SN.CreateWindowExW(ex, ClassName, title, SN.WS_POPUP, 0, 0, 10, 10, 0, 0, SN.GetModuleHandleW(null), 0);
        if (Hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
        Windows[Hwnd] = this;
        int round = SN.DWMWCP_DONOTROUND;   // the page draws its own corners
        SN.DwmSetWindowAttribute(Hwnd, SN.DWMWA_WINDOW_CORNER_PREFERENCE, &round, sizeof(int));
        _device = DComp.CreateDevice();
        _target = _device.CreateTargetForHwnd(Hwnd, true);
        _visual = _device.CreateVisual();
        _target.SetRoot(_visual);
        _device.Commit();
    }

    public async Task StartAsync(CoreWebView2Environment env, float scale)
    {
        _ctl = await env.CreateCoreWebView2CompositionControllerAsync(Hwnd);
        _ctl.RootVisualTarget = _visual;
        _ctl.DefaultBackgroundColor = System.Drawing.Color.Transparent;
        _ctl.ShouldDetectMonitorScaleChanges = false;
        _ctl.RasterizationScale = scale;
        _ctl.BoundsMode = CoreWebView2BoundsMode.UseRawPixels;
        _ctl.CursorChanged += (_, _) => { if (Hovered) SN.SetCursor(_ctl.Cursor); };
        _device.Commit();
    }

    private bool Hovered => _tracking;

    /// <summary>The window's place and size (px) and the page's scale; the controller fills it.</summary>
    public void Place(int x, int y, int w, int h, float scale, bool show, bool activate)
    {
        if (_ctl is null) return;
        if (Math.Abs(_ctl.RasterizationScale - scale) > 0.001) _ctl.RasterizationScale = scale;
        _ctl.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
        uint flags = SN.SWP_NOACTIVATE | (show ? SN.SWP_SHOWWINDOW : 0);
        SN.SetWindowPos(Hwnd, 0, x, y, w, h, flags | SN.SWP_NOZORDER);
        if (show)
        {
            _ctl.IsVisible = true;
            if (activate) Front();
        }
        _device.Commit();
    }

    public unsafe void Front()
    {
        uint fg = SN.GetWindowThreadProcessId(SN.GetForegroundWindow(), null), me = SN.GetCurrentThreadId();
        bool attached = fg != 0 && fg != me && SN.AttachThreadInput(me, fg, true);
        SN.BringWindowToTop(Hwnd);
        SN.SetForegroundWindow(Hwnd);
        if (attached) SN.AttachThreadInput(me, fg, false);
        _ctl?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    /// <summary>The page wakes for a show (the window is still hidden while it renders and measures).</summary>
    public void Wake() { if (_ctl is not null && !_ctl.IsVisible) _ctl.IsVisible = true; }

    /// <summary>The page rests while nothing is shown.</summary>
    public void Rest() { if (_ctl is not null) _ctl.IsVisible = false; }

    /// <summary>Hidden, and the page rests (no frames, no timers) until the next show: a warm host costs nothing.</summary>
    public void Hide()
    {
        SN.ShowWindow(Hwnd, SN.SW_HIDE);
        if (_ctl is not null) _ctl.IsVisible = false;
    }

    public bool Visible => SN.IsWindowVisible(Hwnd);

    public void SetCaptureExcluded(bool excluded) =>
        SN.SetWindowDisplayAffinity(Hwnd, excluded ? SN.WDA_EXCLUDEFROMCAPTURE : SN.WDA_NONE);

    /// <summary>The page asked to move the window (its header was pressed): Windows' own move loop.</summary>
    public void Drag()
    {
        SN.ReleaseCapture();
        SN.SendMessageW(Hwnd, SN.WM_NCLBUTTONDOWN, SN.HTCAPTION, 0);
    }

    public unsafe (int X, int Y, int W, int H) Rect
    {
        get
        {
            SN.RECT r;
            SN.GetWindowRect(Hwnd, &r);
            return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    private unsafe void Mouse(uint msg, nint wParam, nint lParam)
    {
        if (_ctl is null) return;
        uint mouseData = 0;
        var keys = (CoreWebView2MouseEventVirtualKeys)(int)(wParam & 0xFFFF);
        var p = new SN.POINT { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
        switch (msg)
        {
            case SN.WM_MOUSEWHEEL:
            case SN.WM_MOUSEHWHEEL:
                mouseData = (uint)(short)((wParam >> 16) & 0xFFFF);
                SN.ScreenToClient(Hwnd, &p);   // wheel points are in screen coordinates
                break;
            case SN.WM_XBUTTONDOWN:
            case SN.WM_XBUTTONUP:
            case SN.WM_XBUTTONDBLCLK:
                mouseData = (uint)((wParam >> 16) & 0xFFFF);
                break;
        }
        if (msg == SN.WM_MOUSEMOVE && !_tracking)
        {
            var t = new SN.TRACKMOUSEEVENT { cbSize = (uint)sizeof(SN.TRACKMOUSEEVENT), dwFlags = SN.TME_LEAVE, hwndTrack = Hwnd };
            SN.TrackMouseEvent(&t);
            _tracking = true;
        }
        if (msg is SN.WM_LBUTTONDOWN or SN.WM_RBUTTONDOWN or SN.WM_MBUTTONDOWN or SN.WM_XBUTTONDOWN) SN.SetCapture(Hwnd);
        if (msg is SN.WM_LBUTTONUP or SN.WM_RBUTTONUP or SN.WM_MBUTTONUP or SN.WM_XBUTTONUP && SN.GetCapture() == Hwnd) SN.ReleaseCapture();
        if (msg == SN.WM_MOUSELEAVE) _tracking = false;
        _ctl.SendMouseInput((CoreWebView2MouseEventKind)msg, keys, mouseData, new System.Drawing.Point(p.X, p.Y));
    }

    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        Windows.TryGetValue(hwnd, out var self);
        try
        {
            switch (msg)
            {
                case UiSyncContext.WM_RUN:
                    SurfaceHost.Ui?.Drain();
                    return 0;
                case SN.WM_ACTIVATE:
                    self?.Activated?.Invoke((wParam & 0xFFFF) != 0);
                    break;
                case SN.WM_CLOSE:
                    self?.CloseAsked?.Invoke();
                    return 0;
                case SN.WM_SETCURSOR when self?._ctl is not null && (lParam & 0xFFFF) == SN.HTCLIENT:
                    SN.SetCursor(self._ctl.Cursor);
                    return 1;
                case >= SN.WM_MOUSEMOVE and <= SN.WM_MOUSEHWHEEL:
                case SN.WM_MOUSELEAVE:
                    if (self is not null) { self.Mouse(msg, wParam, lParam); return 0; }
                    break;
                case SN.WM_DPICHANGED:
                    return 0;   // the host places and scales every show itself
            }
        }
        catch (Exception e)
        {
            SettingsLog.Write($"surfaces: window message {msg:X} failed ({e.GetType().Name})");
        }
        return SN.DefWindowProcW(hwnd, msg, wParam, lParam);
    }
}
