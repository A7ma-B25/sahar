using System.Globalization;
using Sahar.Core.Senses;
using Sahar.Core.Settings;

namespace Sahar.Core.Mind;

/// <summary>
/// What the engine's senses hold at a moment. <see cref="WindowTitle"/> and <see cref="ClipboardText"/> are here
/// only so the rules can be tested to drop them (and to recognise a private window): they never reach a
/// <see cref="Situation"/>. <see cref="AppExe"/> is the executable's name, for the blocked list.
/// </summary>
public sealed record SituationInputs(DateTimeOffset Now)
{
    public (Sahar.Core.Prayer.Prayer Prayer, TimeSpan Left)? NextPrayer { get; init; }
    public TimeSpan? TimerLeft { get; init; }
    public bool Hushed { get; init; }
    public string? AppName { get; init; }
    public string? AppExe { get; init; }
    public TimeSpan? AppTime { get; init; }
    public string? WindowTitle { get; init; }
    public string? ClipboardText { get; init; }
    public bool MusicPlaying { get; init; }
    public string? MusicTitle { get; init; }
    public int ClaudeWorking { get; init; }
    public int ClaudeWaiting { get; init; }
    public string? Weather { get; init; }
    public int BatteryPercent { get; init; } = 100;
    public bool Charging { get; init; }
    public bool HasBattery { get; init; }
    public string Mood { get; init; } = "calm";
    public int HerLinesToday { get; init; }
}

/// <summary>
/// Spec §3.9: the situation snapshot sent with every turn. Never: window titles, clipboard, screen, file names, URLs,
/// notification text, other people's names. The app's friendly name only with «علّق على شغلك» on and the app not
/// blocked (his list, password managers, private windows); a song title only with the music learning source on;
/// the weather only when it is on.
/// </summary>
public static class SituationRules
{
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    public static Situation From(SituationInputs i, SaharSettings s)
    {
        bool appOk = s.AwarenessEnabled && i.AppName is { Length: > 0 } && !Blocked(i, s);
        return new Situation(
            i.Now.ToString("HH:mm", CultureInfo.InvariantCulture), Days[(int)i.Now.DayOfWeek],
            NextPrayer: i.NextPrayer?.Prayer.ToString().ToLowerInvariant(),
            PrayerInMinutes: i.NextPrayer is { } p ? (int)Math.Ceiling(p.Left.TotalMinutes) : null,
            TimerMinutesLeft: i.TimerLeft is { } t ? (int)Math.Ceiling(t.TotalMinutes) : null,
            Hushed: i.Hushed,
            App: appOk ? i.AppName : null,
            AppMinutes: appOk && i.AppTime is { } at ? (int)at.TotalMinutes : null,
            MusicPlaying: i.MusicPlaying,
            MusicTitle: i.MusicPlaying && s.MindLearnMusic ? i.MusicTitle : null,
            ClaudeWorking: i.ClaudeWorking, ClaudeWaiting: i.ClaudeWaiting,
            Weather: s.WeatherEnabled ? i.Weather : null,
            Battery: !i.HasBattery ? "ok" : i.Charging ? "charging" : i.BatteryPercent < 20 ? "low" : "ok",
            Mood: i.Mood, HerLinesToday: i.HerLinesToday);
    }

    private static bool Blocked(SituationInputs i, SaharSettings s)
    {
        var exe = i.AppExe is { Length: > 0 } e ? PrivacyLists.ExeName(e) : "";
        if (exe.Length > 0 && (PrivacyLists.IsPasswordManager(exe) || s.BlockedApps.Any(b => PrivacyLists.ExeName(b) == exe))) return true;
        return i.WindowTitle is { } title && PrivacyLists.IsPrivateWindow(title);
    }
}
