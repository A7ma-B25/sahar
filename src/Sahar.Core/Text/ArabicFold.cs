namespace Sahar.Core.Text;

/// <summary>
/// Search folding for her pickers (spec §4.3): tashkeel and tatweel go, hamza forms become their letter (أ إ آ ٱ → ا,
/// ؤ → و, ئ → ي), ة → ه, ى → ي, Arabic-Indic digits → ASCII, Latin lower case, spaces trimmed and single.
/// The page's <c>fold.js</c> does the same (tests/web share the fixtures).
/// </summary>
public static class ArabicFold
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var b = new System.Text.StringBuilder(text.Length);
        bool space = false;
        foreach (char ch in text)
        {
            char c = ch;
            if (c is >= '\u064B' and <= '\u065F' || c is '\u0670' or '\u0640' || c is >= '\u06D6' and <= '\u06ED' && c != '\u06E5' && c != '\u06E6') continue;   // tashkeel, dagger alif, tatweel, Quranic marks
            c = c switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                'ى' => 'ي',
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => char.ToLowerInvariant(c),
            };
            if (char.IsWhiteSpace(c)) { space = b.Length > 0; continue; }
            if (space) { b.Append(' '); space = false; }
            b.Append(c);
        }
        return b.ToString();
    }

    /// <summary>An empty query matches everything; digits match the number's start («1» finds 1, 10–19, 100–114);
    /// otherwise the folded name, or one of its words without «ال», starts with the folded query.</summary>
    public static bool Matches(string? query, string name, int number)
    {
        var q = Fold(query);
        if (q.Length == 0) return true;
        if (q.All(char.IsAsciiDigit)) return number.ToString(System.Globalization.CultureInfo.InvariantCulture) == q;
        var n = Fold(name);
        if (n.StartsWith(q, StringComparison.Ordinal)) return true;
        foreach (var w in n.Split(' '))
        {
            if (w.StartsWith(q, StringComparison.Ordinal)) return true;
            if (w.StartsWith("ال", StringComparison.Ordinal) && w[2..].StartsWith(q, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
