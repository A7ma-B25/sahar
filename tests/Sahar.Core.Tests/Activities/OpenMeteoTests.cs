using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class OpenMeteoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 14, 20, 0, TimeSpan.FromHours(3));

    private const string Full = """
    {"utc_offset_seconds":10800,"current":{"temperature_2m":24.4,"weather_code":0},
     "hourly":{"time":["2026-11-13T14:00","2026-11-13T15:00","2026-11-13T16:00"],"temperature_2m":[24.4,25,22],"weather_code":[0,1,61],
               "precipitation_probability":[0,10,80],"wind_gusts_10m":[10,20,64]},
     "daily":{"temperature_2m_max":[26.2],"temperature_2m_min":[12.1]}}
    """;

    [Fact]
    public void Parse_Full()
    {
        var f = OpenMeteo.Parse(Full, """{"hourly":{"european_aqi":[83]}}""", Now)!;
        Assert.Equal((24.4, 0, 26.2, 12.1, (int?)83), (f.TempNow, f.CodeNow, f.TodayMax, f.TodayMin, f.Aqi));
        Assert.Equal(3, f.Hours.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 16, 0, 0, TimeSpan.FromHours(3)), f.Hours[2].Time);
        Assert.Equal((22.0, 61, 80, 64.0), (f.Hours[2].Temp, f.Hours[2].Code, f.Hours[2].RainChance, f.Hours[2].Gusts));
    }

    [Fact]
    public void Parse_TheOldStub_HoursFromNow()
    {
        // 0.12's test stub (and any answer without current/daily): the hours count from the current hour.
        var f = OpenMeteo.Parse("""{"hourly":{"time":["2026-11-06T20:00","2026-11-06T21:00"],"weather_code":[61,63],"precipitation_probability":[80,90]}}""", null, Now)!;
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 14, 0, 0, TimeSpan.FromHours(3)), f.Hours[0].Time);
        Assert.Contains(WeatherAlert.Rain, WeatherAlerts.Find(f, Now));
        Assert.Null(f.Aqi);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{oops")]
    [InlineData("""{"hourly":{}}""")]
    public void Parse_Broken_Null(string json) => Assert.Null(OpenMeteo.Parse(json, null, Now));

    [Fact]
    public void Parse_BrokenAir_StillAForecast() => Assert.Null(OpenMeteo.Parse(Full, "{nope", Now)!.Aqi);

    [Fact]
    public void Urls_RoundedCoordinates()
    {
        Assert.Contains("latitude=33.51&longitude=36.28", OpenMeteo.ForecastUrl(33.5138, 36.2765));
        Assert.Contains("forecast_hours=12", OpenMeteo.ForecastUrl(1, 2));
        Assert.StartsWith("https://air-quality-api.open-meteo.com/v1/air-quality?latitude=33.51&longitude=36.28", OpenMeteo.AirUrl(33.5138, 36.2765));
    }
}
