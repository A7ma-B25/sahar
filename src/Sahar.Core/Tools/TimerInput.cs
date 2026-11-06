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
}
