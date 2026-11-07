using System.Globalization;

namespace Sahar.Core.Prayer;

/// <summary>What the 🕌 list and the reminder say, in Syrian Arabic.</summary>
public static class PrayerText
{
    /// <summary>"ساعة و12 دقيقة", "10 دقايق", "هلق"; a part of a minute counts as a whole one.</summary>
    public static string Duration(TimeSpan left)
    {
        int total = (int)Math.Ceiling(left.TotalMinutes - 1e-9);
        if (left.TotalMinutes < 1) return "هلق";
        int h = total / 60, m = total % 60;
        if (h == 0) return Count(m, "دقيقة", "دقيقتين", "دقايق");
        var hours = Count(h, "ساعة", "ساعتين", "ساعات");
        return m == 0 ? hours : hours + " و" + Count(m, "دقيقة", "دقيقتين", "دقايق");
    }

    private static string Count(int n, string one, string two, string few) => n switch
    {
        1 => one,
        2 => two,
        <= 10 => $"{n} {few}",
        _ => $"{n} {one}",
    };

    public static string NextLine(Prayer p, TimeSpan left) => $"الجاية: {PrayerNames.Arabic(p)} بعد {Duration(left)}";

    public static string Reminder(Prayer p, int minutes) => $"بعد {Duration(TimeSpan.FromMinutes(minutes))} {PrayerNames.Arabic(p)}";

    public static string TimeLine(Prayer p, DateTimeOffset at) =>
        $"{PrayerNames.Arabic(p)} — {at.ToString("HH:mm", CultureInfo.InvariantCulture)}";

    public static string CityLine(string city) => $"المدينة: {city} — غيّر";

    public static string MethodLine(PrayerMethod m) => $"طريقة الحساب: {PrayerNames.Method(m)}";
}
