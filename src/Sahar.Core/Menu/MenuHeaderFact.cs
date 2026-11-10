using System.Globalization;
using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Sahar.Core.Text;

namespace Sahar.Core.Menu;

/// <summary>What the header could say under her line (null = not happening).</summary>
public sealed record MenuFacts(TimeSpan? TimerLeft, bool ClaudeWaiting, (Sahar.Core.Prayer.Prayer Prayer, TimeSpan Left)? NextPrayer, bool QuranPlaying,
    TimeSpan? HushLeft, bool HushUntilOff);

/// <summary>The one fact the header shows: its kind, its text (Arabic-Indic digits), and the item a click opens (null = not clickable).</summary>
public sealed record HeaderFact(string Kind, string Text, string? OpensId);

/// <summary>The header's two lines (spec §5): her line, and the single most urgent fact.</summary>
public static class MenuHeaderFact
{
    public const string Ask = "شو بدك نعمل؟", Listening = "عم اسمعك…", Hushed = "عم اسكت هلق";

    /// <summary>Order: running timer › Claude waiting › next prayer › Quran playing › hush remaining.</summary>
    public static HeaderFact? Pick(MenuFacts f, MenuHeader mode)
    {
        if (mode == MenuHeader.None) return null;
        var prayer = f.NextPrayer is { } p
            ? new HeaderFact("prayer", Digits.ToArabicIndic($"{PrayerNames.Arabic(p.Prayer)} بعد {PrayerText.Duration(p.Left)}"), "prayer")
            : null;
        if (mode == MenuHeader.Prayer) return prayer;
        if (f.TimerLeft is { } t) return new("timer", "المؤقت: باقي " + Digits.ToArabicIndic(Clock(t)), "timer");
        if (f.ClaudeWaiting) return new("claude", "Claude ناطر جوابك", null);
        if (prayer != null) return prayer;
        if (f.QuranPlaying) return new("quran", "عم نسمع قرآن", "listen");
        if (f.HushLeft is { } h) return new("hush", Digits.ToArabicIndic($"رح ارجع احكي بعد {PrayerText.Duration(h)}"), null);
        if (f.HushUntilOff) return new("hush", "ساكتة لحد ما تقلي", null);
        return null;
    }

    /// <summary>"24:10", from an hour "1:05:09" (whole seconds, rounded up).</summary>
    public static string Clock(TimeSpan t)
    {
        int s = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds) - 1e-9);
        int h = s / 3600, m = s / 60 % 60, sec = s % 60;
        return h > 0 ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, sec)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", m, sec);
    }

    /// <summary>Header line 1: listening › her reply (6 s) › hushed › the question.</summary>
    public static string Greeting(bool listening, string? reply, bool hushOn) =>
        listening ? Listening : !string.IsNullOrEmpty(reply) ? reply : hushOn ? Hushed : Ask;
}
