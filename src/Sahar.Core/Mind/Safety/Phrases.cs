using System.Text;
using Sahar.Core.Chat;

namespace Sahar.Core.Mind.Safety;

/// <summary>Reviewed phrase lists matched as whole words on folded text (diacritics, hamza, ة/ى folded; Arabic's
/// attached و ف ب ل ال allowed in front). A phrase ending in <c>*</c> is a stem (any ending).</summary>
internal static class Phrases
{
    private static readonly string[] Prefixes = ["وبال", "فبال", "وال", "فال", "بال", "كال", "لل", "ال", "و", "ف", "ب", "ل", "ك"];

    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = ChatCommands.Normalize(text);
        var b = new StringBuilder(s.Length);
        foreach (var c in s)
            b.Append(c switch
            {
                'ى' => 'ي',
                'ة' => 'ه',
                'ؤ' => 'و',
                'ئ' => 'ي',
                '’' or '‘' or '`' => '\'',
                _ => c,
            });
        return b.ToString();
    }

    public static string[] FoldAll(IEnumerable<string> phrases) => phrases.Select(p => p.EndsWith('*') ? Fold(p[..^1]) + "*" : Fold(p)).Distinct().ToArray();

    /// <summary>Is any phrase in the folded text, as whole words?</summary>
    public static bool Any(string folded, string[] phrases) => phrases.Any(p => Has(folded, p));

    public static bool Has(string folded, string phrase)
    {
        bool stem = phrase.EndsWith('*');
        var p = stem ? phrase[..^1] : phrase;
        if (p.Length == 0) return false;
        for (int i = folded.IndexOf(p, StringComparison.Ordinal); i >= 0; i = folded.IndexOf(p, i + 1, StringComparison.Ordinal))
        {
            int end = i + p.Length;
            bool right = stem || end == folded.Length || !char.IsLetterOrDigit(folded[end]);
            if (right && LeftOk(folded, i)) return true;
        }
        return false;
    }

    private static bool LeftOk(string s, int i)
    {
        if (i == 0 || !char.IsLetterOrDigit(s[i - 1])) return true;
        int start = i;
        while (start > 0 && char.IsLetterOrDigit(s[start - 1])) start--;
        var before = s[start..i];
        return Prefixes.Contains(before, StringComparer.Ordinal);
    }
}
