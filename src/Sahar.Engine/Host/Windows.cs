using System.Runtime.InteropServices;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Host;

/// <summary>Something that handles a window's messages. Return null to fall back to DefWindowProc.</summary>
internal interface IWindowHandler
{
    nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam);
}

/// <summary>One window class for every engine window; messages are routed by HWND.</summary>
internal static unsafe class Win
{
    private const string ClassName = "Sahar.Engine";
    private static readonly Dictionary<nint, IWindowHandler> Handlers = [];
    private static IWindowHandler? _pending;
    private static bool _registered;

    /// <summary>Every window the engine owns (never counted as "something fullscreen").</summary>
    public static readonly HashSet<nint> Own = [];

    public static nint Instance => Native.GetModuleHandle(null);

    public static nint Create(IWindowHandler handler, uint exStyle, uint style, RECT r, string title)
    {
        EnsureClass();
        _pending = handler;
        nint hwnd;
        fixed (char* cls = ClassName)
        fixed (char* t = title)
            hwnd = Native.CreateWindowEx(exStyle, cls, t, style, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                0, 0, Instance, 0);
        _pending = null;
        if (hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        Handlers[hwnd] = handler;
        Own.Add(hwnd);
        return hwnd;
    }

    private static void EnsureClass()
    {
        if (_registered) return;
        fixed (char* name = ClassName)
        {
            var cls = new WNDCLASSEXW
            {
                CbSize = (uint)sizeof(WNDCLASSEXW),
                LpfnWndProc = &WndProc,
                HInstance = Instance,
                HCursor = Native.LoadCursor(0, Native.IDC_ARROW),
                LpszClassName = name,
            };
            if (Native.RegisterClassEx(&cls) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
        _registered = true;
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        try
        {
            if (!Handlers.TryGetValue(hwnd, out var handler))
            {
                // Messages sent during CreateWindowEx arrive before the HWND is known.
                handler = _pending;
                if (handler != null) Handlers[hwnd] = handler;
            }
            if (handler?.OnMessage(hwnd, msg, wParam, lParam) is { } result) return result;
            if (msg == Native.WM_DESTROY) { Handlers.Remove(hwnd); Own.Remove(hwnd); }
        }
        catch (Exception e)
        {
            Log.Write($"wndproc error: {e}");
        }
        return Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public static void SetCaptureExcluded(nint hwnd, bool excluded)
    {
        if (Native.SetWindowDisplayAffinity(hwnd, excluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE) == 0)
            Log.Write($"SetWindowDisplayAffinity failed: {Marshal.GetLastPInvokeError()}");
    }
}

/// <summary>
/// The transparent, click-through drawing surface. One window over the whole virtual screen, in physical
/// pixels (per-monitor-v2), whose content is a composition visual tree. Mixed DPI is handled by the
/// renderer, which rasterises Sahar for the DPI of the monitor she is on.
/// </summary>
internal sealed class OverlayWindow : IWindowHandler
{
    public nint Hwnd { get; }
    public RECT Bounds { get; private set; }
    public event Action? DisplayChanged;
    /// <summary>A system setting changed (WM_SETTINGCHANGE): e.g. "Animation effects" on or off.</summary>
    public event Action? SettingChanged;

    public OverlayWindow(bool capturable)
    {
        Bounds = Native.VirtualScreen();
        Hwnd = Win.Create(this,
            Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE, Bounds, "Sahar");
        Native.SetLayeredWindowAttributes(Hwnd, 0, 255, Native.LWA_ALPHA);
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_DPICHANGED:
                return 0; // the overlay keeps covering the virtual screen; never let Windows resize it
            case Native.WM_DISPLAYCHANGE:
            case Native.WM_SETTINGCHANGE:
                FitVirtualScreen();
                DisplayChanged?.Invoke();
                if (msg == Native.WM_SETTINGCHANGE) SettingChanged?.Invoke();
                return null;
            default:
                return null;
        }
    }

    private void FitVirtualScreen()
    {
        var r = Native.VirtualScreen();
        if (r.Equals(Bounds)) return;
        Bounds = r;
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOACTIVATE);
        Log.Write($"overlay resized to {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");
    }
}

/// <summary>An almost invisible (alpha 1) window that follows Sahar's body and takes her mouse input.</summary>
internal sealed class InputWindow : IWindowHandler
{
    public nint Hwnd { get; }
    public RectI Rect { get; private set; }
    public Func<nint, uint, nuint, nint, nint?>? Handler { get; set; }

    public InputWindow(bool capturable, RectI initial)
    {
        Rect = initial;
        Hwnd = Win.Create(this,
            Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE,
            new RECT { Left = initial.Left, Top = initial.Top, Right = initial.Right, Bottom = initial.Bottom },
            "Sahar input");
        Native.SetLayeredWindowAttributes(Hwnd, 0, 1, Native.LWA_ALPHA);
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    public void MoveTo(RectI r)
    {
        if (r.Equals(Rect)) return;
        Rect = r;
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_NOACTIVATE);
    }

    public void SetVisible(bool visible) => Native.ShowWindow(Hwnd, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == Native.WM_MOUSEACTIVATE) return Native.MA_NOACTIVATE;
        return Handler?.Invoke(hwnd, msg, wParam, lParam);
    }
}
