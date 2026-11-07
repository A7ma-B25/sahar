using System.Numerics;
using System.Runtime.InteropServices;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Tools;

/// <summary>
/// A small typing bubble beside her (spec §7.1: timer minutes, quick notes): a title, one Win32 EDIT
/// (right-to-left reading, max 500 characters) and up to three lines below. Enter hands the text over and
/// closes it; Esc or clicking elsewhere closes it.
/// </summary>
internal sealed unsafe class TextBubble : IWindowHandler
{
    private const uint WM_APP_ENTER = 0x8001, WM_APP_ESC = 0x8002, WM_CTLCOLOREDIT = 0x0133, WM_CTLCOLORSTATIC = 0x0138,
        WM_ERASEBKGND = 0x0014, WM_SETFONT = 0x0030, EM_LIMITTEXT = 0x00C5, WM_CHAR = 0x0102;
    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000, ES_RIGHT = 0x2,
        ES_AUTOHSCROLL = 0x80, SS_RIGHT = 0x2, SS_ENDELLIPSIS = 0x4000, WS_EX_RTLREADING = 0x2000;
    private const int GWLP_WNDPROC = -4, VK_RETURN = 0x0D;

    private static nint s_oldEditProc, s_owner;

    private readonly bool _capturable;
    private nint _hwnd, _edit, _title, _font;
    private readonly List<nint> _lines = [];
    private Action<string>? _onEnter;
    private bool _closing;

    public TextBubble(bool capturable) => _capturable = capturable;

    public bool IsOpen => _hwnd != 0;
    /// <summary>The lines shown under the edit box (for the UI tests).</summary>
    public IReadOnlyList<string> Lines { get; private set; } = [];
    public string Title { get; private set; } = "";
    public nint EditHwnd => _edit;

    /// <summary>Screen rectangle of the edit box (px), for the UI tests.</summary>
    public RectI EditRect
    {
        get
        {
            RECT r;
            if (_edit == 0 || Native.GetWindowRect(_edit, &r) == 0) return default;
            return new RectI(r.Left, r.Top, r.Right, r.Bottom);
        }
    }

    public void Show(RectF body, string title, IReadOnlyList<string> below, Action<string> onEnter)
    {
        Close();
        title = Sahar.Core.Settings.Address.ToYou(title);   // her words agree with who she talks to
        var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        var monitor = Monitors.At(centre);
        float dpi = monitor.DpiScale;
        int pad = (int)(12 * dpi), w = (int)(280 * dpi), titleH = (int)(22 * dpi), editH = (int)(30 * dpi), lineH = (int)(22 * dpi);
        int h = pad + titleH + (int)(6 * dpi) + editH + (below.Count > 0 ? (int)(8 * dpi) + below.Count * lineH : 0) + pad;
        var work = monitor.WorkF;
        bool left = centre.X > work.X + work.Width / 2;
        float x = left ? body.X - w - 12 * dpi : body.Right + 12 * dpi;
        float y = centre.Y - h / 2f;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - h));

        _onEnter = onEnter;
        _closing = false;
        Lines = below;
        Title = title;
        _hwnd = Win.Create(this, Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW, Native.WS_POPUP,
            new RECT { Left = (int)x, Top = (int)y, Right = (int)x + w, Bottom = (int)y + h }, "Sahar bubble");
        s_owner = _hwnd;
        Win.SetCaptureExcluded(_hwnd, !_capturable);
        Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)(18 * dpi), (int)(18 * dpi)), 1);
        fixed (char* face = "Segoe UI") _font = Native.CreateFont(-(int)(14 * dpi), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);

        int inner = w - 2 * pad, top = pad;
        _title = Child("STATIC", title, WS_CHILD | WS_VISIBLE | SS_RIGHT, WS_EX_RTLREADING, pad, top, inner, titleH);
        top += titleH + (int)(6 * dpi);
        _edit = Child("EDIT", "", WS_CHILD | WS_VISIBLE | WS_BORDER | ES_RIGHT | ES_AUTOHSCROLL, WS_EX_RTLREADING, pad, top, inner, editH);
        Native.SendMessage(_edit, EM_LIMITTEXT, 500, 0);
        top += editH + (int)(8 * dpi);
        foreach (var line in below)
        {
            _lines.Add(Child("STATIC", line, WS_CHILD | WS_VISIBLE | SS_RIGHT | SS_ENDELLIPSIS, WS_EX_RTLREADING, pad, top, inner, lineH));
            top += lineH;
        }
        s_oldEditProc = Native.SetWindowLongPtr(_edit, GWLP_WNDPROC, (nint)(delegate* unmanaged<nint, uint, nuint, nint, nint>)&EditProc);

        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(_hwnd);
        Native.SetFocus(_edit);
        Log.Write("bubble: open");
    }

    private nint Child(string cls, string text, uint style, uint exStyle, int x, int y, int w, int h)
    {
        nint hwnd;
        fixed (char* c = cls)
        fixed (char* t = text)
            hwnd = Native.CreateWindowEx(exStyle, c, t, style, x, y, w, h, _hwnd, 0, Win.Instance, 0);
        Native.SendMessage(hwnd, WM_SETFONT, (nuint)_font, 1);
        return hwnd;
    }

    public void Close()
    {
        if (_hwnd == 0) return;
        _closing = true;
        if (_edit != 0 && s_oldEditProc != 0) Native.SetWindowLongPtr(_edit, GWLP_WNDPROC, s_oldEditProc);
        Native.DestroyWindow(_hwnd);
        if (_font != 0) Native.DeleteObject(_font);
        _hwnd = _edit = _title = _font = 0;
        _lines.Clear();
        Lines = [];
        s_owner = 0;
        s_oldEditProc = 0;
        _onEnter = null;
        Log.Write("bubble: closed");
    }

    private string Text()
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

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_APP_ENTER:
            {
                var text = Text();
                var done = _onEnter;
                Close();
                done?.Invoke(text);
                return 0;
            }
            case WM_APP_ESC:
                Close();
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && !_closing:   // clicked elsewhere
                Native.PostMessage(hwnd, WM_APP_ESC, 0, 0);
                return null;
            case WM_ERASEBKGND:
            {
                RECT r;
                Native.GetClientRect(hwnd, &r);
                Native.FillRect((nint)wParam, &r, Native.GetStockObject(0 /* WHITE_BRUSH */));
                return 1;
            }
            case WM_CTLCOLORSTATIC:
            case WM_CTLCOLOREDIT:
                Native.SetTextColor((nint)wParam, lParam == _title || msg == WM_CTLCOLOREDIT ? 0x50231Eu : 0x784E4Au);   // #1E2350 / #4A4E78 (BGR)
                Native.SetBkColor((nint)wParam, 0xFFFFFF);
                return Native.GetStockObject(0);
            default:
                return null;
        }
    }

    [UnmanagedCallersOnly]
    private static nint EditProc(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == Native.WM_KEYDOWN && (int)wParam == VK_RETURN) { Native.PostMessage(s_owner, WM_APP_ENTER, 0, 0); return 0; }
        if (msg == Native.WM_KEYDOWN && (int)wParam == Native.VK_ESCAPE) { Native.PostMessage(s_owner, WM_APP_ESC, 0, 0); return 0; }
        if (msg == WM_CHAR && (wParam == 13 || wParam == 27)) return 0;   // no beep
        return Native.CallWindowProc(s_oldEditProc, hwnd, msg, wParam, lParam);
    }
}
