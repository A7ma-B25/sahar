using System.Numerics;
using Sahar.Engine.Host;

namespace Sahar.Engine.Menu;

/// <summary>
/// While the menu is open: an invisible window over her monitor, 1 px short of each edge (a window covering a whole
/// monitor is a fullscreen app to the shell and taskbar tools); no redirection bitmap and nothing drawn, so it costs
/// no memory, but it still takes the mouse. A click on another monitor activates that app, which closes the menu.
/// It takes the
/// clicks (a button, or "outside" which closes the menu and goes no further), Esc, and closes the menu when
/// another window becomes active.
/// </summary>
internal sealed class MenuCatcher : IWindowHandler
{
    public nint Hwnd { get; }
    private RECT _bounds;

    public Action<Vector2>? Clicked { get; set; }
    public Action<Vector2>? Moved { get; set; }
    public Action? Dismissed { get; set; }

    public MenuCatcher(bool capturable)
    {
        _bounds = Win.MonitorInset(Vector2.Zero);
        Hwnd = Win.Create(this, Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW,
            Native.WS_POPUP, _bounds, "Sahar menu");
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    public bool Visible { get; private set; }

    /// <summary>Shows it over the monitor at <paramref name="at"/> (her body's centre).</summary>
    public void Show(Vector2 at)
    {
        _bounds = Win.MonitorInset(at);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left,
            _bounds.Bottom - _bounds.Top, 0);
        Visible = true;
        Native.ShowWindow(Hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
    }

    public void Hide()
    {
        Visible = false;
        Native.ShowWindow(Hwnd, Native.SW_HIDE);
    }

    private Vector2 Screen(nint lParam) =>
        new(_bounds.Left + (short)(lParam & 0xFFFF), _bounds.Top + (short)((lParam >> 16) & 0xFFFF));

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
            case Native.WM_RBUTTONDOWN:
                return 0;
            case Native.WM_LBUTTONUP:
                Clicked?.Invoke(Screen(lParam));
                return 0;
            case Native.WM_RBUTTONUP:
                Dismissed?.Invoke();
                return 0;
            case Native.WM_MOUSEMOVE:
                Moved?.Invoke(Screen(lParam));
                return 0;
            case Native.WM_KEYDOWN when (int)wParam == Native.VK_ESCAPE:
                Dismissed?.Invoke();
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && Visible:   // WA_INACTIVE: another window took over
                Dismissed?.Invoke();
                return null;
            default:
                return null;
        }
    }
}
