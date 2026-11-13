using Sahar.Core.Activities;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Activities;

public class WeatherNowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 10, 0, TimeSpan.FromHours(3));

    private static Forecast F(Func<int, HourCast, HourCast>? change = null, double max = 30, double min = 18, int? aqi = 20)
    {
        var hours = Enumerable.Range(0, 12).Select(i =>
        {
            var h = new HourCast(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(3)).AddHours(i), 24, 1, 0, 10);
            return change is null ? h : change(i, h);
        }).ToList();
        return new Forecast(24, 1, hours, max, min, aqi, Now);
    }

    [Fact]
    public void Calm_NoAlerts() => Assert.Empty(WeatherAlerts.Find(F(), Now));

    [Theory]
    [InlineData(1, 61, true)]
    [InlineData(2, 61, true)]    // 14:00 is 1 h 50 from 12:10
    [InlineData(3, 61, false)]   // 15:00 is past the next two hours
    [InlineData(1, 3, false)]
    public void Rain_WithinTwoHours(int hour, int code, bool alert) =>
        Assert.Equal(alert, WeatherAlerts.Find(F((i, h) => i == hour ? h with { Code = code } : h), Now).Contains(WeatherAlert.Rain));

    [Theory]
    [InlineData(94, false)]
    [InlineData(95, true)]
    [InlineData(99, true)]
    public void Storm_Codes95To99(int code, bool alert)
    {
        var found = WeatherAlerts.Find(F((i, h) => i == 1 ? h with { Code = code } : h), Now);
        Assert.Equal(alert, found.Contains(WeatherAlert.Storm));
        if (alert) Assert.DoesNotContain(WeatherAlert.Rain, found);   // a storm says it all
    }

    [Theory]
    [InlineData(59.9, 1, false)]
    [InlineData(60, 1, true)]
    [InlineData(60, 3, true)]    // 15:00 is within three hours of 12:10
    [InlineData(60, 4, false)]
    public void Wind_GustsWithinThreeHours(double gust, int hour, bool alert) =>
        Assert.Equal(alert, WeatherAlerts.Find(F((i, h) => i == hour ? h with { Gusts = gust } : h), Now).Contains(WeatherAlert.Wind));

    [Theory]
    [InlineData(37.9, false)]
    [InlineData(38, true)]
    public void Heat_TodaysMax(double max, bool alert) => Assert.Equal(alert, WeatherAlerts.Find(F(max: max), Now).Contains(WeatherAlert.Heat));

    [Theory]
    [InlineData(-0.9, 5, false)]
    [InlineData(-1, 5, true)]
    [InlineData(-1, 11, true)]
    public void Frost_WithinTwelveHours(double t, int hour, bool alert) =>
        Assert.Equal(alert, WeatherAlerts.Find(F((i, h) => i == hour ? h with { Temp = t } : h), Now).Contains(WeatherAlert.Frost));

    [Theory]
    [InlineData(79, false)]
    [InlineData(80, true)]
    [InlineData(null, false)]
    public void Air_Aqi80(int? aqi, bool alert) => Assert.Equal(alert, WeatherAlerts.Find(F(aqi: aqi), Now).Contains(WeatherAlert.Air));

    [Fact]
    public void Latch_OncePerSpell_RearmsAfterAnHourCalm()
    {
        var latch = new AlertLatch();
        Assert.Equal([WeatherAlert.Rain], latch.Fire([WeatherAlert.Rain], Now));
        Assert.Empty(latch.Fire([WeatherAlert.Rain], Now.AddHours(1)));
        Assert.Empty(latch.Fire([], Now.AddHours(1.5)));
        Assert.Empty(latch.Fire([WeatherAlert.Rain], Now.AddHours(2.2)));   // calm only 0.7 h: the same spell
        Assert.Empty(latch.Fire([], Now.AddHours(2.5)));
        Assert.Equal([WeatherAlert.Rain], latch.Fire([WeatherAlert.Rain], Now.AddHours(3.6)));   // 1.1 h calm: a new spell
    }

    [Fact]
    public void City_WeatherCityWins()
    {
        var s = new SaharSettings { WeatherCity = "حلب", WeatherLatitude = 36.2021, WeatherLongitude = 37.1343, PrayerCity = "دمشق", PrayerLatitude = 33.5, PrayerLongitude = 36.3 };
        var p = WeatherCity.Resolve(s)!;
        Assert.Equal(("حلب", false), (p.Label, p.FromPrayer));
        Assert.Equal((36.2, 37.13), (p.Lat, p.Lon));
    }

    [Fact]
    public void City_FallsBackToPrayer_LabelSaysSo()
    {
        var p = WeatherCity.Resolve(new SaharSettings { PrayerCity = "دمشق", PrayerLatitude = 33.5138, PrayerLongitude = 36.2765 })!;
        Assert.True(p.FromPrayer);
        Assert.Equal("نفس مدينة الصلاة: دمشق", ActivityText.WeatherCityLabel(p));
        Assert.Equal((33.51, 36.28), (p.Lat, p.Lon));
    }

    [Fact]
    public void City_WindowsLocationLabel()
    {
        var p = WeatherCity.Resolve(new SaharSettings { PrayerCity = SettingsBridge.WindowsLocation, PrayerLatitude = 24.7, PrayerLongitude = 46.7 })!;
        Assert.Equal("نفس مدينة الصلاة: موقع الويندوز", ActivityText.WeatherCityLabel(p));
    }

    [Fact]
    public void City_EmptyWeatherCityName_FallsBack() =>
        Assert.True(WeatherCity.Resolve(new SaharSettings { WeatherCity = "  ", PrayerCity = "دمشق", PrayerLatitude = 33.5, PrayerLongitude = 36.3 })!.FromPrayer);

    [Fact]
    public void City_NoneWithoutEither() => Assert.Null(WeatherCity.Resolve(new SaharSettings()));

    [Theory]
    [InlineData(33.5138, 33.51)]
    [InlineData(-0.005, -0.01)]
    [InlineData(46.715, 46.72)]
    public void City_RoundedToHundredth(double deg, double expected) => Assert.Equal(expected, WeatherCity.Round(deg));

    [Fact]
    public void Today_TempAndSky() => Assert.Equal("٢٤° صحو", ActivityText.WeatherToday(F() with { CodeNow = 0 }));
}
