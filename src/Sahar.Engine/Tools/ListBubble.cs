using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Tools;

/// <summary>
/// A list beside her (the 📋 clipboard history): a title, a Win32 LISTBOX (right-to-left) and one button.
/// A click (or Enter) on a line picks it and closes; the button runs its action; Esc or clicking elsewhere closes.
/// </summary>
internal sealed unsafe class ListBubble : IWindowHandler
{
    private const uint WM_APP_CLOSE = 0x8003, WM_COMMAND = 0x0111, WM_CTLCOLORSTATIC = 0x0138, WM_CTLCOLORLISTBOX = 0x0134,
        WM_ERASEBKGND = 0x0014, WM_SETFONT = 0x0030, WM_VKEYTOITEM = 0x002E, LB_ADDSTRING = 0x0180, LB_GETCURSEL = 0x0188;
    private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000, WS_VSCROLL = 0x00200000,
        LBS_NOTIFY = 0x1, LBS_NOINTEGRALHEIGHT = 0x100, LBS_WANTKEYBOARDINPUT = 0x400, SS_RIGHT = 0x2, BS_PUSHBUTTON = 0,
        WS_EX_RTLREADING = 0x2000, WS_EX_RIGHT = 0x1000, WS_EX_LEFTSCROLLBAR = 0x4000;
    private const int IdList = 100, IdButton = 101, LBN_SELCHANGE = 1, BN_CLICKED = 0, VK_RETURN = 0x0D;

    private readonly bool _capturable;
    private nint _hwnd, _list, _title, _button, _font;
    private Action<int>? _pick;
    private Action? _action;
    private bool _closing;

    public ListBubble(bool capturable) => _capturable = capturable;

    public bool IsOpen => _hwnd != 0;
    public int Lines { get; private set; }
    /// <summary>Test mode: what it shows (lines joined by newlines, the button).</summary>
    public string Text { get; private set; } = "";
    public string ButtonText { get; private set; } = "";

    public void Show(RectF body, string title, IReadOnlyList<string> lines, string buttonText, Action<int> pick, Action action)
    {
        Close();
        var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        var monitor = Monitors.At(centre);
        float dpi = monitor.DpiScale;
        int pad = (int)(12 * dpi), w = (int)(320 * dpi), titleH = (int)(22 * dpi), listH = (int)(Math.Clamp(lines.Count, 3, 10) * 24 * dpi),
            buttonH = (int)(30 * dpi), h = pad + titleH + (int)(6 * dpi) + listH + (int)(8 * dpi) + buttonH + pad;
        var work = monitor.WorkF;
        float x = centre.X > work.X + work.Width / 2 ? body.X - w - 12 * dpi : body.Right + 12 * dpi;
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - w));
        float y = Math.Clamp(centre.Y - h / 2f, work.Y, Math.Max(work.Y, work.Bottom - h));

        _pick = pick;
        _action = action;
        _closing = false;
        _hwnd = Win.Create(this, Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW, Native.WS_POPUP,
            new RECT { Left = (int)x, Top = (int)y, Right = (int)x + w, Bottom = (int)y + h }, "Sahar list");
        Win.SetCaptureExcluded(_hwnd, !_capturable);
        Native.SetWindowRgn(_hwnd, Native.CreateRoundRectRgn(0, 0, w + 1, h + 1, (int)(18 * dpi), (int)(18 * dpi)), 1);
        fixed (char* face = "Segoe UI") _font = Native.CreateFont(-(int)(14 * dpi), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, face);
        int inner = w - 2 * pad, top = pad;
        _title = Child("STATIC", title, WS_CHILD | WS_VISIBLE | SS_RIGHT, WS_EX_RTLREADING, pad, top, inner, titleH, 0);
        top += titleH + (int)(6 * dpi);
        _list = Child("LISTBOX", "", WS_CHILD | WS_VISIBLE | WS_BORDER | WS_VSCROLL | LBS_NOTIFY | LBS_NOINTEGRALHEIGHT | LBS_WANTKEYBOARDINPUT,
            WS_EX_RTLREADING | WS_EX_RIGHT | WS_EX_LEFTSCROLLBAR, pad, top, inner, listH, IdList);
        foreach (var line in lines)
            fixed (char* l = line) Native.SendMessage(_list, LB_ADDSTRING, 0, (nint)l);
        Lines = lines.Count;
        Text = string.Join('\n', lines);
        ButtonText = buttonText;
        top += listH + (int)(8 * dpi);
        _button = Child("BUTTON", buttonText, WS_CHILD | WS_VISIBLE | BS_PUSHBUTTON, 0, pad, top, inner, buttonH, IdButton);

        Native.ShowWindow(_hwnd, Native.SW_SHOW);
        Native.SetForegroundWindow(_hwnd);
        Native.SetFocus(_list);
        Log.Write("list: open");
    }

    private nint Child(string cls, string text, uint style, uint exStyle, int x, int y, int w, int h, int id)
    {
        nint hwnd;
        fixed (char* c = cls)
        fixed (char* t = text)
            hwnd = Native.CreateWindowEx(exStyle, c, t, style, x, y, w, h, _hwnd, id, Win.Instance, 0);
        Native.SendMessage(hwnd, WM_SETFONT, (nuint)_font, 1);
        return hwnd;
    }

    public void Close()
    {
        if (_hwnd == 0) return;
        _closing = true;
        Native.DestroyWindow(_hwnd);
        if (_font != 0) Native.DeleteObject(_font);
        _hwnd = _list = _title = _button = _font = 0;
        Lines = 0;
        Text = ButtonText = "";
        _pick = null;
        _action = null;
        Log.Write("list: closed");
    }

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_COMMAND:
            {
                int id = (int)(wParam & 0xFFFF), code = (int)((wParam >> 16) & 0xFFFF);
                if (id == IdList && code == LBN_SELCHANGE) { Pick(); return 0; }
                if (id == IdButton && code == BN_CLICKED)
                {
                    var action = _action;
                    Native.PostMessage(hwnd, WM_APP_CLOSE, 0, 0);
                    action?.Invoke();
                    return 0;
                }
                return null;
            }
            case WM_VKEYTOITEM:
            {
                int vk = (int)(wParam & 0xFFFF);
                if (vk == Native.VK_ESCAPE) { Native.PostMessage(hwnd, WM_APP_CLOSE, 0, 0); return -2; }
                if (vk == VK_RETURN) { Pick(); return -2; }
                return -1;
            }
            case WM_APP_CLOSE:
                Close();
                return 0;
            case Native.WM_ACTIVATE when (wParam & 0xFFFF) == 0 && !_closing:
                Native.PostMessage(hwnd, WM_APP_CLOSE, 0, 0);
                return null;
            case WM_ERASEBKGND:
            {
                RECT r;
                Native.GetClientRect(hwnd, &r);
                Native.FillRect((nint)wParam, &r, Native.GetStockObject(0));
                return 1;
            }
            case WM_CTLCOLORSTATIC:
            case WM_CTLCOLORLISTBOX:
                Native.SetTextColor((nint)wParam, 0x50231Eu);
                Native.SetBkColor((nint)wParam, 0xFFFFFF);
                return Native.GetStockObject(0);
            default:
                return null;
        }
    }

    private void Pick()
    {
        int index = (int)Native.SendMessage(_list, LB_GETCURSEL, 0, 0);
        if (index < 0) return;
        var pick = _pick;
        Native.PostMessage(_hwnd, WM_APP_CLOSE, 0, 0);
        pick?.Invoke(index);
    }

    /// <summary>Test mode: picks a line as a click would.</summary>
    public void PickLine(int index)
    {
        var pick = _pick;
        Close();
        pick?.Invoke(index);
    }
}
