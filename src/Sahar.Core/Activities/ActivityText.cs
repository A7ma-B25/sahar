using System.Globalization;
using Sahar.Core.Text;

namespace Sahar.Core.Activities;

/// <summary>Everything her tag, cards and bubble say about activities (Syrian Arabic, Arabic-Indic digits, as the
/// approved mockups ActRest and ActCards).</summary>
public static class ActivityText
{
    public const string AskTag = "Claude ناطر جوابك";
    public const string AskBubble = "Claude ناطر جوابك 👀";
    public const string AskWhere = "جاوبه من نافذة Claude Code.";
    public const string Screenshot = "لقطة جديدة";
    public const string CopyDone = "خلص النسخ";
    public const string Copying = "عم انسخ";
    public const string Volume = "الصوت";
    public const string Charging = "عم يشحن";
    public const string Away = "وإنت غايب";
    public const string Today = "اليوم";
    public const string Music = "الموسيقى";
    public const string Paused = "واقفة";
    public const string Camera = "الكاميرا شغّالة";
    public const string Mic = "المايك شغّال";
    public const string ClaudeDone = "Claude خلّص";

    public static string N(long n) => Digits.ToArabicIndic(n.ToString(CultureInfo.InvariantCulture));

    public static string Percent(double p) => N((long)Math.Round(p)) + "٪";

    /// <summary>«٤ د», «١ س ٣٥ د», «٢ س»; under a minute «أقل من دقيقة».</summary>
    public static string Duration(TimeSpan t)
    {
        long m = (long)Math.Floor(t.TotalMinutes);
        if (m < 1) return "أقل من دقيقة";
        long h = m / 60, rest = m % 60;
        return h == 0 ? $"{N(m)} د" : rest == 0 ? $"{N(h)} س" : $"{N(h)} س {N(rest)} د";
    }

    /// <summary>«دقيقة», «٤ دقايق», «ساعة و١٠ دقايق», «٣ ساعات و٢٠ دقيقة» (the card headers).</summary>
    public static string Long(TimeSpan t)
    {
        long m = Math.Max(0, (long)Math.Floor(t.TotalMinutes)), h = m / 60, rest = m % 60;
        static string Minutes(long n) => n switch { 1 => "دقيقة", 2 => "دقيقتين", <= 10 => $"{N(n)} دقايق", _ => $"{N(n)} دقيقة" };
        static string Hours(long n) => n switch { 1 => "ساعة", 2 => "ساعتين", <= 10 => $"{N(n)} ساعات", _ => $"{N(n)} ساعة" };
        if (h == 0) return m == 0 ? "أقل من دقيقة" : Minutes(m);
        return rest == 0 ? Hours(h) : $"{Hours(h)} و{Minutes(rest)}";
    }

    /// <summary>A time of day on a 12-hour clock («٣:٢٢»).</summary>
    public static string Clock(DateTimeOffset t) => N(t.Hour % 12 == 0 ? 12 : t.Hour % 12) + ":" + N(t.Minute).PadLeft(2, '٠');

    /// <summary>An hour in the forecast row («٣م», «٩ص»).</summary>
    public static string Hour(DateTimeOffset t) => N(t.Hour % 12 == 0 ? 12 : t.Hour % 12) + (t.Hour < 12 ? "ص" : "م");

    /// <summary>A song position («١:٢٤»; over an hour «١:٠١:٤٠»).</summary>
    public static string SongTime(TimeSpan t)
    {
        long s = Math.Max(0, (long)t.TotalSeconds);
        return s >= 3600 ? $"{N(s / 3600)}:{N(s / 60 % 60).PadLeft(2, '٠')}:{N(s % 60).PadLeft(2, '٠')}" : $"{N(s / 60)}:{N(s % 60).PadLeft(2, '٠')}";
    }

    public static string Shelf(int n) => n switch
    {
        1 => "معي شغلة",
        2 => "معي شغلتين",
        <= 10 => $"معي {N(n)} شغلات",
        _ => $"معي {N(n)} شغلة",
    };

    public static string ClaudeStep(ClaudeStep step) => step switch
    {
        Activities.ClaudeStep.Editing => "Claude عم يعدّل",
        Activities.ClaudeStep.Reading => "Claude عم يقرا",
        Activities.ClaudeStep.Running => "Claude عم يشغّل أمر",
        Activities.ClaudeStep.Searching => "Claude عم يدوّر",
        Activities.ClaudeStep.Web => "Claude عم يدوّر عالنت",
        _ => "Claude عم يشتغل",
    };

    public static string AlertTag(WeatherAlert a) => a switch
    {
        WeatherAlert.Rain => "مطر بعد ساعة",
        WeatherAlert.Storm => "عاصفة جاية",
        WeatherAlert.Wind => "ريح قوية جاية",
        WeatherAlert.Heat => "حر قوي اليوم",
        WeatherAlert.Frost => "صقيع الليلة",
        _ => "الهوا مو نضيف",
    };

    public static string AlertBubble(WeatherAlert a) => a switch
    {
        WeatherAlert.Rain => "رح تشتي بعد ساعة تقريباً. خود شمسية!",
        WeatherAlert.Storm => "في عاصفة جاية، خليك جوّا إذا فيك.",
        WeatherAlert.Wind => "ريح قوية جاية، سكّر الشبابيك.",
        WeatherAlert.Heat => "اليوم حر كتير، اشرب مي!",
        WeatherAlert.Frost => "رح تبرد كتير الليلة، تدفّى.",
        _ => "الهوا مو نضيف اليوم، خفّف الطلعات.",
    };

    /// <summary>The card's line under an alert («لحد ٦٥ كم/س، سكّر الشبابيك»).</summary>
    public static string AlertDetail(WeatherAlert a, Forecast f) => a switch
    {
        WeatherAlert.Wind => $"لحد {N((long)Math.Round(f.Hours.Take(3).Max(h => h.Gusts)))} كم/س، سكّر الشبابيك",
        WeatherAlert.Heat => $"لحد {N((long)Math.Round(f.TodayMax))}°، اشرب مي",
        WeatherAlert.Frost => $"لتحت {N((long)Math.Round(f.Hours.Min(h => h.Temp)))}°",
        WeatherAlert.Air => $"مؤشر الهوا {N(f.Aqi ?? 0)}",
        _ => "خود شمسية",
    };

    /// <summary>The sky in two words from a WMO code.</summary>
    public static string Sky(int code) => code switch
    {
        0 => "صحو",
        1 or 2 => "شوية غيم",
        3 => "غايم",
        45 or 48 => "ضباب",
        >= 51 and <= 67 => "مطر",
        >= 71 and <= 77 => "تلج",
        >= 80 and <= 86 => "زخّات",
        >= 95 and <= 99 => "عاصفة",
        _ => "",
    };

    public static string Degrees(double c) => (c < 0 ? "-" : "") + N((long)Math.Abs(Math.Round(c))) + "°";

    /// <summary>The resting tag «٢٤° صحو».</summary>
    public static string WeatherToday(Forecast f) => $"{Degrees(f.TempNow)} {Sky(f.CodeNow)}".Trim();

    public static string WeatherCityLabel(WeatherPlace p) => p.FromPrayer ? $"نفس مدينة الصلاة: {p.Label}" : p.Label;

    public static string AwayBubble(int count) => $"أهلين! صار {N(count)} شغلات وإنت غايب.";

    public static string AwayRow(ActivityKind k, int count) => k switch
    {
        ActivityKind.ClaudeDone => count == 1 ? "Claude خلّص المهمة" : $"Claude خلّص {N(count)} مهمات",
        ActivityKind.CopyDone => "خلص النسخ",
        ActivityKind.WeatherAlert => "تحذير طقس",
        ActivityKind.Charging => "الشحن صار ١٠٠٪",
        ActivityKind.BatteryLow => "البطارية ضعفت",
        ActivityKind.Screenshot => count == 1 ? "لقطة جديدة" : $"{N(count)} لقطات جديدة",
        ActivityKind.ClaudeAsk => "Claude سألك",
        _ => "صار شي",
    };

    public static string More(int n) => $"و{N(n)} كمان";

    /// <summary>During a call (camera or mic in use) the screen may be shared: the kind only, no titles, commands or
    /// file names (spec 0.13 §3).</summary>
    public static Activity ForCall(Activity a) => a.Kind switch
    {
        ActivityKind.Music or ActivityKind.MusicPaused => a with { Text = Music, Meta = null },
        ActivityKind.ClaudeWorking => a with { Text = ClaudeStep(Activities.ClaudeStep.None) },
        _ => a,
    };
}
