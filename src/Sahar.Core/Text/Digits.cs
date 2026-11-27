using System.Text;

namespace Sahar.Core.Text;

/// <summary>Digits in her text: Arabic-Indic for what she shows (as in the mockups), Latin for parsing what you type.</summary>
public static class Digits
{
    /// <summary>0–9 → ٠–٩.</summary>
    public static string ToArabicIndic(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (var ch in s) b.Append(ch is >= '0' and <= '9' ? (char)('٠' + (ch - '0')) : ch);
        return b.ToString();
    }

    /// <summary>
    /// 0.17 review I4: her chat words in Arabic digits, except where digits are not prose — inside `code` and ``` fences,
    /// in links (http…, www.…), and in a word that mixes Latin letters and digits (v1.2.3, gpt-4o, range(10)).
    /// </summary>
    public static string ForChat(string s)
    {
        var b = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (s.AsSpan(i).StartsWith("```"))
            {
                int end = s.IndexOf("```", i + 3, StringComparison.Ordinal);
                end = end < 0 ? s.Length : end + 3;
                b.Append(s, i, end - i);
                i = end;
                continue;
            }
            if (s[i] == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > 0) { b.Append(s, i, end + 1 - i); i = end + 1; continue; }
            }
            if (char.IsWhiteSpace(s[i])) { b.Append(s[i++]); continue; }
            // one word (to the next space or backtick)
            int w = i;
            while (w < s.Length && !char.IsWhiteSpace(s[w]) && s[w] != '`') w++;
            var word = s[i..w];
            bool keep = word.StartsWith("http", StringComparison.OrdinalIgnoreCase) || word.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                || word.Any(char.IsAsciiLetter);
            b.Append(keep ? word : ToArabicIndic(word));
            i = w;
        }
        return b.ToString();
    }

    /// <summary>Arabic-Indic (٠–٩) and Persian (۰–۹) digits → 0–9.</summary>
    public static string ToLatin(string s)
    {
        var b = new StringBuilder(s.Length);
        foreach (var ch in s)
            b.Append(ch switch
            {
                >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
                _ => ch,
            });
        return b.ToString();
    }

    /// <summary>A number as she shows it: Latin digits in English, Arabic-Indic otherwise.</summary>
    public static string Format(long n, Settings.UiLanguage language)
    {
        var s = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return language == Settings.UiLanguage.English ? s : ToArabicIndic(s);
    }
}
