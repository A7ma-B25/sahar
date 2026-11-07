using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Xunit;
using P = Sahar.Core.Prayer.Prayer;

namespace Sahar.Core.Tests.Prayer;

public class PrayerTextTests
{
    [Theory]
    [InlineData(0.5, "هلق")]
    [InlineData(1, "دقيقة")]
    [InlineData(2, "دقيقتين")]
    [InlineData(10, "10 دقايق")]
    [InlineData(12, "12 دقيقة")]
    [InlineData(60, "ساعة")]
    [InlineData(72, "ساعة و12 دقيقة")]
    [InlineData(122, "ساعتين ودقيقتين")]
    [InlineData(185, "3 ساعات و5 دقايق")]
    [InlineData(9.2, "10 دقايق")]   // a part of a minute counts as a whole one: never "after 0"
    public void Duration_SyrianArabic(double minutes, string expected) =>
        Assert.Equal(expected, PrayerText.Duration(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void NextLine() =>
        Assert.Equal("الجاية: العصر بعد ساعة و12 دقيقة", PrayerText.NextLine(P.Asr, TimeSpan.FromMinutes(72)));

    [Fact]
    public void ReminderLine() => Assert.Equal("بعد 10 دقايق العصر", PrayerText.Reminder(P.Asr, 10));

    [Fact]
    public void TimeLine_UsesTheTimesOwnClock() =>
        Assert.Equal("الفجر — 05:01", PrayerText.TimeLine(P.Fajr, new DateTimeOffset(2026, 11, 7, 5, 1, 40, TimeSpan.FromHours(3))));

    [Fact]
    public void CityAndMethodLines()
    {
        Assert.Equal("المدينة: دمشق — غيّر", PrayerText.CityLine("دمشق"));
        Assert.Equal("طريقة الحساب: أم القرى", PrayerText.MethodLine(PrayerMethod.UmmAlQura));
    }

    [Fact]
    public void Settings_PrayerDefaultsAndRoundTrip()
    {
        var d = new SaharSettings();
        Assert.Null(d.PrayerCity);
        Assert.Equal(PrayerMethod.UmmAlQura, d.PrayerMethod);
        Assert.Equal(10, d.PrayerReminderMinutes);
        var path = Path.Combine(Path.GetTempPath(), $"sahar-prayer-{Guid.NewGuid():N}.json");
        try
        {
            SettingsStore.Save(path, d with { PrayerCity = "Damascus", PrayerLatitude = 33.51, PrayerLongitude = 36.29, PrayerMethod = PrayerMethod.Egyptian });
            var back = SettingsStore.Load(path);
            Assert.Equal("Damascus", back.PrayerCity);
            Assert.Equal(33.51, back.PrayerLatitude);
            Assert.Equal(PrayerMethod.Egyptian, back.PrayerMethod);
            Assert.Contains("\"prayerMethod\": \"egyptian\"", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }
}
