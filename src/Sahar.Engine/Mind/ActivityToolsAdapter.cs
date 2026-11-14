using Sahar.Engine.Activities;
using Sahar.Engine.Mind.Tools;

namespace Sahar.Engine.Mind;

/// <summary>Integration (0.14 × 0.13): her brain's weather.now, claude.status and shelf.* over the activity sources.
/// Counts and her own tag line only — never a prompt, a project name, a file name or a city.</summary>
internal sealed class ActivityToolsAdapter(WeatherSource weather, ClaudeSource claude, ShelfSource shelf, ActivityHub hub,
    Func<Sahar.Core.Settings.SaharSettings> settings) : IActivityTools
{
    public string? WeatherNow() => settings().WeatherEnabled && settings().ActWeather ? weather.Summary : null;
    public (int Working, int Waiting, int DoneToday)? ClaudeStatus() => settings().ActClaude ? claude.Counts() : null;
    public bool HasShelf => settings().ActShelf && shelf.HasItems;
    public void ShelfOpen() => hub.OpenCard("shelf");
    public bool ShelfTidy() => shelf.Tidy();
    public bool ShelfClear() => shelf.Empty();
}
