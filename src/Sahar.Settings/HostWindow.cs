using System.Runtime.InteropServices;

namespace Sahar.Settings;

/// <summary>
/// The plain Win32 window (class <c>SaharSettingsWindow</c>, title «إعدادات سَحَر»): 960×680 DIP centred on the cursor's
/// monitor, at least 720×520 DIP, its caption coloured to the page (DWM caption and text colours, immersive dark mode
/// when the page is dark).
/// </summary>
internal sealed unsafe class HostWindow
{
    public const string ClassName = "SaharSettingsWindow";
    public const string Title = "إعدادات سَحَر";
    public const int WidthDip = 960, HeightDip = 680, MinWidthDip = 720, MinHeightDip = 520;

    private const int AppIconId = 32512;   // the .NET SDK's resource id for ApplicationIcon
    private static Native.WndProc? _proc;   // kept alive: Windows calls it for the window's whole life
    private static HostWindow? _current;
    private readonly UiSyncContext _ui;

    public nint Hwnd { get; }
    public event Action? Resized;
    public event Action<bool>? Activated;
    public event Func<Task>? CloseRequested;
    public event Action? SettingChanged;

    public HostWindow(UiSyncContext ui)
    {
        _ui = ui;
        _current = this;
        _proc = Proc;
        nint instance = Native.GetModuleHandleW(null);
        fixed (char* cls = ClassName)
        {
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)sizeof(Native.WNDCLASSEXW),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
                hInstance = instance,
                hCursor = Native.LoadCursorW(0, Native.IDC_ARROW),
                hIcon = Native.LoadIconW(instance, AppIconId),   // the exe's icon (src/Sahar.Engine/assets/sahar.ico)
                lpszClassName = cls,
            };
            Native.RegisterClassExW(&wc);
        }
        var (x, y, w, h) = Placement();
        Hwnd = Native.CreateWindowExW(0, ClassName, Title, Native.WS_OVERLAPPEDWINDOW, x, y, w, h, 0, 0, instance, 0);
        if (Hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastPInvokeError()})");
        _ui.Hwnd = Hwnd;
    }

    public void Show()
    {
        Native.ShowWindow(Hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(Hwnd);
    }

    /// <summary>Test mode: to the front even when another program has the foreground (input attached for a moment).</summary>
    public void ForceFront()
    {
        if (Native.IsIconic(Hwnd)) Native.ShowWindow(Hwnd, Native.SW_RESTORE);
        uint fg = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), null), me = Native.GetCurrentThreadId();
        bool attached = fg != 0 && fg != me && Native.AttachThreadInput(me, fg, true);
        Native.BringWindowToTop(Hwnd);
        Native.SetForegroundWindow(Hwnd);
        if (attached) Native.AttachThreadInput(me, fg, false);
    }

    public float Dpi => Native.GetDpiForWindow(Hwnd) is var d and > 0 ? d / 96f : 1f;

    public (int Width, int Height) ClientSize
    {
        get
        {
            Native.RECT r;
            Native.GetClientRect(Hwnd, &r);
            return (r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    public (int X, int Y, int Width, int Height) WindowRect
    {
        get
        {
            Native.RECT r;
            Native.GetWindowRect(Hwnd, &r);
            return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        }
    }

    /// <summary>The caption takes the page's background and ink; immersive dark mode when the page is dark.</summary>
    public void SetCaption(uint bgRgb, uint inkRgb, bool dark)
    {
        static uint ColorRef(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);   // 0x00BBGGRR
        int on = dark ? 1 : 0;
        uint caption = ColorRef(bgRgb), text = ColorRef(inkRgb);
        Native.DwmSetWindowAttribute(Hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, &on, sizeof(int));
        Native.DwmSetWindowAttribute(Hwnd, Native.DWMWA_CAPTION_COLOR, &caption, sizeof(uint));
        Native.DwmSetWindowAttribute(Hwnd, Native.DWMWA_TEXT_COLOR, &text, sizeof(uint));
    }

    public void Close() => Native.DestroyWindow(Hwnd);

    /// <summary>The default size, centred on the work area of the monitor under the cursor, in that monitor's pixels.</summary>
    private static (int X, int Y, int W, int H) Placement()
    {
        Native.POINT p;
        Native.GetCursorPos(&p);
        nint monitor = Native.MonitorFromPoint(p, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = (uint)sizeof(Native.MONITORINFO) };
        Native.GetMonitorInfoW(monitor, &info);
        uint dx = 96, dy = 96;
        Native.GetDpiForMonitor(monitor, 0, &dx, &dy);
        float scale = dx / 96f;
        var work = info.rcWork;
        int ww = work.Right - work.Left, wh = work.Bottom - work.Top;
        int w = Math.Min((int)(WidthDip * scale), ww), h = Math.Min((int)(HeightDip * scale), wh);
        return (work.Left + (ww - w) / 2, work.Top + (wh - h) / 2, w, h);
    }

    private static nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var self = _current;
        try
        {
            switch (msg)
            {
                case UiSyncContext.WM_RUN:
                    self?._ui.Drain();
                    return 0;
                case Native.WM_SIZE:
                    self?.Resized?.Invoke();
                    break;
                case Native.WM_ACTIVATE:
                    self?.Activated?.Invoke((wParam & 0xFFFF) != 0);
                    break;
                case Native.WM_SETTINGCHANGE:
                    self?.SettingChanged?.Invoke();
                    break;
                case Native.WM_GETMINMAXINFO:
                {
                    float scale = Native.GetDpiForWindow(hwnd) is var d and > 0 ? d / 96f : 1f;
                    var info = (Native.MINMAXINFO*)lParam;
                    info->MinTrackSize.X = (int)(MinWidthDip * scale);
                    info->MinTrackSize.Y = (int)(MinHeightDip * scale);
                    return 0;
                }
                case Native.WM_DPICHANGED:
                {
                    var r = (Native.RECT*)lParam;
                    Native.SetWindowPos(hwnd, 0, r->Left, r->Top, r->Right - r->Left, r->Bottom - r->Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    return 0;
                }
                case Native.WM_CLOSE:
                    if (self?.CloseRequested is { } close) { _ = close(); return 0; }
                    break;
                case Native.WM_DESTROY:
                    Native.PostQuitMessage(Program.ExitCode);
                    return 0;
            }
        }
        catch (Exception e)
        {
            SettingsLog.Write($"error: window message {msg:X}: {e.GetType().Name}");
        }
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>The message loop; returns the code given to PostQuitMessage.</summary>
    public static int Loop()
    {
        Native.MSG m;
        while (Native.GetMessageW(&m, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(&m);
            Native.DispatchMessageW(&m);
        }
        return (int)m.wParam;
    }
}
