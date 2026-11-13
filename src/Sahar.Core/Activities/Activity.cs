namespace Sahar.Core.Activities;

/// <summary>What she can carry on her tag (spec 0.13 §1.3, §2).</summary>
public enum ActivityKind
{
    ClaudeAsk, WeatherAlert, Screenshot, CopyDone, ClaudeDone, Back, BatteryLow, Charging, Volume, Copy, Timer, ClaudeWorking,
    Music, CameraMic, Shelf, MusicPaused, WeatherToday, ScreenTime,
}

/// <summary>Weather alerts (spec 0.13 §2 Weather): each a moment, once per spell.</summary>
public enum WeatherAlert { Rain, Storm, Wind, Heat, Frost, Air }

/// <summary>One thing on her tag. <paramref name="Id"/> is stable per source ("music", "claude:&lt;session&gt;", "copy"), so an
/// update replaces it in place. The text is shown, never logged.</summary>
public sealed record Activity(string Id, ActivityKind Kind, string Text, string? Meta = null, string Icon = "", float? Progress = null,
    bool Asking = false, WeatherAlert? Alert = null);

/// <summary>The rank of each kind, high (1) to low (spec 0.13 §1.3, board ActRules).</summary>
public static class ActivityRank
{
    public static int Of(ActivityKind k) => k switch
    {
        ActivityKind.ClaudeAsk => 1,
        _ when IsMoment(k) => 2,
        ActivityKind.Copy or ActivityKind.Timer => 3,
        ActivityKind.ClaudeWorking => 4,
        ActivityKind.Music => 5,
        ActivityKind.CameraMic => 6,
        ActivityKind.Shelf => 7,
        ActivityKind.MusicPaused => 8,
        ActivityKind.WeatherToday => 9,
        _ => 10,   // screen time: in the cycle, never by itself
    };

    /// <summary>Moments take the front for a while, then go.</summary>
    public static bool IsMoment(ActivityKind k) => k is ActivityKind.WeatherAlert or ActivityKind.Screenshot or ActivityKind.CopyDone
        or ActivityKind.ClaudeDone or ActivityKind.Back or ActivityKind.BatteryLow or ActivityKind.Charging or ActivityKind.Volume;

    /// <summary>6 s; the volume moment 1.5 s.</summary>
    public static TimeSpan MomentLength(ActivityKind k) => k == ActivityKind.Volume ? TimeSpan.FromSeconds(1.5) : TimeSpan.FromSeconds(6);
}

/// <summary>Settings → النشاطات: each kind's switch (her own timer has none).</summary>
public static class ActivitySwitch
{
    public static bool On(ActivityKind k, Settings.SaharSettings s) => k switch
    {
        ActivityKind.Music or ActivityKind.MusicPaused => s.ActMusic,
        ActivityKind.WeatherAlert or ActivityKind.WeatherToday => s.ActWeather,
        ActivityKind.ClaudeAsk or ActivityKind.ClaudeWorking or ActivityKind.ClaudeDone => s.ActClaude,
        ActivityKind.Shelf or ActivityKind.Screenshot => s.ActShelf,
        ActivityKind.Volume or ActivityKind.BatteryLow or ActivityKind.Charging => s.ActSystem,
        ActivityKind.CameraMic => s.ActMicCam,
        ActivityKind.Copy or ActivityKind.CopyDone => s.ActCopy,
        ActivityKind.Back => s.ActAway,
        ActivityKind.ScreenTime => s.ActScreenTime,
        _ => true,
    };
}
