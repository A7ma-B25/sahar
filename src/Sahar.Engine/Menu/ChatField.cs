using System.Runtime.InteropServices;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Render;

namespace Sahar.Engine.Menu;

/// <summary>
/// The card's chat row (spec §5): a borderless popup holding one EDIT exactly over the field — real text input (IME,
/// caret, RTL), 500 characters at most, Rubik from memory, the field's colours; the placeholder «احكي معي…» is painted
/// by the EDIT's subclass while it is empty (plan Ruling 10). Enter submits, Esc and Tab are passed to the card. What is
/// typed is never logged.
/// </summary>
internal sealed unsafe class ChatField : IWindowHandler
{
    private const uint WM_APP_ENTER = 0x8001, WM_APP_ESC = 0x8002, WM_APP_TAB = 0x8003, WM_APP_SHIFTTAB = 0x8004,
        WM_CTLCOLOREDIT = 0x0133, WM_ERASEBKGND = 0x0014, WM_SETFONT = 0x0030, EM_LIMITTEXT = 0x00C5, WM_CHAR = 0x0102,
        WM_PAINT = 0x000F, WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008;
    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, ES_RIGHT = 0x2, ES_AUTOHSCROLL = 0x80, WS_EX_RTLREADING = 0x2000,
        WS_EX_NOACTIVATE = 0x08000000;
    private const int GWLP_WNDPROC = -4, VK_RETURN = 0x0D, VK_TAB = 0x09;
    public const int MaxLength = 500;
    public const string Placeholder = "احكي معي…";

    private static nint s_oldEditProc, s_owner;
    private static uint s_placeholderColor;
    private static nint s_font;
    private static bool s_rtl = true;

    private readonly bool _capturable;
    private nint _hwnd, _edit, _brush;
    private uint _bg, _ink;

    public ChatField(bool capturable) => _capturable = capturable;

    public nint Hwnd => _hwnd;
    public nint EditHwnd => _edit;
    public bool Shown { get; private set; }
    public bool HasFocus => _edit != 0 && Native.GetFocus() == _edit;

    public event Action<string>? Submitted;
    public event Action? Escaped;
    /// <summary>Tab (false) or Shift+Tab (true) out of the field.</summary>
    public event Action<bool>? TabbedOut;
    /// <summary>Another app took over while the field had the focus.</summary>
    public event Action<nint>? Deactivated;

    /// <summary>Shows the field over <paramref name="field"/> (screen px), in the theme's colours.</summary>
    public void Show(RectI field, float dpi, ThemeTokens t, bool rtl)
    {
        _bg = Bgr(t.Sunken);
        _ink = Bgr(t.Ink);
        s_placeholderColor = Bgr(t.Text3);
        s_rtl = rtl;
        if (_brush != 0) Native.DeleteObject(_brush);
        _brush = Native.CreateSolidBrush(_bg);
        int pad = (int)MathF.Round(10 * dpi);
        int textH = (int)MathF.Round(22 * dpi);
        if (_hwnd == 0)
        {
            _hwnd = Win.Create(this, Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW, Native.WS_POPUP, Win.ToRECT(field), "Sahar chat");
            s_owner = _hwnd;
            Win.SetCaptureExcluded(_hwnd, !_capturable);
            s_font = Fonts.GdiFont(14 * dpi);
            fixed (char* c = "EDIT")
            fixed (char* empty = "")
                _edit = Native.CreateWindowEx(rtl ? WS_EX_RTLREADING : 0, c, empty, WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL | (rtl ? ES_RIGHT : 0),
                    pad, (field.Height - textH) / 2, field.Width - 2 * pad, textH, _hwnd, 0, Win.Instance, 0);
            Native.SendMessage(_edit, WM_SETFONT, (nuint)s_font, 1);
            Native.SendMessage(_edit, EM_LIMITTEXT, MaxLength, 0);
            s_oldEditProc = Native.SetWindowLongPtr(_edit, GWLP_WNDPROC, (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&EditProc);
        }
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, field.Left, field.Top, field.Width, field.Height, 0x0010 /* SWP_NOACTIVATE */);
        Native.SetWindowPos(_edit, 0, pad, (field.Height - textH) / 2, field.Width - 2 * pad, textH, 0x0014 /* NOZORDER|NOACTIVATE */);
        Native.ShowWindow(_hwnd, Native.SW_SHOWNOACTIVATE);
        Native.InvalidateRect(_edit, null, 1);
        Shown = true;
    }

    public void Hide()
    {
        if (_hwnd == 0) return;
        Shown = false;
        Native.ShowWindow(_hwnd, Native.SW_HIDE);
    }

    /// <summary>The caret goes into the field (the popup becomes the foreground window).</summary>
    public void Focus()
    {
        if (!Shown) return;
        Native.SetForegroundWindow(_hwnd);
        Native.SetFocus(_edit);
    }

    public string Text
    {
        get
        {
            int n = Native.GetWindowTextLength(_edit);
            if (n <= 0) return "";
            var buffer = new char[n + 1];
            fixed (char* b = buffer)
            {
                int got = Native.GetWindowText(_edit, b, buffer.Length);
                return new string(b, 0, got);
            }
        }
    }

    public void Clear()
    {
        if (_edit == 0) return;
        Native.SetWindowText(_edit, "");
        Native.InvalidateRect(_edit, null, 1);
    }

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_APP_ENTER:
                Submitted?.Invoke(Text);
                return 0;
            case WM_APP_ESC:
                Escaped?.Invoke();
                return 0;
            case WM_APP_TAB:
            case WM_APP_SHIFTTAB:
                TabbedOut?.Invoke(msg == WM_APP_SHIFTTAB);
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && Shown:   // the field lost the foreground
                Deactivated?.Invoke(lParam);
                return null;
            case WM_ERASEBKGND:
            {
                RECT r;
                Native.GetClientRect(hwnd, &r);
                Native.FillRect((nint)wParam, &r, _brush);
                return 1;
            }
            case WM_CTLCOLOREDIT:
                Native.SetTextColor((nint)wParam, _ink);
                Native.SetBkColor((nint)wParam, _bg);
                return _brush;
            default:
                return null;
        }
    }

    [UnmanagedCallersOnly]
    private static nint EditProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == Native.WM_KEYDOWN)
        {
            switch ((int)wParam)
            {
                case VK_RETURN: Native.PostMessage(s_owner, WM_APP_ENTER, 0, 0); return 0;
                case Native.VK_ESCAPE: Native.PostMessage(s_owner, WM_APP_ESC, 0, 0); return 0;
                case VK_TAB: Native.PostMessage(s_owner, Native.GetKeyState(0x10) < 0 ? WM_APP_SHIFTTAB : WM_APP_TAB, 0, 0); return 0;
            }
        }
        if (msg == WM_CHAR && (wParam == 13 || wParam == 27 || wParam == 9)) return 0;   // no beep
        var r = Native.CallWindowProc(s_oldEditProc, hwnd, msg, wParam, lParam);
        if (msg is WM_PAINT or WM_SETFOCUS or WM_KILLFOCUS && Native.GetWindowTextLength(hwnd) == 0) PaintPlaceholder(hwnd);
        if (msg == WM_CHAR) Native.InvalidateRect(hwnd, null, 1);   // the placeholder goes with the first letter
        return r;
    }

    private static void PaintPlaceholder(nint hwnd)
    {
        nint dc = Native.GetDC(hwnd);
        try
        {
            RECT r;
            Native.GetClientRect(hwnd, &r);
            Native.SetBkMode(dc, 1 /* TRANSPARENT */);
            Native.SetTextColor(dc, s_placeholderColor);
            nint old = Native.SelectObject(dc, s_font);
            const uint DT_SINGLELINE = 0x20, DT_VCENTER = 0x4, DT_RIGHT = 0x2, DT_RTLREADING = 0x20000, DT_NOPREFIX = 0x800;
            fixed (char* t = Placeholder)
                Native.DrawText(dc, t, Placeholder.Length, &r, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX | (s_rtl ? DT_RIGHT | DT_RTLREADING : 0));
            Native.SelectObject(dc, old);
        }
        finally { Native.ReleaseDC(hwnd, dc); }
    }

    private static uint Bgr(Rgba c) => (uint)(c.R | (c.G << 8) | (c.B << 16));
}
