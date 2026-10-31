using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Spike;

/// <summary>Something that handles a window's messages. Return null to fall back to DefWindowProc.</summary>
internal interface IWindowHandler
{
    nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam);
}

/// <summary>One window class for every window in the spike; messages are routed by HWND.</summary>
internal static unsafe class Win
{
    private const string ClassName = "DeskCompanionSpike";
    private static readonly Dictionary<nint, IWindowHandler> Handlers = new();
    private static IWindowHandler? _pending;
    private static bool _registered;

    public static nint Instance => Native.GetModuleHandle(null);

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
                HbrBackground = 6, // COLOR_WINDOW + 1
                LpszClassName = name,
            };
            if (Native.RegisterClassEx(&cls) == 0)
                throw new InvalidOperationException($"RegisterClassEx failed: {Marshal.GetLastPInvokeError()}");
        }
        _registered = true;
    }

    public static nint Create(IWindowHandler handler, uint exStyle, uint style, int x, int y, int w, int h, string title = "", nint parent = 0)
    {
        EnsureClass();
        _pending = handler;
        nint hwnd;
        fixed (char* cls = ClassName)
        fixed (char* t = title)
            hwnd = Native.CreateWindowEx(exStyle, cls, t, style, x, y, w, h, parent, 0, Instance, 0);
        _pending = null;
        if (hwnd == 0) throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}");
        Handlers[hwnd] = handler;
        Native.OwnWindows.Add(hwnd);
        return hwnd;
    }

    /// <summary>A plain child STATIC control (used by the HUD for its text).</summary>
    public static nint CreateStatic(nint parent, int x, int y, int w, int h)
    {
        fixed (char* cls = "STATIC")
        fixed (char* t = "")
            return Native.CreateWindowEx(0, cls, t, Native.WS_CHILD | Native.WS_VISIBLE | Native.SS_LEFT, x, y, w, h, parent, 0, Instance, 0);
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
            if (handler != null)
            {
                var result = handler.OnMessage(hwnd, msg, wParam, lParam);
                if (result.HasValue) return result.Value;
            }
            if (msg == Native.WM_DESTROY) Handlers.Remove(hwnd);
        }
        catch (Exception e)
        {
            Log.Write($"wndproc error: {e}");
        }
        return Native.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public static (int x, int y) LParamPoint(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        return (x, y);
    }
}

/// <summary>A tiny log next to the metrics, for anything that goes wrong on a machine we can't see.</summary>
internal static class Log
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskCompanionSpike");

    public static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "log.txt"), $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the spike down.
        }
    }
}
