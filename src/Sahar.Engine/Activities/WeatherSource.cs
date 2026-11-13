using Sahar.Core.Activities;
using Sahar.Core.Brain;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;

namespace Sahar.Engine.Activities;

/// <summary>
/// The weather on her tag (spec 0.13 §2): «٢٤° صحو» when nothing else is live, each alert once per spell as a moment
/// with her bubble line (rain also gives her rain face and the drooping leaf), and the card with the next five hours.
/// Coordinates come from the weather's own city or, without one, the prayer place (owner ruling 4).
/// </summary>
internal sealed class WeatherSource
{
    private readonly ActivityHub _hub;
    private readonly Weather _weather;
    private readonly Func<SaharSettings> _settings;
    private readonly Action<EventKind> _post;
    private readonly AlertLatch _latch = new();
    private Forecast? _last;

    public WeatherSource(ActivityHub hub, Weather weather, Func<SaharSettings> settings, Action<EventKind> post)
    {
        _hub = hub;
        _weather = weather;
        _settings = settings;
        _post = post;
        weather.Forecasted += f => Safe(() => Show(f, DateTimeOffset.Now));
        weather.TurnedOff += () => Safe(Off);
    }

    /// <summary>Rain is coming now (her leaf droops like an umbrella).</summary>
    public bool RainSoon => _last is { } f && WeatherAlerts.Find(f, DateTimeOffset.Now).Contains(WeatherAlert.Rain);
    public event Action? Changed;

    public void Show(Forecast f, DateTimeOffset now)
    {
        _last = f;
        var alerts = WeatherAlerts.Find(f, now);
        var fired = _latch.Fire(alerts, now);
        Log.Write($"weather: {alerts.Count} alerts, {fired.Count} new");
        _hub.Set(new Activity("weather", ActivityKind.WeatherToday, ActivityText.WeatherToday(f), null, f.CodeNow is 0 or 1 ? "sun" : "cloud"), Card);
        foreach (var a in fired)
        {
            if (a == WeatherAlert.Rain) _post(EventKind.WeatherRain);
            _hub.Moment(new Activity("weather-alert", ActivityKind.WeatherAlert, ActivityText.AlertTag(a), null, ActivityIcons.For(ActivityKind.WeatherAlert, a), Alert: a),
                Card, ActivityText.AlertBubble(a));
        }
        Changed?.Invoke();
    }

    public void Off()
    {
        _last = null;
        _hub.Remove("weather");
        Changed?.Invoke();
    }

    private CardData? Card()
    {
        if (_last is not { } f || WeatherCity.Resolve(_settings()) is not { } place) return null;
        return new CardData(CardKind.Weather, Weather: new WeatherCard(f, place, WeatherAlerts.Find(f, DateTimeOffset.Now), f.At));
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"weather source error: {e.GetType().Name}"); }
    }
}
