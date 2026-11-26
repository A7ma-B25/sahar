namespace Sahar.Core.Text;

/// <summary>Emoji detection (0.16). Owner ruling 3 was REVERSED 2026-11-26: her speech keeps its emoji — never wire
/// <see cref="Strip"/> into anything she says. Kept for tests and UI-chrome checks only. Pictographs, dingbats used as emoji,
/// variation selector 16, ZWJ and skin tones count; typographic marks (✓ · — « » …) do not.</summary>
public static class Emoji
{
    public static bool Has(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < text.Length; i++)
            if (IsEmojiAt(text, i, out _)) return true;
        return false;
    }

    /// <summary>The text without its emoji; the spaces they leave at the ends go too.</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var b = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (IsEmojiAt(text, i, out int len)) { i += len - 1; continue; }
            b.Append(text[i]);
        }
        return b.ToString().Trim();
    }

    private static bool IsEmojiAt(string s, int i, out int len)
    {
        len = 1;
        int cp = s[i];
        if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
        {
            cp = char.ConvertToUtf32(s[i], s[i + 1]);
            len = 2;
        }
        return cp is 0xFE0F or 0x200D or 0x20E3
            || cp is >= 0x1F000 and <= 0x1FAFF          // pictographs, emoticons, transport, symbols & pictographs ext.
            || cp is >= 0x1F1E6 and <= 0x1F1FF          // flags
            || cp is >= 0x2600 and <= 0x26FF            // misc symbols (☀ ⚽ ⛅ …)
            || cp is >= 0x2700 and <= 0x27BF && cp != 0x2713 && cp != 0x2714   // dingbats (✨ ✍ ✅), not the check marks
            || cp is 0x2B50 or 0x2B55 or 0x231A or 0x231B or 0x23F0 or 0x23F3 or 0x2934 or 0x2935 or 0x3030 or 0x303D;
    }
}
