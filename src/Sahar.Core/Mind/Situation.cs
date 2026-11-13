namespace Sahar.Core.Mind;

/// <summary>
/// What she knows of the moment (spec §3.9), sent with every turn. Built by the engine (stream C: <c>Situation.From</c>
/// rules). NEVER: window titles, clipboard, screen, file names, URLs, notification text, other people's names.
/// <see cref="App"/> only with «علّق على شغلك» on and the app not blocked; <see cref="MusicTitle"/> only with the
/// music learning source on.
/// </summary>
public sealed record Situation(
    string LocalTime, string Weekday,
    string? NextPrayer = null, int? PrayerInMinutes = null,
    int? TimerMinutesLeft = null, bool Hushed = false,
    string? App = null, int? AppMinutes = null,
    bool MusicPlaying = false, string? MusicTitle = null,
    int ClaudeWorking = 0, int ClaudeWaiting = 0,
    string? Weather = null, string Battery = "ok", string Mood = "calm", int HerLinesToday = 0);
