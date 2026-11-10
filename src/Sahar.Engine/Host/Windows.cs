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

    public static RECT ToRECT(RectI r) => new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };

    /// <summary>Moves windows together, in one batch (one DeferWindowPos), so they never lag each other.</summary>
    public static void Apply(WindowMove? a, WindowMove? b = null)
    {
        var moves = new[] { a, b }.Where(m => m is not null).Select(m => m!.Value).ToArray();
        if (moves.Length == 0) return;
        nint batch = Native.BeginDeferWindowPos(moves.Length);
        foreach (var w in moves)
            if (batch != 0) batch = Native.DeferWindowPos(batch, w.Hwnd, w.After, w.Rect.Left, w.Rect.Top, w.Rect.Width, w.Rect.Height, w.Flags);
        if (batch != 0 && Native.EndDeferWindowPos(batch) != 0) return;
        // A failed batch is dropped whole by Windows: move them one by one.
        foreach (var w in moves) Native.SetWindowPos(w.Hwnd, w.After, w.Rect.Left, w.Rect.Top, w.Rect.Width, w.Rect.Height, w.Flags);
    }

    /// <summary>A catcher's area: the monitor at a point, 1 px inside each edge — never the whole monitor, which the shell
    /// would take for a fullscreen app (Now Bar hides, notifications go quiet).</summary>
    public static RECT MonitorInset(System.Numerics.Vector2 at)
    {
        var (monitor, _, _) = Native.MonitorAt(new POINT { X = (int)at.X, Y = (int)at.Y });
        return new RECT { Left = monitor.Left + 1, Top = monitor.Top + 1, Right = monitor.Right - 1, Bottom = monitor.Bottom - 1 };
    }

    public static void SetCaptureExcluded(nint hwnd, bool excluded)
    {
        if (Native.SetWindowDisplayAffinity(hwnd, excluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE) == 0)
            Log.Write($"SetWindowDisplayAffinity failed: {Marshal.GetLastPInvokeError()}");
    }
}

/// <summary>
/// The transparent, click-through drawing surface, in physical pixels (per-monitor-v2), whose content is a
/// composition visual tree. It is only as big as she needs (<see cref="OverlayStage"/>): a window covering a whole
/// monitor is a fullscreen app to Windows, Now Bar and other taskbar tools. Mixed DPI is handled by the renderer.
/// </summary>
internal sealed class OverlayWindow : IWindowHandler
{
    public nint Hwnd { get; }
    /// <summary>Where the window is now (screen pixels).</summary>
    public RectI Rect { get; private set; }
    public event Action? DisplayChanged;
    /// <summary>A system setting changed (WM_SETTINGCHANGE): e.g. "Animation effects" on or off.</summary>
    public event Action? SettingChanged;

    public OverlayWindow(bool capturable, RectI initial)
    {
        Rect = initial;
        Hwnd = Win.Create(this,
            Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE, Win.ToRECT(initial), "Sahar");
        Native.SetLayeredWindowAttributes(Hwnd, 0, 255, Native.LWA_ALPHA);
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    /// <summary>The move to <paramref name="r"/> (none if it is there already): position only when the size is the same.</summary>
    public WindowMove? MoveTo(RectI r)
    {
        if (r.Equals(Rect)) return null;
        bool sameSize = r.Width == Rect.Width && r.Height == Rect.Height;
        Rect = r;
        return new WindowMove(Hwnd, 0, r, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER | (sameSize ? Native.SWP_NOSIZE : 0));
    }

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_DPICHANGED:
                return 0; // the stage sizes it; never let Windows resize it for a new DPI
            case Native.WM_DISPLAYCHANGE:
            case Native.WM_SETTINGCHANGE:
                DisplayChanged?.Invoke();
                if (msg == Native.WM_SETTINGCHANGE) SettingChanged?.Invoke();
                return null;
            default:
                return null;
        }
    }
}

/// <summary>One window's new place, for a batched move (<see cref="Win.Apply"/>).</summary>
internal readonly record struct WindowMove(nint Hwnd, nint After, RectI Rect, uint Flags);

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

    /// <summary>The move to <paramref name="r"/> (none if it is there already); it stays on top of the topmost band.</summary>
    public WindowMove? MoveTo(RectI r)
    {
        if (r.Equals(Rect)) return null;
        Rect = r;
        return new WindowMove(Hwnd, Native.HWND_TOPMOST, r, Native.SWP_NOACTIVATE);
    }

    public void SetVisible(bool visible) => Native.ShowWindow(Hwnd, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == Native.WM_MOUSEACTIVATE) return Native.MA_NOACTIVATE;
        return Handler?.Invoke(hwnd, msg, wParam, lParam);
    }
}
