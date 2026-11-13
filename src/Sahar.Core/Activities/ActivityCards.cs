using Sahar.Core.Art;

namespace Sahar.Core.Activities;

/// <summary>Which card a tag opens (board ActCards).</summary>
public enum CardKind { Music, Weather, Claude, Ask, Shelf, Copy, System, Time, Away }

public enum LyricsView { Off, Asking, Loading, Lines, None }

/// <summary>A picture for a card or tag: 8-bit premultiplied BGRA, width × height.</summary>
public sealed record Pixels(byte[] Bgra, int Width, int Height);

public sealed record MusicCard(string Title, string Artist, string App, Pixels? Cover, Rgba? CoverColour, bool Playing, TimeSpan Position,
    TimeSpan Duration, LyricsView Lyrics, string? PreviousLine, string? Line);

public sealed record WeatherCard(Forecast Forecast, WeatherPlace Place, IReadOnlyList<WeatherAlert> Alerts, DateTimeOffset Fetched);

public enum ShelfThing { Shot, Photo, Doc }

public sealed record ShelfTile(string Label, ShelfThing Thing, string Ext, Pixels? Thumb);

public sealed record ShelfCard(IReadOnlyList<ShelfTile> Tiles, int Count, IReadOnlySet<int> Selected);

public sealed record CopyCard(double Percent, TimeSpan? Left);

public sealed record SystemCard(int? Volume, int? Battery, bool Charging, TimeSpan? ToFull, string? MicApp, string? CameraApp);

public sealed record TimeCard(TimeSpan Total, IReadOnlyList<(string App, TimeSpan Time)> Apps, TimeSpan? Yesterday);

/// <summary>Everything one activity card draws from (one of the payloads, by <see cref="Kind"/>).</summary>
public sealed record CardData(CardKind Kind, MusicCard? Music = null, WeatherCard? Weather = null, ClaudeSession? Claude = null,
    ShelfCard? Shelf = null, CopyCard? Copy = null, SystemCard? System = null, TimeCard? Time = null, AwaySummary? Away = null);

public static class ActivityCards
{
    /// <summary>The card a tag opens.</summary>
    public static CardKind For(ActivityKind k) => k switch
    {
        ActivityKind.Music or ActivityKind.MusicPaused => CardKind.Music,
        ActivityKind.WeatherAlert or ActivityKind.WeatherToday => CardKind.Weather,
        ActivityKind.ClaudeWorking or ActivityKind.ClaudeDone => CardKind.Claude,
        ActivityKind.ClaudeAsk => CardKind.Ask,
        ActivityKind.Shelf or ActivityKind.Screenshot => CardKind.Shelf,
        ActivityKind.Copy or ActivityKind.CopyDone => CardKind.Copy,
        ActivityKind.Volume or ActivityKind.BatteryLow or ActivityKind.Charging or ActivityKind.CameraMic => CardKind.System,
        ActivityKind.ScreenTime => CardKind.Time,
        ActivityKind.Back => CardKind.Away,
        _ => CardKind.System,
    };
}
