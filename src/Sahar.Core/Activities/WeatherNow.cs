using Sahar.Core.Senses;
using Sahar.Core.Settings;

namespace Sahar.Core.Activities;

/// <summary>One forecast hour (Open-Meteo hourly).</summary>
public sealed record HourCast(DateTimeOffset Time, double Temp, int Code, int RainChance, double Gusts);

/// <summary>What Open-Meteo said (spec 0.13 §2 Weather): now, the next 12 hours, today's range, air quality.</summary>
public sealed record Forecast(double TempNow, int CodeNow, IReadOnlyList<HourCast> Hours, double TodayMax, double TodayMin, int? Aqi, DateTimeOffset At);

/// <summary>The alert thresholds (spec 0.13 §2): rain (the old <see cref="WeatherRules.RainSoon"/>) or a storm (codes
/// 95–99) within 2 h, gusts ≥ 60 km/h within 3 h, today's max ≥ 38 °C, ≤ −1 °C within 12 h, AQI ≥ 80.</summary>
public static class WeatherAlerts
{
    public const double GustKmh = 60, HeatC = 38, FrostC = -1;
    public const int AqiBad = 80;

    public static IReadOnlyList<WeatherAlert> Find(Forecast f, DateTimeOffset now)
    {
        IEnumerable<HourCast> Within(double hours) => f.Hours.Where(h => h.Time < now.AddHours(hours) && h.Time.AddHours(1) > now);
        var list = new List<WeatherAlert>();
        var two = Within(2).ToList();
        bool storm = two.Any(h => h.Code is >= 95 and <= 99);
        if (storm) list.Add(WeatherAlert.Storm);
        else if (two.Any(h => WeatherRules.RainSoon([h.Code], [h.RainChance]))) list.Add(WeatherAlert.Rain);
        if (Within(3).Any(h => h.Gusts >= GustKmh)) list.Add(WeatherAlert.Wind);
        if (f.TodayMax >= HeatC) list.Add(WeatherAlert.Heat);
        if (Within(12).Any(h => h.Temp <= FrostC)) list.Add(WeatherAlert.Frost);
        if (f.Aqi >= AqiBad) list.Add(WeatherAlert.Air);
        return list;
    }
}

/// <summary>Each alert fires once per spell; a spell ends after an hour without that alert.</summary>
public sealed class AlertLatch
{
    public static readonly TimeSpan Calm = TimeSpan.FromHours(1);
    /// <summary>Per alert seen in this spell: since when it has been calm (null while it is still there). Calm counts
    /// from the first forecast without it, so an hourly fetch never repeats an alert by a gap between fetches.</summary>
    private readonly Dictionary<WeatherAlert, DateTimeOffset?> _spell = [];

    public IReadOnlyList<WeatherAlert> Fire(IReadOnlyList<WeatherAlert> now, DateTimeOffset t)
    {
        var fired = new List<WeatherAlert>();
        foreach (var a in now)
        {
            if (!_spell.TryGetValue(a, out var calm) || calm is { } since && t - since >= Calm) fired.Add(a);
            _spell[a] = null;
        }
        foreach (var a in _spell.Keys.Where(k => !now.Contains(k) && _spell[k] is null).ToList()) _spell[a] = t;
        return fired;
    }
}

/// <summary>Where the weather is for (owner ruling 4: it falls back to the prayer place).</summary>
public sealed record WeatherPlace(string Label, double Lat, double Lon, bool FromPrayer);

public static class WeatherCity
{
    /// <summary>The weather's own city wins; else the prayer place (the weather flow never writes it); else none.
    /// Coordinates go out rounded to 0.01° (about 1 km).</summary>
    public static WeatherPlace? Resolve(SaharSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.WeatherCity) && s.WeatherLatitude is { } lat && s.WeatherLongitude is { } lon)
            return new WeatherPlace(s.WeatherCity.Trim(), Round(lat), Round(lon), false);
        if (s.PrayerLatitude is { } plat && s.PrayerLongitude is { } plon)
            return new WeatherPlace(string.IsNullOrWhiteSpace(s.PrayerCity) ? SettingsBridge.WindowsLocation : s.PrayerCity.Trim(), Round(plat), Round(plon), true);
        return null;
    }

    public static double Round(double deg) => Math.Round(deg, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Open-Meteo's answers → <see cref="Forecast"/> (spec 0.13 §2 Weather). The hours count from the current
/// hour (the request asks for the next 12), so a stale or test answer still lines up with now.</summary>
public static class OpenMeteo
{
    public static string ForecastUrl(double lat, double lon) => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"https://api.open-meteo.com/v1/forecast?latitude={WeatherCity.Round(lat)}&longitude={WeatherCity.Round(lon)}&current=temperature_2m,weather_code&hourly=temperature_2m,weather_code,precipitation_probability,wind_gusts_10m&daily=temperature_2m_max,temperature_2m_min&forecast_hours=12&forecast_days=1&timezone=auto");

    public static string AirUrl(double lat, double lon) => string.Create(System.Globalization.CultureInfo.InvariantCulture,
        $"https://air-quality-api.open-meteo.com/v1/air-quality?latitude={WeatherCity.Round(lat)}&longitude={WeatherCity.Round(lon)}&hourly=european_aqi&forecast_hours=1&timezone=auto");

    public static Forecast? Parse(string forecastJson, string? airJson, DateTimeOffset now)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(forecastJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("hourly", out var hourly) || !hourly.TryGetProperty("weather_code", out var codes)) return null;
            static double[] Nums(System.Text.Json.JsonElement parent, string name) =>
                parent.TryGetProperty(name, out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? a.EnumerateArray().Select(e => e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetDouble() : 0).ToArray() : [];
            var code = Nums(hourly, "weather_code");
            var temp = Nums(hourly, "temperature_2m");
            var rain = Nums(hourly, "precipitation_probability");
            var gust = Nums(hourly, "wind_gusts_10m");
            var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
            var hours = code.Select((c, i) => new HourCast(start.AddHours(i), i < temp.Length ? temp[i] : 0, (int)c,
                i < rain.Length ? (int)rain[i] : 0, i < gust.Length ? gust[i] : 0)).ToList();
            if (hours.Count == 0) return null;
            double tempNow = hours[0].Temp;
            int codeNow = hours[0].Code;
            if (root.TryGetProperty("current", out var cur))
            {
                if (cur.TryGetProperty("temperature_2m", out var t) && t.TryGetDouble(out var tv)) tempNow = tv;
                if (cur.TryGetProperty("weather_code", out var wc) && wc.TryGetInt32(out var cv)) codeNow = cv;
            }
            double max = hours.Max(h => h.Temp), min = hours.Min(h => h.Temp);
            if (root.TryGetProperty("daily", out var daily))
            {
                if (Nums(daily, "temperature_2m_max") is [var mx, ..]) max = mx;
                if (Nums(daily, "temperature_2m_min") is [var mn, ..]) min = mn;
            }
            return new Forecast(tempNow, codeNow, hours, max, min, Aqi(airJson), now);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static int? Aqi(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("hourly", out var h) && h.TryGetProperty("european_aqi", out var a)
                && a.ValueKind == System.Text.Json.JsonValueKind.Array && a.GetArrayLength() > 0 && a[0].TryGetDouble(out var v) ? (int)Math.Round(v) : null;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }
}
