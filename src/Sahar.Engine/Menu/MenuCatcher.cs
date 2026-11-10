using System.Numerics;
using Sahar.Core.Menu;
using Sahar.Engine.Host;

namespace Sahar.Engine.Menu;

/// <summary>
/// While the menu is open: an invisible window over her monitor, 1 px short on its taskbar's side only (a window covering a whole
/// monitor is a fullscreen app to the shell and taskbar tools); no redirection bitmap and nothing drawn, so it costs
/// no memory, but it still takes the mouse. A click on another monitor activates that app, which closes the menu.
/// It takes the clicks (an item, or "outside" which closes the menu and goes no further), the card's keys (Tab, arrows,
/// Enter, Esc, 1–3), and closes the menu when another window becomes active — unless it is one of
/// <see cref="AllowedFocus"/> (the chat field).
/// </summary>
internal sealed class MenuCatcher : IWindowHandler
{
    public nint Hwnd { get; }
    private RECT _bounds;

    public Action<Vector2>? Clicked { get; set; }
    public Action<Vector2>? Moved { get; set; }
    public Action? Dismissed { get; set; }
    public Action<MenuKey>? KeyPressed { get; set; }
    /// <summary>Windows that may take the focus without closing the menu (the chat field's popup and edit).</summary>
    public HashSet<nint> AllowedFocus { get; } = [];

    public MenuCatcher(bool capturable)
    {
        _bounds = Win.CatcherArea(Vector2.Zero);
        Hwnd = Win.Create(this, Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW,
            Native.WS_POPUP, _bounds, "Sahar menu");
        Win.SetCaptureExcluded(Hwnd, !capturable);
    }

    public bool Visible { get; private set; }

    /// <summary>Shows it over the monitor at <paramref name="at"/> (her body's centre).</summary>
    public void Show(Vector2 at)
    {
        _bounds = Win.CatcherArea(at);
        Native.SetWindowPos(Hwnd, Native.HWND_TOPMOST, _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left,
            _bounds.Bottom - _bounds.Top, 0);
        Visible = true;
        Native.ShowWindow(Hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(Hwnd);
        Native.SetFocus(Hwnd);
    }

    /// <summary>Takes the keyboard back (after the chat field had it).</summary>
    public void Focus()
    {
        if (!Visible) return;
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

    private static MenuKey? Key(int vk) => vk switch
    {
        0x09 => Native.GetKeyState(0x10) < 0 ? MenuKey.ShiftTab : MenuKey.Tab,
        0x25 => MenuKey.Left,
        0x26 => MenuKey.Up,
        0x27 => MenuKey.Right,
        0x28 => MenuKey.Down,
        0x0D => MenuKey.Enter,
        Native.VK_ESCAPE => MenuKey.Escape,
        0x31 or 0x61 => MenuKey.Digit1,
        0x32 or 0x62 => MenuKey.Digit2,
        0x33 or 0x63 => MenuKey.Digit3,
        _ => null,
    };

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
            case Native.WM_KEYDOWN when Key((int)wParam) is { } key:
                if (KeyPressed is { } k) k(key);
                else if (key == MenuKey.Escape) Dismissed?.Invoke();
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && Visible:   // WA_INACTIVE: another window took over
                if (lParam != 0 && AllowedFocus.Contains(lParam)) return null;   // the chat field: the menu stays
                Dismissed?.Invoke();
                return null;
            default:
                return null;
        }
    }
}
