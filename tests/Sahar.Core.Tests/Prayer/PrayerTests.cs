using Sahar.Core.Prayer;

namespace Sahar.Core.Tests.Prayer;

public class PrayerTests
{
    private static readonly TimeSpan Saudi = TimeSpan.FromHours(3);

    private static void Near(DateTimeOffset actual, int h, int m, string what)
    {
        var expected = new TimeSpan(h, m, 0);
        var got = actual.TimeOfDay;
        Assert.True(Math.Abs((got - expected).TotalMinutes) <= 3, $"{what}: {got:hh\\:mm} vs {expected:hh\\:mm}");
    }

    [Fact]
    public void UmmAlQura_Makkah_2025_07_05()
    {
        // eSalah, Makkah (21.415114, 39.878162), Umm al-Qura, standard Asr.
        var t = PrayerTimes.For(new DateOnly(2025, 7, 5), 21.415114, 39.878162, Saudi, PrayerMethod.UmmAlQura, hanafiAsr: false);
        Near(t[Sahar.Core.Prayer.Prayer.Fajr], 4, 16, "Fajr");
        Near(t[Sahar.Core.Prayer.Prayer.Sunrise], 5, 43, "Sunrise");
        Near(t[Sahar.Core.Prayer.Prayer.Dhuhr], 12, 25, "Dhuhr");
        Near(t[Sahar.Core.Prayer.Prayer.Asr], 15, 43, "Asr");
        Near(t[Sahar.Core.Prayer.Prayer.Maghrib], 19, 7, "Maghrib");
        Near(t[Sahar.Core.Prayer.Prayer.Isha], 20, 37, "Isha");
    }

    [Fact]
    public void UmmAlQura_Makkah_2026_01_01()
    {
        // pray.zone, Makkah al Mukarramah, Umm al-Qura.
        var t = PrayerTimes.For(new DateOnly(2026, 1, 1), 21.4225, 39.8262, Saudi, PrayerMethod.UmmAlQura, hanafiAsr: false);
        Near(t[Sahar.Core.Prayer.Prayer.Fajr], 5, 37, "Fajr");
        Near(t[Sahar.Core.Prayer.Prayer.Sunrise], 6, 59, "Sunrise");
        Near(t[Sahar.Core.Prayer.Prayer.Dhuhr], 12, 24, "Dhuhr");
        Near(t[Sahar.Core.Prayer.Prayer.Asr], 15, 29, "Asr");
        Near(t[Sahar.Core.Prayer.Prayer.Maghrib], 17, 50, "Maghrib");
        Near(t[Sahar.Core.Prayer.Prayer.Isha], 19, 20, "Isha");
    }

    [Fact]
    public void MWL_Damascus_SensibleOrder()
    {
        var t = PrayerTimes.For(new DateOnly(2026, 11, 7), 33.5138, 36.2765, Saudi, PrayerMethod.MuslimWorldLeague, false);
        var order = Enum.GetValues<Sahar.Core.Prayer.Prayer>().Select(p => t[p]).ToList();
        for (int i = 1; i < order.Count; i++) Assert.True(order[i] > order[i - 1]);
        Near(t[Sahar.Core.Prayer.Prayer.Dhuhr], 12, 19, "Dhuhr");   // solar noon: 12:00 + 3 h − 36.28°/15 − equation of time (~16 min) ≈ 12:19
    }

    [Fact]
    public void Hanafi_AsrLater()
    {
        var d = new DateOnly(2026, 11, 7);
        var s = PrayerTimes.For(d, 33.5, 36.3, Saudi, PrayerMethod.MuslimWorldLeague, false);
        var h = PrayerTimes.For(d, 33.5, 36.3, Saudi, PrayerMethod.MuslimWorldLeague, true);
        Assert.True(h[Sahar.Core.Prayer.Prayer.Asr] - s[Sahar.Core.Prayer.Prayer.Asr] > TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void UmmAlQura_RamadanIsha120()
    {
        // 1 Ramadan 1447 ≈ 18 February 2026.
        var t = PrayerTimes.For(new DateOnly(2026, 2, 21), 21.4225, 39.8262, Saudi, PrayerMethod.UmmAlQura, false);
        Assert.Equal(TimeSpan.FromMinutes(120), t[Sahar.Core.Prayer.Prayer.Isha] - t[Sahar.Core.Prayer.Prayer.Maghrib]);
        var u = PrayerTimes.For(new DateOnly(2026, 4, 1), 21.4225, 39.8262, Saudi, PrayerMethod.UmmAlQura, false);
        Assert.Equal(TimeSpan.FromMinutes(90), u[Sahar.Core.Prayer.Prayer.Isha] - u[Sahar.Core.Prayer.Prayer.Maghrib]);
    }

    [Fact]
    public void PrayerTimes_HighLatitude_NoNaN()
    {
        var t = PrayerTimes.For(new DateOnly(2026, 6, 21), 59.91, 10.75, TimeSpan.FromHours(2), PrayerMethod.MuslimWorldLeague, false);
        Assert.Equal(6, t.Count);
        var order = Enum.GetValues<Sahar.Core.Prayer.Prayer>().Select(p => t[p]).ToList();
        for (int i = 1; i < order.Count; i++) Assert.True(order[i] > order[i - 1], $"{(Sahar.Core.Prayer.Prayer)i} not after the one before");
    }

    private static PrayerSchedule Damascus() => new(33.5138, 36.2765, PrayerMethod.MuslimWorldLeague, false, _ => Saudi);

    [Fact]
    public void NextPrayer_AfterIsha_IsTomorrowsFajr()
    {
        var s = Damascus();
        var late = new DateTimeOffset(2026, 11, 7, 23, 30, 0, Saudi);
        var next = s.Next(late)!.Value;
        Assert.Equal(Sahar.Core.Prayer.Prayer.Fajr, next.Prayer);
        Assert.Equal(new DateOnly(2026, 11, 8), DateOnly.FromDateTime(next.At.Date));
        var noon = new DateTimeOffset(2026, 11, 7, 12, 30, 0, Saudi);
        Assert.Equal(Sahar.Core.Prayer.Prayer.Asr, s.Next(noon)!.Value.Prayer);
    }

    [Fact]
    public void PrayerReminders_OncePerPrayer()
    {
        var s = Damascus();
        var asr = PrayerTimes.For(new DateOnly(2026, 11, 7), 33.5138, 36.2765, Saudi, PrayerMethod.MuslimWorldLeague, false)[Sahar.Core.Prayer.Prayer.Asr];
        Assert.False(s.TakeReminder(asr.AddMinutes(-15), 10, out _));
        Assert.True(s.TakeReminder(asr.AddMinutes(-10), 10, out var p));
        Assert.Equal(Sahar.Core.Prayer.Prayer.Asr, p);
        Assert.False(s.TakeReminder(asr.AddMinutes(-9), 10, out _));
        Assert.False(s.TakeReminder(asr.AddMinutes(-3), 10, out _));
    }

    [Fact]
    public void PrayerReminders_AfterSleepPastTheTime_NotLate()
    {
        var s = Damascus();
        var asr = PrayerTimes.For(new DateOnly(2026, 11, 7), 33.5138, 36.2765, Saudi, PrayerMethod.MuslimWorldLeague, false)[Sahar.Core.Prayer.Prayer.Asr];
        Assert.False(s.TakeReminder(asr.AddMinutes(20), 10, out _));   // the prayer time already passed: no late reminder
    }

    [Fact]
    public void Adhan_QuietFiveMinutes()
    {
        var s = Damascus();
        var dhuhr = PrayerTimes.For(new DateOnly(2026, 11, 7), 33.5138, 36.2765, Saudi, PrayerMethod.MuslimWorldLeague, false)[Sahar.Core.Prayer.Prayer.Dhuhr];
        Assert.False(s.InAdhanQuiet(dhuhr.AddMinutes(-1)));
        Assert.True(s.InAdhanQuiet(dhuhr.AddMinutes(2)));
        Assert.False(s.InAdhanQuiet(dhuhr.AddMinutes(6)));
        Assert.True(s.TakeAdhan(dhuhr.AddMinutes(1), out var p));
        Assert.Equal(Sahar.Core.Prayer.Prayer.Dhuhr, p);
        Assert.False(s.TakeAdhan(dhuhr.AddMinutes(2), out _));
    }

    [Fact]
    public void ArabicNames() => Assert.Equal("العصر", PrayerNames.Arabic(Sahar.Core.Prayer.Prayer.Asr));
}
