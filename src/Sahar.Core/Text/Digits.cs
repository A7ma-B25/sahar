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
}
