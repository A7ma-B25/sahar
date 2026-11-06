using System.Runtime.InteropServices;

namespace Sahar.Engine;

// Hand-written Win32 bindings: blittable types only, so Native AOT needs no marshalling stubs.

[StructLayout(LayoutKind.Sequential)]
internal struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
internal struct RECT { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public nint Hwnd;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public POINT Pt;
    public uint Private;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint CbSize;
    public uint Style;
    public delegate* unmanaged<nint, uint, nuint, nint, nint> LpfnWndProc;
    public int CbClsExtra;
    public int CbWndExtra;
    public nint HInstance;
    public nint HIcon;
    public nint HCursor;
    public nint HbrBackground;
    public char* LpszMenuName;
    public char* LpszClassName;
    public nint HIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public uint CbSize;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESS_MEMORY_COUNTERS_EX
{
    public uint Cb;
    public uint PageFaultCount;
    public nuint PeakWorkingSetSize;
    public nuint WorkingSetSize;
    public nuint QuotaPeakPagedPoolUsage;
    public nuint QuotaPagedPoolUsage;
    public nuint QuotaPeakNonPagedPoolUsage;
    public nuint QuotaNonPagedPoolUsage;
    public nuint PagefileUsage;
    public nuint PeakPagefileUsage;
    public nuint PrivateUsage;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DispatcherQueueOptions
{
    public int DwSize;
    public int ThreadType;     // DQTYPE_THREAD_CURRENT = 2
    public int ApartmentType;  // DQTAT_COM_NONE = 0
}

internal static unsafe partial class Native
{
    // Window messages
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const nint MA_NOACTIVATE = 3;

    // Styles
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // ShowWindow / SetWindowPos
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOACTIVATE = 0x0010;
    public static readonly nint HWND_TOPMOST = -1;

    public const uint LWA_ALPHA = 0x2;
    public const uint WDA_NONE = 0x0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const int IDC_ARROW = 32512;

    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);

    public static void AttachToParentConsole()
    {
        if (!AttachConsole(-1 /* ATTACH_PARENT_PROCESS */)) return;
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(stdout);
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(WNDCLASSEXW* cls);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static partial nint CreateWindowEx(uint exStyle, char* className, char* windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hwnd, uint msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    public static partial int GetMessage(MSG* msg, nint hwnd, uint min, uint max);

    [LibraryImport("user32.dll")]
    public static partial int TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    public static partial int DestroyWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int index);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint instance, nint name);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    public static partial int GetMonitorInfo(nint monitor, MONITORINFO* info);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowRect(nint hwnd, RECT* rect);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(nint monitor, int type, uint* dpiX, uint* dpiY);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    public static partial nint GetModuleHandle(char* name);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    public static partial int GetProcessMemoryInfo(nint process, PROCESS_MEMORY_COUNTERS_EX* counters, uint size);

    [LibraryImport("CoreMessaging.dll")]
    public static partial int CreateDispatcherQueueController(DispatcherQueueOptions options, nint* controller);

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags,
        int* featureLevels, uint featureLevelCount, uint sdkVersion, nint* device, int* featureLevel, nint* context);

    [LibraryImport("d2d1.dll")]
    public static partial int D2D1CreateDevice(nint dxgiDevice, nint creationProperties, nint* d2dDevice);

    /// <summary>The bounding box of all monitors, in physical pixels.</summary>
    public static RECT VirtualScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN), y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        return new RECT
        {
            Left = x, Top = y,
            Right = x + GetSystemMetrics(SM_CXVIRTUALSCREEN), Bottom = y + GetSystemMetrics(SM_CYVIRTUALSCREEN),
        };
    }

    /// <summary>Monitor and work-area rects plus effective DPI of the monitor nearest to a point.</summary>
    public static (RECT monitor, RECT work, uint dpi) MonitorAt(POINT pt)
    {
        nint m = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { CbSize = (uint)sizeof(MONITORINFO) };
        GetMonitorInfo(m, &info);
        uint dx = 96, dy = 96;
        if (GetDpiForMonitor(m, 0 /* MDT_EFFECTIVE_DPI */, &dx, &dy) != 0) dx = 96;
        return (info.Monitor, info.Work, dx);
    }

    public static long WorkingSetBytes()
    {
        var m = new PROCESS_MEMORY_COUNTERS_EX { Cb = (uint)sizeof(PROCESS_MEMORY_COUNTERS_EX) };
        GetProcessMemoryInfo(GetCurrentProcess(), &m, m.Cb);
        return (long)m.WorkingSetSize;
    }
}
