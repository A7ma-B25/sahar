using System.Text;
using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Tools;

/// <summary>
/// 0.19 spec §7: what a tool returns is untrusted. <see cref="Sanitize"/> strips the characters used to hide
/// instructions — zero-width (U+200B–U+200D, U+2060–U+2064, U+FEFF, U+180E), the tag block (U+E0000–U+E007F), bidi
/// embeddings/overrides/isolates (U+202A–U+202E, U+2066–U+2069) and control characters except newline and tab — keeps
/// ordinary Arabic and the LRM/RLM/ALM marks, and caps it at <see cref="Cap"/> characters. <see cref="Block"/> wraps it
/// for the one tool-less step (in the user turn, never the system prompt). Pure.
/// </summary>
public static partial class ToolOutput
{
    public const int Cap = 4000;

    public static string Sanitize(string text)
    {
        var b = new StringBuilder(Math.Min(text.Length, Cap + 8));
        foreach (var r in text.EnumerateRunes())
        {
            int v = r.Value;
            if (v is 0x200B or 0x200C or 0x200D or 0xFEFF or 0x180E || v is >= 0x2060 and <= 0x2064) continue;
            if (v is >= 0x202A and <= 0x202E || v is >= 0x2066 and <= 0x2069) continue;
            if (v is >= 0xE0000 and <= 0xE007F) continue;
            if (Rune.GetUnicodeCategory(r) == System.Globalization.UnicodeCategory.Control && v is not ('\n' or '\t')) continue;
            if (b.Length + r.Utf16SequenceLength > Cap) { b.Append('…'); break; }
            b.Append(r.ToString());
        }
        return b.ToString();
    }

    [GeneratedRegex("<(?=/?tool_output)", RegexOptions.IgnoreCase)] private static partial Regex Tag();

    /// <summary><c>&lt;tool_output source="…" trust="untrusted"&gt;</c> around the (already sanitized) text; a tag inside
    /// the text cannot close or open the block.</summary>
    public static string Block(string source, string text)
    {
        var src = new string(source.Where(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-').ToArray());
        return $"<tool_output source=\"{src}\" trust=\"untrusted\">\n{Tag().Replace(Sanitize(text), "‹")}\n</tool_output>";
    }
}
