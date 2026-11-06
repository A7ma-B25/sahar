using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Host;

/// <summary>
/// A hidden window for system messages plus the notification-area icon: menu "Hide 1 h" and "Quit".
/// The icon re-adds itself when Explorer restarts (TaskbarCreated). Power and session-lock messages
/// arrive here too and are passed on.
/// </summary>
internal sealed unsafe class Tray : IWindowHandler, IDisposable
{
    public const string Tip = "سَحَر · Sahar";
    private const uint CallbackMessage = 0x8001;          // WM_APP + 1
    private const uint WM_POWERBROADCAST = 0x0218, WM_WTSSESSION_CHANGE = 0x02B1;
    private const uint WM_CONTEXTMENU = 0x007B, WM_RBUTTONUP = 0x0205, NIN_SELECT = 0x0400, WM_LBUTTONUP = 0x0202;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_SHOWTIP = 0x80;
    private const uint MF_STRING = 0, MF_CHECKED = 0x8, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    private const int CmdHide = 1, CmdQuit = 2, CmdActivity = 10;   // 10 + (int)ActivityLevel

    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private bool _added;

    public nint Hwnd { get; }
    public event Action? HideHour;
    public event Action? ShowNow;
    public event Action<ActivityLevel>? ActivityChosen;
    /// <summary>The level shown as checked in the menu.</summary>
    public ActivityLevel Activity { get; set; } = ActivityLevel.Medium;
    public event Action? Quit;
    public event Action? PowerChanged;
    public event Action<bool>? SessionLocked;

    public Tray()
    {
        fixed (char* name = "TaskbarCreated") _taskbarCreated = Native.RegisterWindowMessage(name);
        Hwnd = Win.Create(this, Native.WS_EX_TOOLWINDOW, Native.WS_POPUP, default, "Sahar tray");
        Native.WTSRegisterSessionNotification(Hwnd, 0 /* this session */);
        _icon = MakeIcon();
        Add();
    }

    private void Add()
    {
        var d = Data();
        d.UFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        d.UCallbackMessage = CallbackMessage;
        d.HIcon = _icon;
        var tip = new Span<char>(d.SzTip, 128);
        Tip.AsSpan().CopyTo(tip);
        tip[Tip.Length] = '\0';
        // After a real Explorer restart the icon is gone and NIM_ADD works; if it still exists, refresh it.
        _added = Native.ShellNotifyIcon(NIM_ADD, &d) != 0 || Native.ShellNotifyIcon(NIM_MODIFY, &d) != 0;
        d.UVersion = 4;
        Native.ShellNotifyIcon(NIM_SETVERSION, &d);
        Log.Write(_added ? "tray icon added" : "tray icon not added (no taskbar yet?)");
    }

    /// <summary>Where Explorer shows the icon (or its overflow), or null when Explorer does not know it.</summary>
    public RECT? IconRect()
    {
        var id = new NOTIFYICONIDENTIFIER { CbSize = (uint)sizeof(NOTIFYICONIDENTIFIER), HWnd = Hwnd, UID = 1 };
        RECT r;
        return Native.Shell_NotifyIconGetRect(&id, &r) >= 0 ? r : null;
    }

    private NOTIFYICONDATAW Data() => new() { CbSize = (uint)sizeof(NOTIFYICONDATAW), HWnd = Hwnd, UID = 1 };

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            Add();
            return 0;
        }
        switch (msg)
        {
            case CallbackMessage:
                uint ev = (uint)(lParam & 0xFFFF);
                if (ev is WM_CONTEXTMENU or WM_RBUTTONUP) ShowMenu();
                else if (ev is NIN_SELECT or WM_LBUTTONUP) ShowNow?.Invoke();   // a click on the icon brings her back
                return 0;
            case WM_POWERBROADCAST:
                PowerChanged?.Invoke();
                return 1;
            case WM_WTSSESSION_CHANGE:
                if (wParam == 7) SessionLocked?.Invoke(true);        // WTS_SESSION_LOCK
                else if (wParam == 8) SessionLocked?.Invoke(false);  // WTS_SESSION_UNLOCK
                return 0;
        }
        return null;
    }

    private void ShowMenu()
    {
        nint menu = Native.CreatePopupMenu();
        nint levels = Native.CreatePopupMenu();
        foreach (var (level, text) in new[] { (ActivityLevel.Calm, "هادية / Calm"), (ActivityLevel.Medium, "متوسطة / Medium"), (ActivityLevel.Lively, "نشيطة / Lively") })
            fixed (char* t = text)
                Native.AppendMenu(levels, MF_STRING | (level == Activity ? MF_CHECKED : 0), (nuint)(CmdActivity + (int)level), t);
        fixed (char* activity = "نشاطها / Activity")
        fixed (char* hide = "إخفاء ساعة / Hide 1 h")
        fixed (char* quit = "إغلاق / Quit")
        {
            Native.AppendMenu(menu, MF_POPUP, (nuint)levels, activity);
            Native.AppendMenu(menu, MF_SEPARATOR, 0, null);
            Native.AppendMenu(menu, MF_STRING, CmdHide, hide);
            Native.AppendMenu(menu, MF_SEPARATOR, 0, null);
            Native.AppendMenu(menu, MF_STRING, CmdQuit, quit);
        }
        var p = Native.CursorPos();
        Native.SetForegroundWindow(Hwnd);   // so the menu closes when clicking elsewhere
        int cmd = Native.TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, p.X, p.Y, 0, Hwnd, 0);
        Native.DestroyMenu(menu);
        if (cmd is >= CmdActivity and <= CmdActivity + 2) ActivityChosen?.Invoke((ActivityLevel)(cmd - CmdActivity));
        else if (cmd == CmdHide) HideHour?.Invoke();
        else if (cmd == CmdQuit) Quit?.Invoke();
    }

    /// <summary>Placeholder icon (board icon C to come): a lavender Sahar-coloured disc with two eyes.</summary>
    private static nint MakeIcon()
    {
        const int n = 32;
        var header = new BITMAPINFOHEADER { BiSize = (uint)sizeof(BITMAPINFOHEADER), BiWidth = n, BiHeight = -n, BiPlanes = 1, BiBitCount = 32 };
        void* bits;
        nint color = Native.CreateDIBSection(0, &header, 0, &bits, 0, 0);
        var px = (uint*)bits;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - 16, dy = y + 0.5f - 16;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float a = Math.Clamp(14.5f - d, 0, 1);
                uint r = 0xF1, g = 0xEC, b = 0xFF;                       // Calm mid
                if (d > 9) { r = 0xC9; g = 0xB8; b = 0xFF; }              // Calm bud ring
                bool eye = (MathF.Abs(dx + 4.5f) < 1.6f || MathF.Abs(dx - 4.5f) < 1.6f) && MathF.Abs(dy) < 2.6f;
                if (eye) { r = 0x2A; g = 0x23; b = 0x50; }
                uint alpha = (uint)(a * 255);
                px[y * n + x] = (alpha << 24) | (r << 16) | (g << 8) | b;
            }
        nint mask = Native.CreateBitmap(n, n, 1, 1, null);
        var info = new ICONINFO { FIcon = 1, HbmMask = mask, HbmColor = color };
        nint icon = Native.CreateIconIndirect(&info);
        Native.DeleteObject(mask);
        Native.DeleteObject(color);
        return icon;
    }

    public void Dispose()
    {
        if (_added)
        {
            var d = Data();
            Native.ShellNotifyIcon(NIM_DELETE, &d);
        }
        Native.WTSUnRegisterSessionNotification(Hwnd);
        Native.DestroyIcon(_icon);
    }
}
