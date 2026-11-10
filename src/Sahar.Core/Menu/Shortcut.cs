using System.Globalization;

namespace Sahar.Core.Menu;

[Flags]
public enum KeyMods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

/// <summary>A global shortcut: modifiers and one virtual key ("Ctrl+Alt+S"). Parsing ignores case and order.</summary>
public readonly record struct Shortcut(KeyMods Mods, int Vk)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Mods.HasFlag(KeyMods.Ctrl)) parts.Add("Ctrl");
        if (Mods.HasFlag(KeyMods.Alt)) parts.Add("Alt");
        if (Mods.HasFlag(KeyMods.Shift)) parts.Add("Shift");
        if (Mods.HasFlag(KeyMods.Win)) parts.Add("Win");
        parts.Add(KeyName(Vk));
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Shortcut s)
    {
        s = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var mods = KeyMods.None;
        int vk = 0;
        foreach (var raw in text.Split('+'))
        {
            var p = raw.Trim().ToLowerInvariant();
            switch (p)
            {
                case "ctrl" or "control": mods |= KeyMods.Ctrl; break;
                case "alt": mods |= KeyMods.Alt; break;
                case "shift": mods |= KeyMods.Shift; break;
                case "win" or "windows": mods |= KeyMods.Win; break;
                default:
                    if (vk != 0) return false;
                    vk = KeyCode(p);
                    if (vk == 0) return false;
                    break;
            }
        }
        if (vk == 0) return false;
        s = new Shortcut(mods, vk);
        return true;
    }

    private static int KeyCode(string p) => p switch
    {
        { Length: 1 } when p[0] is >= 'a' and <= 'z' => 0x41 + (p[0] - 'a'),
        { Length: 1 } when p[0] is >= '0' and <= '9' => 0x30 + (p[0] - '0'),
        "space" => 0x20,
        "esc" or "escape" => 0x1B,
        "tab" => 0x09,
        _ when p.Length is 2 or 3 && p[0] == 'f' && int.TryParse(p.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24 => 0x70 + n - 1,
        _ => 0,
    };

    private static string KeyName(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture),
        0x20 => "Space",
        0x1B => "Esc",
        0x09 => "Tab",
        _ => "?",
    };
}

/// <summary>Whether a shortcut may be used, and why not (her voice, for the settings page).</summary>
public sealed record ShortcutCheck(bool Ok, Shortcut Value, string? Reason);

public static class Shortcuts
{
    public const string NeedsCtrlOrAlt = "لازم يكون فيه Ctrl أو Alt.";
    public const string WindowsOwn = "هالاختصار للويندوز، اختار غيره.";
    public const string AppsCommon = "هالاختصار بتستعمله البرامج كتير، اختار غيره.";
    /// <summary>Another program already registered it (RegisterHotKey failed).</summary>
    public const string Taken = "في برنامج تاني ماخد هالاختصار، جرّب غيره.";

    public static Shortcut Default { get; } = new(KeyMods.Ctrl | KeyMods.Alt, 0x53);

    private static readonly HashSet<int> CommonCtrl = [.. "CVXZASFPWTNOY".Select(c => (int)c)];

    public static ShortcutCheck Check(string? text)
    {
        if (text is null) return new(true, Default, null);
        if (!Shortcut.TryParse(text, out var s)) return new(false, default, NeedsCtrlOrAlt);
        var m = s.Mods;
        if (m.HasFlag(KeyMods.Win)) return new(false, s, WindowsOwn);
        bool alt = m.HasFlag(KeyMods.Alt), ctrl = m.HasFlag(KeyMods.Ctrl);
        if (alt && !ctrl && s.Vk is 0x73 or 0x09 or 0x1B or 0x20) return new(false, s, WindowsOwn);   // Alt+F4, Tab, Esc, Space
        if (ctrl && !alt && s.Vk == 0x1B) return new(false, s, WindowsOwn);                              // Ctrl+Esc (Start)
        if (!alt && !ctrl) return new(false, s, NeedsCtrlOrAlt);
        if (ctrl && !alt && !m.HasFlag(KeyMods.Shift) && CommonCtrl.Contains(s.Vk)) return new(false, s, AppsCommon);
        return new(true, s, null);
    }
}
