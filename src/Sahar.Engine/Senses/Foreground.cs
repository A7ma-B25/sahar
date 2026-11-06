using System.Runtime.InteropServices;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Senses;

/// <summary>Raises <see cref="Changed"/> when the foreground window changes (WinEvent hook, out of context).</summary>
internal static unsafe class Foreground
{
    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private static nint _hook, _moveHook;

    public static event Action? Changed;

    public static void Start()
    {
        if (_hook != 0) return;
        _hook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT);
        _moveHook = Native.SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, 0, &OnEvent, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (_hook == 0) Log.Write("foreground hook failed");
    }

    public static void Stop()
    {
        if (_hook != 0) Native.UnhookWinEvent(_hook);
        if (_moveHook != 0) Native.UnhookWinEvent(_moveHook);
        _hook = _moveHook = 0;
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(nint hook, uint ev, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try { Changed?.Invoke(); }
        catch (Exception e) { Log.Write($"foreground hook error: {e}"); }
    }
}
