using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// Rain coming (spec §5 #7, §8.1), only when you turn it on: your city goes to Open-Meteo's geocoding once, then
/// only its coordinates, at most once an hour. Test mode reads geocode.json / forecast.json from a folder instead.
/// </summary>
internal sealed class Weather : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string? _stub;
    private readonly Action<EventKind> _post;
    private readonly DispatcherQueue _ui;
    private DateTimeOffset? _lastFetch, _lastRain;

    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public bool On => Latitude is not null && Longitude is not null;

    public Weather(DispatcherQueue ui, Action<EventKind> post, string? stubDir, double? lat, double? lon)
    {
        _ui = ui;
        _post = post;
        _stub = stubDir;
        (Latitude, Longitude) = (lat, lon);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sahar/1.0 (desktop companion)");
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromMinutes(10);
        t.Tick += (_, _) => _ = CheckAsync();
        t.Start();
    }

    public void TurnOff()
    {
        Latitude = Longitude = null;
        Log.Write("weather: off");
    }

    /// <summary>Finds a city; null if Open-Meteo doesn't know it or can't be reached (the error says which).</summary>
    public async Task<(GeoResult? Place, string? Error)> FindAsync(string city)
    {
        try
        {
            var json = _stub is not null
                ? await File.ReadAllTextAsync(Path.Combine(_stub, "geocode.json"))
                : await _http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=1&language=ar&name=" + Uri.EscapeDataString(city.Trim()));
            var r = JsonSerializer.Deserialize(json, WeatherJson.Default.GeoResponse);
            return r?.Results is { Count: > 0 } list ? (list[0], null) : (null, "ما لقيت هالمدينة");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Log.Write($"weather: city lookup failed ({e.GetType().Name})");
            return (null, "ما في نت هلق، جرّب بعدين");
        }
    }

    public void Use(GeoResult place)
    {
        (Latitude, Longitude) = (place.Latitude, place.Longitude);
        _lastFetch = null;
        Log.Write("weather: on");
        _ = CheckAsync();
    }

    public async Task CheckAsync()
    {
        try
        {
            if (!On || !WeatherRules.ShouldFetch(DateTimeOffset.Now, _lastFetch)) return;
            _lastFetch = DateTimeOffset.Now;
            var json = _stub is not null
                ? await File.ReadAllTextAsync(Path.Combine(_stub, "forecast.json"))
                : await _http.GetStringAsync(string.Create(CultureInfo.InvariantCulture,
                    $"https://api.open-meteo.com/v1/forecast?latitude={Latitude:0.###}&longitude={Longitude:0.###}&hourly=weather_code,precipitation_probability&forecast_hours=2&timezone=auto"));
            var f = JsonSerializer.Deserialize(json, WeatherJson.Default.ForecastResponse);
            if (f?.Hourly is not { } h) return;
            if (!WeatherRules.RainSoon(h.WeatherCode ?? [], (h.PrecipitationProbability ?? []).Select(p => p ?? 0).ToList())) return;
            if (_lastRain is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(3)) return;
            _lastRain = DateTimeOffset.Now;
            Log.Write("weather: rain coming");
            _ui.TryEnqueue(() => _post(EventKind.WeatherRain));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Log.Write($"weather: forecast failed ({e.GetType().Name})");
        }
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record GeoResult(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude);

internal sealed record GeoResponse([property: JsonPropertyName("results")] List<GeoResult>? Results);

internal sealed record Hourly(
    [property: JsonPropertyName("weather_code")] List<int>? WeatherCode,
    [property: JsonPropertyName("precipitation_probability")] List<int?>? PrecipitationProbability);

internal sealed record ForecastResponse([property: JsonPropertyName("hourly")] Hourly? Hourly);

[JsonSerializable(typeof(GeoResponse))]
[JsonSerializable(typeof(ForecastResponse))]
internal sealed partial class WeatherJson : JsonSerializerContext;
