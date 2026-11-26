using System.Globalization;
using Sahar.Core.Text;

namespace Sahar.Core.Tools;

/// <summary>Minutes typed for a timer: Latin, Arabic-Indic (٢٥) or Persian (۲۵) digits, 1–600, an optional "د"/"m"/"min".</summary>
public static class TimerInput
{
    public const int MaxMinutes = 600;

    public static int? ParseMinutes(string text)
    {
        var s = text.Trim();
        foreach (var suffix in new[] { "دقيقة", "دقايق", "min", "د", "m" })
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { s = s[..^suffix.Length].TrimEnd(); break; }
        if (s.Length is 0 or > 4) return null;
        int value = 0;
        foreach (char ch in s)
        {
            int d = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= '٠' and <= '٩' => ch - '٠',
                >= '۰' and <= '۹' => ch - '۰',
                _ => -1,
            };
            if (d < 0) return null;
            value = value * 10 + d;
        }
        return value is >= 1 and <= MaxMinutes ? value : null;
    }

    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(MaxMinutes);

    private static readonly Dictionary<string, (int Seconds, int Count)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ثانية"] = (1, 1), ["ثانيه"] = (1, 1), ["ثواني"] = (1, 1), ["ثوان"] = (1, 1), ["ثانيتين"] = (1, 2), ["ث"] = (1, 1),
        ["s"] = (1, 1), ["sec"] = (1, 1), ["secs"] = (1, 1), ["second"] = (1, 1), ["seconds"] = (1, 1),
        ["دقيقة"] = (60, 1), ["دقيقه"] = (60, 1), ["دقايق"] = (60, 1), ["دقائق"] = (60, 1), ["دقيقتين"] = (60, 2), ["د"] = (60, 1),
        ["m"] = (60, 1), ["min"] = (60, 1), ["mins"] = (60, 1), ["minute"] = (60, 1), ["minutes"] = (60, 1),
        ["ساعة"] = (3600, 1), ["ساعه"] = (3600, 1), ["ساعات"] = (3600, 1), ["ساعتين"] = (3600, 2), ["h"] = (3600, 1),
        ["hr"] = (3600, 1), ["hour"] = (3600, 1), ["hours"] = (3600, 1),
    };

    /// <summary>0.15.0: a timer's length in any unit — «5 ثواني», «٢٥» (minutes), «ساعة», «ساعتين», «ربع ساعة»,
    /// «10 sec», «5د». 1 s to 600 min; null otherwise (owner: «شغلي المؤقت 5 ثواني» started five minutes).</summary>
    public static TimeSpan? ParseDuration(string text)
    {
        var s = Digits.ToLatin(text ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        // "5د" / "10sec": the number and its unit written together.
        int cut = 0;
        while (cut < s.Length && char.IsAsciiDigit(s[cut])) cut++;
        if (cut > 0 && cut < s.Length && s[cut] != ' ') s = s[..cut] + " " + s[cut..];
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        long seconds;
        switch (words)
        {
            case [var n] when Number(n) is { } v: seconds = v * 60L; break;
            case [var u] when Units.TryGetValue(u, out var one): seconds = one.Seconds * (long)one.Count; break;
            case [var n, var u] when Number(n) is { } v && Units.TryGetValue(u, out var unit): seconds = v * (long)unit.Seconds; break;
            case ["ربع", var u] when Units.TryGetValue(u, out var q) && q.Seconds == 3600: seconds = 900; break;
            case ["نص" or "نصف", var u] when Units.TryGetValue(u, out var h) && h.Seconds == 3600: seconds = 1800; break;
            default: return null;
        }
        var span = TimeSpan.FromSeconds(seconds);
        return span >= TimeSpan.FromSeconds(1) && span <= MaxDuration ? span : null;
    }

    private static int? Number(string s) =>
        s.Length is > 0 and <= 5 && s.All(char.IsAsciiDigit) ? int.Parse(s, CultureInfo.InvariantCulture) : null;

    /// <summary>A length in her words: «٢٥ دقيقة», «٥ ثواني», «١ دقيقة و٣٠ ثانية».</summary>
    public static string Words(TimeSpan t)
    {
        int total = (int)Math.Round(t.TotalSeconds), m = total / 60, sec = total % 60;
        static string N(int n) => Digits.ToArabicIndic(n.ToString(CultureInfo.InvariantCulture));
        string Sec(int n) => $"{N(n)} {(n is >= 3 and <= 10 ? "ثواني" : "ثانية")}";
        if (m == 0) return Sec(sec);
        return sec == 0 ? $"{N(m)} دقيقة" : $"{N(m)} دقيقة و{Sec(sec)}";
    }
}
