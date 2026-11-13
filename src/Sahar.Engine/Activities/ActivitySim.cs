using Sahar.Core.Activities;
using Sahar.Core.Art;

namespace Sahar.Engine.Activities;

/// <summary>
/// Test mode: every activity of the mockups (ActRest, ActCards) with their sample data, so CI can draw each tag and
/// card in light and dark without music, Claude, a battery or a copy on the runner.
/// </summary>
internal static class ActivitySim
{
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "music", "musicPaused", "weather", "weatherAlert", "claude", "claudeAsk", "claudeDone", "shelf", "shot", "copy", "volume",
        "battery", "charging", "camera", "away", "time",
    ];

    /// <summary>The activity and its card for a kind; null for an unknown kind.</summary>
    public static (Activity Activity, Func<CardData?> Card)? Make(string kind, DateTimeOffset now)
    {
        var claude = new ClaudeSession("sim", "sahar", ClaudeStep.Editing, "OverlayPlan.cs",
            [
                new ClaudeTodo("افهم كيف بتكبر النافذة", "completed"), new ClaudeTodo("اكتب اختبار للشاشتين", "completed"),
                new ClaudeTodo("عدّل OverlayPlan", "in_progress"), new ClaudeTodo("شغّل كل الاختبارات", "pending"), new ClaudeTodo("حدّث الملاحظات", "pending"),
            ], now.AddMinutes(-4), false, now);
        var music = new MusicCard("نسّم علينا الهوا", "فيروز", "Spotify", null, null, true, TimeSpan.FromSeconds(84), TimeSpan.FromSeconds(220),
            LyricsView.Off, null, null);
        var system = new SystemCard(60, 82, true, TimeSpan.FromMinutes(40), "Teams", null);
        Func<CardData?> Sys(SystemCard s) => () => new CardData(CardKind.System, System: s);
        return kind switch
        {
            "music" => (new Activity("music", ActivityKind.Music, "نسّم علينا الهوا", null, "notes", 0.38f), () => new CardData(CardKind.Music, Music: music)),
            "musicPaused" => (new Activity("music", ActivityKind.MusicPaused, "نسّم علينا الهوا", ActivityText.Paused, "notes"),
                () => new CardData(CardKind.Music, Music: music with { Playing = false })),
            "musicLyricsAsk" => (new Activity("music", ActivityKind.Music, "نسّم علينا الهوا", null, "notes", 0.38f),
                () => new CardData(CardKind.Music, Music: music with { Lyrics = LyricsView.Asking })),
            "musicLyrics" => (new Activity("music", ActivityKind.Music, "نسّم علينا الهوا", null, "notes", 0.38f),
                () => new CardData(CardKind.Music, Music: music with { Lyrics = LyricsView.Lines, PreviousLine = "نسّم علينا الهوا من مفرق الوادي", Line = "يا هوا دخل الهوا خدني على بلادي" })),
            "weather" => (new Activity("weather", ActivityKind.WeatherToday, "٢٤° صحو", null, "sun"), () => new CardData(CardKind.Weather, Weather: Weather(now))),
            "weatherAlert" => (new Activity("weather-alert", ActivityKind.WeatherAlert, ActivityText.AlertTag(WeatherAlert.Rain), null, "rain", Alert: WeatherAlert.Rain),
                () => new CardData(CardKind.Weather, Weather: Weather(now))),
            "claude" => (new Activity("claude:sim", ActivityKind.ClaudeWorking, "Claude عم يعدّل", "٤ د", "terminal"), () => new CardData(CardKind.Claude, Claude: claude)),
            "claudeAsk" => (new Activity("claude-ask:sim", ActivityKind.ClaudeAsk, ActivityText.AskTag, null, "bell", Asking: true),
                () => new CardData(CardKind.Ask, Claude: claude with { Step = ClaudeStep.Running, Target = null, Asking = true })),
            "claudeDone" => (new Activity("claude-done:sim", ActivityKind.ClaudeDone, ActivityText.ClaudeDone, "١٢ د", "check"),
                () => new CardData(CardKind.Claude, Claude: claude with { Started = null, Step = ClaudeStep.None, Target = null })),
            "shelf" => (new Activity("shelf", ActivityKind.Shelf, ActivityText.Shelf(4), null, "stack"), () => new CardData(CardKind.Shelf, Shelf: Shelf())),
            "shot" => (new Activity("shot", ActivityKind.Screenshot, ActivityText.Screenshot), () => new CardData(CardKind.Shelf, Shelf: Shelf())),
            "copy" => (new Activity("copy", ActivityKind.Copy, ActivityText.Copying, "٤٢٪", "copy", 0.42f),
                () => new CardData(CardKind.Copy, Copy: new CopyCard(42, TimeSpan.FromMinutes(3)))),
            "volume" => (new Activity("volume", ActivityKind.Volume, ActivityText.Volume, "٦٠", "speaker", 0.6f), Sys(system)),
            "battery" => (new Activity("battery", ActivityKind.BatteryLow, "البطارية ١٥٪", null, "battery"), Sys(system with { Battery = 15, Charging = false, ToFull = null })),
            "charging" => (new Activity("charging", ActivityKind.Charging, ActivityText.Charging, "٨٢٪", "bolt"), Sys(system)),
            "camera" => (new Activity("cameramic", ActivityKind.CameraMic, ActivityText.Camera, null, "camera"), Sys(system with { CameraApp = "Teams" })),
            "away" => (new Activity("away", ActivityKind.Back, ActivityText.Away, "٤", "lock"), () => new CardData(CardKind.Away, Away: Away(now))),
            "time" => (new Activity("screentime", ActivityKind.ScreenTime, ActivityText.Today, "٥ س ٢٠ د", "hourglass"), () => new CardData(CardKind.Time,
                Time: new TimeCard(TimeSpan.FromMinutes(320), [("Code", TimeSpan.FromMinutes(130)), ("Chrome", TimeSpan.FromMinutes(95)),
                    ("Claude", TimeSpan.FromMinutes(50)), ("Telegram", TimeSpan.FromMinutes(25))], TimeSpan.FromMinutes(360)))),
            _ => null,
        };
    }

    private static WeatherCard Weather(DateTimeOffset now)
    {
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset).AddHours(1);
        (int Code, double Temp, double Gust)[] five = [(0, 25, 10), (0, 26, 12), (3, 24, 20), (1, 21, 65), (1, 18, 62)];
        var hours = five.Select((h, i) => new HourCast(start.AddHours(i), h.Temp, h.Code, 0, h.Gust)).ToList();
        var f = new Forecast(24, 0, hours, 26, 12, 30, now);
        return new WeatherCard(f, new WeatherPlace("دمشق", 33.51, 36.28, true), [WeatherAlert.Wind], now.AddMinutes(-10));
    }

    private static ShelfCard Shelf() => new(
        [
            new ShelfTile("لقطة ٢:١٤", ShelfThing.Shot, "", null), new ShelfTile("البحر", ShelfThing.Photo, "", null),
            new ShelfTile("العقد", ShelfThing.Doc, "PDF", null), new ShelfTile("ملاحظات", ShelfThing.Doc, "TXT", null),
        ], 4, new HashSet<int> { 0 });

    private static AwaySummary Away(DateTimeOffset now)
    {
        var at = new DateTimeOffset(now.Year, now.Month, now.Day, 15, 0, 0, now.Offset);
        return new AwaySummary(TimeSpan.FromMinutes(70),
            [
                (ActivityKind.ClaudeDone, 1, at.AddMinutes(22)), (ActivityKind.CopyDone, 1, at.AddMinutes(5)),
                (ActivityKind.WeatherAlert, 1, at.AddMinutes(10)), (ActivityKind.Charging, 1, at.AddMinutes(40)),
            ], 0, 4);
    }

    /// <summary>The sim's cover: the mockup's placeholder colour.</summary>
    public static Rgba Cover(bool dark) => Rgba.Parse(dark ? "#C9773F" : "#E38B4F");
}
