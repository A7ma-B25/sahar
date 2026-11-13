using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// The weather (spec §5 #7, §8.1; 0.13 §2), only when you turn it on: your city goes to Open-Meteo's geocoding once,
/// then only its coordinates (rounded to 0.01°), at most once an hour — the forecast and the air quality. Test mode
/// reads geocode.json / forecast.json / air.json from a folder instead.
/// </summary>
internal sealed class Weather : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly string? _stub;
    private readonly DispatcherQueue _ui;
    private DateTimeOffset? _lastFetch;

    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public bool On => Latitude is not null && Longitude is not null;

    public Weather(DispatcherQueue ui, string? stubDir, double? lat, double? lon)
    {
        _ui = ui;
        _stub = stubDir;
        (Latitude, Longitude) = (lat, lon);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sahar/1.0 (desktop companion)");
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromMinutes(10);
        t.Tick += (_, _) => _ = CheckAsync();
        t.Start();
        if (On) ui.TryEnqueue(() => _ = CheckAsync());   // 0.13: her weather tag from the start, not after ten minutes
    }

    /// <summary>Turned off (the tray, the settings): her weather tag goes.</summary>
    public event Action? TurnedOff;

    public void TurnOff()
    {
        Latitude = Longitude = null;
        Last = null;
        TurnedOff?.Invoke();
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

    /// <summary>The last forecast (0.13: her weather tag and card), raised on the UI thread.</summary>
    public event Action<Sahar.Core.Activities.Forecast>? Forecasted;
    public Sahar.Core.Activities.Forecast? Last { get; private set; }

    /// <summary>Fetches at most once an hour (also called on unlock: then only if the last one is stale).</summary>
    public async Task CheckAsync()
    {
        try
        {
            if (!On || !WeatherRules.ShouldFetch(DateTimeOffset.Now, _lastFetch)) return;
            _lastFetch = DateTimeOffset.Now;
            double lat = Latitude!.Value, lon = Longitude!.Value;
            var json = _stub is not null
                ? await File.ReadAllTextAsync(Path.Combine(_stub, "forecast.json"))
                : await _http.GetStringAsync(Sahar.Core.Activities.OpenMeteo.ForecastUrl(lat, lon));
            string? air = null;
            try
            {
                air = _stub is not null
                    ? (File.Exists(Path.Combine(_stub, "air.json")) ? await File.ReadAllTextAsync(Path.Combine(_stub, "air.json")) : null)
                    : await _http.GetStringAsync(Sahar.Core.Activities.OpenMeteo.AirUrl(lat, lon));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException) { Log.Write($"weather: air quality failed ({e.GetType().Name})"); }
            if (Sahar.Core.Activities.OpenMeteo.Parse(json, air, DateTimeOffset.Now) is not { } f) { Log.Write("weather: forecast not understood"); return; }
            Log.Write("weather: forecast");
            _ui.TryEnqueue(() =>
            {
                Last = f;
                Forecasted?.Invoke(f);
            });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            Log.Write($"weather: forecast failed ({e.GetType().Name})");
        }
    }

    /// <summary>A fetch now (on unlock or wake), if the last one is older than an hour.</summary>
    public void Stale() => _ = CheckAsync();

    public void Dispose() => _http.Dispose();
}

internal sealed record GeoResult(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude);

internal sealed record GeoResponse([property: JsonPropertyName("results")] List<GeoResult>? Results);

[JsonSerializable(typeof(GeoResponse))]
internal sealed partial class WeatherJson : JsonSerializerContext;
