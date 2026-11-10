using System.Globalization;

namespace Sahar.Core.Prayer;

public enum Prayer { Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha }

/// <summary>Calculation methods (spec §7.2): Fajr angle, and Isha as an angle or minutes after Maghrib.</summary>
public enum PrayerMethod { UmmAlQura, MuslimWorldLeague, Egyptian, Karachi, NorthAmerica }

/// <summary>
/// Prayer times computed on the PC (spec §7.2), with the standard astronomical formulas (sun declination and equation
/// of time from the day number; hour angle for a depression angle; Asr by shadow length). High latitudes where the
/// twilight angle is never reached use the "angle-based" portion of the night.
/// </summary>
public static class PrayerTimes
{
    private const double RiseSetAngle = 0.833;

    private static (double Fajr, double? IshaAngle, double IshaMinutes) Params(PrayerMethod m) => m switch
    {
        PrayerMethod.UmmAlQura => (18.5, null, 90),
        PrayerMethod.MuslimWorldLeague => (18, 17, 0),
        PrayerMethod.Egyptian => (19.5, 17.5, 0),
        PrayerMethod.Karachi => (18, 18, 0),
        _ => (15, 15, 0),
    };

    public static IReadOnlyDictionary<Prayer, DateTimeOffset> For(DateOnly date, double lat, double lon, TimeSpan utcOffset, PrayerMethod m, bool hanafiAsr)
    {
        var (fajrAngle, ishaAngle, ishaMinutes) = Params(m);
        double jDate = Julian(date.Year, date.Month, date.Day) - lon / (15 * 24.0);
        double tz = utcOffset.TotalHours;

        // Day portions of first guesses, then one refinement pass with those times.
        double[] t = [5, 6, 12, 13, 18, 18, 18];
        for (int pass = 0; pass < 2; pass++)
        {
            var p = t.Select(x => x / 24).ToArray();
            t =
            [
                SunAngleTime(fajrAngle, p[0], ccw: true, jDate, lat),
                SunAngleTime(RiseSetAngle, p[1], ccw: true, jDate, lat),
                MidDay(p[2], jDate),
                AsrTime(hanafiAsr ? 2 : 1, p[3], jDate, lat),
                SunAngleTime(RiseSetAngle, p[4], ccw: false, jDate, lat),   // sunset
                SunAngleTime(RiseSetAngle, p[5], ccw: false, jDate, lat),   // maghrib = sunset
                ishaAngle is { } ia ? SunAngleTime(ia, p[6], ccw: false, jDate, lat) : double.NaN,
            ];
            // Keep NaNs out of the next pass's guesses.
            double[] guess = [5, 6, 12, 13, 18, 18, 18];
            for (int i = 0; i < t.Length; i++) if (double.IsNaN(t[i])) t[i] = guess[i];
            if (pass == 0) continue;
            // Second pass: recompute NaNs honestly for the high-latitude rule below.
            t[0] = SunAngleTime(fajrAngle, p[0], true, jDate, lat);
            t[6] = ishaAngle is { } ia2 ? SunAngleTime(ia2, p[6], false, jDate, lat) : double.NaN;
        }
        for (int i = 0; i < t.Length; i++) t[i] += tz - lon / 15;

        double sunrise = t[1], sunset = t[4], maghrib = t[5];
        bool ramadan = new UmAlQuraCalendar().GetMonth(date.ToDateTime(TimeOnly.MinValue)) == 9;
        double isha = ishaAngle is null ? maghrib + (ramadan && m == PrayerMethod.UmmAlQura ? 120 : ishaMinutes) / 60.0 : t[6];

        // High latitudes: twilight angle not reached (NaN) or too long → angle-based portion of the night.
        double night = 24 - (sunset - sunrise);
        double fajrPortion = fajrAngle / 60 * night;
        double fajr = t[0];
        if (double.IsNaN(fajr) || sunrise - fajr > fajrPortion) fajr = sunrise - fajrPortion;
        if (ishaAngle is { } a)
        {
            double ishaPortion = a / 60 * night;
            if (double.IsNaN(isha) || isha - sunset > ishaPortion) isha = sunset + ishaPortion;
        }

        var midnight = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), utcOffset);
        DateTimeOffset At(double hours) => midnight.AddMinutes(Math.Round(hours * 60));
        return new Dictionary<Prayer, DateTimeOffset>
        {
            [Prayer.Fajr] = At(fajr), [Prayer.Sunrise] = At(sunrise), [Prayer.Dhuhr] = At(t[2]),
            [Prayer.Asr] = At(t[3]), [Prayer.Maghrib] = At(maghrib), [Prayer.Isha] = At(isha),
        };
    }

    private static double Julian(int y, int m, int d)
    {
        if (m <= 2) { y -= 1; m += 12; }
        double a = Math.Floor(y / 100.0), b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
    }

    private static (double Decl, double Eqt) Sun(double jd)
    {
        double d = jd - 2451545.0;
        double g = Fix(357.529 + 0.98560028 * d, 360), q = Fix(280.459 + 0.98564736 * d, 360);
        double l = Fix(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g), 360);
        double e = 23.439 - 0.00000036 * d;
        double ra = ArcTan2(Cos(e) * Sin(l), Cos(l)) / 15;
        return (ArcSin(Sin(e) * Sin(l)), q / 15 - Fix(ra, 24));
    }

    private static double MidDay(double portion, double jDate) => Fix(12 - Sun(jDate + portion).Eqt, 24);

    private static double SunAngleTime(double angle, double portion, bool ccw, double jDate, double lat)
    {
        double decl = Sun(jDate + portion).Decl, noon = MidDay(portion, jDate);
        double x = (-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat));
        if (x is < -1 or > 1) return double.NaN;
        double t = ArcCos(x) / 15;
        return noon + (ccw ? -t : t);
    }

    private static double AsrTime(double factor, double portion, double jDate, double lat)
    {
        double decl = Sun(jDate + portion).Decl;
        double angle = -ArcCot(factor + Tan(Math.Abs(lat - decl)));
        return SunAngleTime(angle, portion, false, jDate, lat);
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;
    private static double Sin(double d) => Math.Sin(Rad(d));
    private static double Cos(double d) => Math.Cos(Rad(d));
    private static double Tan(double d) => Math.Tan(Rad(d));
    private static double ArcSin(double x) => Deg(Math.Asin(x));
    private static double ArcCos(double x) => Deg(Math.Acos(x));
    private static double ArcTan2(double y, double x) => Deg(Math.Atan2(y, x));
    private static double ArcCot(double x) => Deg(Math.Atan(1 / x));
    private static double Fix(double a, double b) { a -= b * Math.Floor(a / b); return a < 0 ? a + b : a; }
}

/// <summary>Today's and tomorrow's times for one place: the next prayer, reminders and the quiet after each adhan.</summary>
public sealed class PrayerSchedule(double lat, double lon, PrayerMethod method, bool hanafiAsr, Func<DateTimeOffset, TimeSpan> utcOffset)
{
    public static readonly TimeSpan AdhanQuiet = TimeSpan.FromMinutes(5);
    private static readonly Prayer[] Five = [Prayer.Fajr, Prayer.Dhuhr, Prayer.Asr, Prayer.Maghrib, Prayer.Isha];
    private readonly HashSet<(DateOnly, Prayer)> _reminded = [], _adhan = [];

    public IReadOnlyDictionary<Prayer, DateTimeOffset> Day(DateOnly d, DateTimeOffset now) =>
        PrayerTimes.For(d, lat, lon, utcOffset(now), method, hanafiAsr);

    private IEnumerable<(Prayer P, DateTimeOffset At, DateOnly Day)> Around(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.Date);
        foreach (var d in new[] { today.AddDays(-1), today, today.AddDays(1) })
        {
            var times = Day(d, now);
            foreach (var p in Five) yield return (p, times[p], d);
        }
    }

    public (Prayer Prayer, DateTimeOffset At)? Next(DateTimeOffset now) =>
        Around(now).Where(x => x.At > now).OrderBy(x => x.At).Select(x => ((Prayer, DateTimeOffset)?)(x.P, x.At)).FirstOrDefault();

    /// <summary>True once per prayer, from <paramref name="minutesBefore"/> before it until its time (never late).</summary>
    public bool TakeReminder(DateTimeOffset now, int minutesBefore, out Prayer prayer)
    {
        foreach (var (p, at, d) in Around(now))
            if (now >= at.AddMinutes(-minutesBefore) && now < at && _reminded.Add((d, p))) { prayer = p; return true; }
        prayer = default;
        return false;
    }

    /// <summary>True once per prayer, within the quiet minutes after its time.</summary>
    public bool TakeAdhan(DateTimeOffset now, out Prayer prayer)
    {
        foreach (var (p, at, d) in Around(now))
            if (now >= at && now < at + AdhanQuiet && _adhan.Add((d, p))) { prayer = p; return true; }
        prayer = default;
        return false;
    }

    public bool InAdhanQuiet(DateTimeOffset now) => Around(now).Any(x => now >= x.At && now < x.At + AdhanQuiet);
}

/// <summary>Prayer names as she says them (spelled «الضهر» and «العشا», as in the mockups — owner ruling 2026-11-10).</summary>
public static class PrayerNames
{
    public static string Arabic(Prayer p) => p switch
    {
        Prayer.Fajr => "الفجر", Prayer.Sunrise => "الشروق", Prayer.Dhuhr => "الضهر",
        Prayer.Asr => "العصر", Prayer.Maghrib => "المغرب", _ => "العشا",
    };

    public static string Method(PrayerMethod m) => m switch
    {
        PrayerMethod.UmmAlQura => "أم القرى", PrayerMethod.MuslimWorldLeague => "رابطة العالم الإسلامي",
        PrayerMethod.Egyptian => "الهيئة المصرية", PrayerMethod.Karachi => "كراتشي", _ => "أمريكا الشمالية",
    };
}
