using System.Numerics;
using Sahar.Engine.Host;

namespace Sahar.Engine.Games;

/// <summary>
/// While a game runs: an invisible window over the whole virtual screen (no redirection bitmap, nothing drawn) that
/// takes the mouse for the game — press, move, release (captured, so a flick can leave the window) — and ends the
/// game on Esc, a right-click, or another window becoming active.
/// </summary>
internal sealed class GameCatcher : IWindowHandler
{
    public nint Hwnd { get; }
    private RECT _bounds;

    public Action<Vector2>? Down { get; set; }
    public Action<Vector2>? Moved { get; set; }
    public Action<Vector2>? Up { get; set; }
    public Action? Quit { get; set; }
    public bool Visible { get; private set; }

    public GameCatcher(bool capturable)
    {
        _bounds = Native.VirtualScreen();
        Hwnd = Win.Create(this, Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW,
            Native.WS_POPUP, _bounds, "Sahar game");
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    public void Show()
    {
        _bounds = Native.VirtualScreen();
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
        Native.ReleaseCapture();
        Native.ShowWindow(Hwnd, Native.SW_HIDE);
    }

    private Vector2 Screen(nint lParam) =>
        new(_bounds.Left + (short)(lParam & 0xFFFF), _bounds.Top + (short)((lParam >> 16) & 0xFFFF));

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_LBUTTONDOWN:
                Native.SetCapture(Hwnd);
                Down?.Invoke(Screen(lParam));
                return 0;
            case Native.WM_LBUTTONUP:
                Native.ReleaseCapture();
                Up?.Invoke(Screen(lParam));
                return 0;
            case Native.WM_MOUSEMOVE:
                Moved?.Invoke(Screen(lParam));
                return 0;
            case Native.WM_RBUTTONDOWN:
                return 0;
            case Native.WM_RBUTTONUP:
                Quit?.Invoke();
                return 0;
            case Native.WM_KEYDOWN when (int)wParam == Native.VK_ESCAPE:
                Quit?.Invoke();
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && Visible:   // another window took over
                Quit?.Invoke();
                return null;
            default:
                return null;
        }
    }
}
