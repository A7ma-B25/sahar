using System.Runtime.InteropServices;

namespace Sahar.Engine.Activities;

/// <summary>The few Win32 calls the activities add (0.13), kept apart from <see cref="Native"/>.</summary>
internal static unsafe partial class ActNative
{
    public const uint WM_MOUSEWHEEL = 0x020A, WM_DROPFILES = 0x0233, WM_RBUTTONUP = 0x0205;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_DESTROY = 0x8001, WINEVENT_OUTOFCONTEXT = 0;
    public const int GWL_EXSTYLE = -20;
    public const uint WS_EX_ACCEPTFILES = 0x10;

    [LibraryImport("shell32.dll")]
    public static partial void DragAcceptFiles(nint hwnd, int accept);

    [LibraryImport("shell32.dll", EntryPoint = "DragQueryFileW")]
    public static partial uint DragQueryFile(nint drop, uint index, char* file, uint size);

    [LibraryImport("shell32.dll")]
    public static partial void DragFinish(nint drop);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static partial int GetClassName(nint hwnd, char* name, int max);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static partial int GetWindowText(nint hwnd, char* text, int max);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW")]
    public static partial nint FindWindow(char* className, char* windowName);

    [LibraryImport("user32.dll")]
    public static partial int IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial int ShowWindow(nint hwnd, int cmd);

    [LibraryImport("user32.dll")]
    public static partial int ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint filter);

    public static string ClassOf(nint hwnd)
    {
        char* b = stackalloc char[128];
        int n = GetClassName(hwnd, b, 128);
        return n > 0 ? new string(b, 0, n) : "";
    }

    public static string TitleOf(nint hwnd)
    {
        char* b = stackalloc char[256];
        int n = GetWindowText(hwnd, b, 256);
        return n > 0 ? new string(b, 0, n) : "";
    }
}

internal static unsafe partial class ActCom
{
    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* obj);

    [LibraryImport("user32.dll", EntryPoint = "InternalGetWindowText")]
    public static partial int InternalGetWindowText(nint hwnd, char* text, int max);

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderPath(Guid* id, uint flags, nint token, char** path);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(void* p);

    /// <summary>A window's title without sending it a message (a hung app can't block us).</summary>
    public static string Title(nint hwnd)
    {
        char* b = stackalloc char[256];
        int n = InternalGetWindowText(hwnd, b, 256);
        return n > 0 ? new string(b, 0, n) : "";
    }

    public static string? KnownFolder(Guid id)
    {
        char* p;
        if (SHGetKnownFolderPath(&id, 0, 0, &p) != 0) return null;
        try { return new string(p); }
        finally { CoTaskMemFree(p); }
    }
}
