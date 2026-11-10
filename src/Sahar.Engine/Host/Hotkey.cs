using Sahar.Core.Menu;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Host;

/// <summary>The card's global shortcut (spec §5; default Ctrl+Alt+S), registered on the tray window (id 1).</summary>
internal sealed class Hotkey(nint trayHwnd)
{
    public const int Id = 1;
    private const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    public Shortcut? Current { get; private set; }

    /// <summary>Registers <paramref name="s"/> (replacing any); on failure nothing stays registered and the reason is
    /// <see cref="Shortcuts.Taken"/> (another program has it).</summary>
    public bool TryRegister(Shortcut s, out string? reason)
    {
        Unregister();
        uint mods = MOD_NOREPEAT;
        if (s.Mods.HasFlag(KeyMods.Alt)) mods |= MOD_ALT;
        if (s.Mods.HasFlag(KeyMods.Ctrl)) mods |= MOD_CONTROL;
        if (s.Mods.HasFlag(KeyMods.Shift)) mods |= MOD_SHIFT;
        if (s.Mods.HasFlag(KeyMods.Win)) mods |= MOD_WIN;
        if (Native.RegisterHotKey(trayHwnd, Id, mods, (uint)s.Vk) == 0)
        {
            reason = Shortcuts.Taken;
            Log.Write("hotkey: taken");   // never which keys someone else uses: only that it failed
            return false;
        }
        Current = s;
        reason = null;
        Log.Write($"hotkey: {s}");
        return true;
    }

    public void Unregister()
    {
        if (Current is null) return;
        Native.UnregisterHotKey(trayHwnd, Id);
        Current = null;
    }
}
