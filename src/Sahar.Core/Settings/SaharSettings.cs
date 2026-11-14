using Sahar.Core.Activities;
using Sahar.Core.Prayer;
using Sahar.Core.Update;

namespace Sahar.Core.Settings;

public enum UiLanguage { Auto, Arabic, English }

/// <summary>Body width in DIPs (spec §4).</summary>
public enum SaharSize { Small = 48, Medium = 80, Large = 120 }

public enum ActivityLevel { Calm, Medium, Lively }

public enum SoundLevel { Silent, Light, Full }

public enum SeasonalIcon { Auto, On, Off }

/// <summary>What the menu card's header shows under her line (spec §5): the most urgent fact, the next prayer, or nothing.</summary>
public enum MenuHeader { Urgent, Prayer, None }

/// <summary>What opens the menu card besides the shortcut (spec §0.6): a double-click, a right-click, or both.</summary>
public enum MenuOpen { Double, Right, Both }

/// <summary>Where the card opens: beside her, or in the middle of her screen.</summary>
public enum MenuPlace { Beside, Center }

/// <summary>Light or dark: as Windows apps are, or always one of them.</summary>
public enum Appearance { System, Light, Dark }

/// <summary>Everything the user can set. Stored in %AppData%\Sahar\settings.json.
/// Setters, not init: the JSON source generator sets only the keys a file has, so an older file keeps the newer
/// settings' defaults (with init it wrote 0 and empty for them: the 0.6.0 one-minute prayer reminder).</summary>
public sealed record SaharSettings
{
    public int Version { get; set; } = 1;
    public UiLanguage Language { get; set; } = UiLanguage.Auto;
    public SaharSize Size { get; set; } = SaharSize.Medium;
    public ActivityLevel Activity { get; set; } = ActivityLevel.Medium;
    public SoundLevel Sound { get; set; } = SoundLevel.Light;
    public float Volume { get; set; } = 0.6f;
    public TimeOnly? QuietFrom { get; set; }
    public TimeOnly? QuietTo { get; set; }
    /// <summary>She comments on what you are doing (the settings page's «علّق على شغلك»).</summary>
    public bool AwarenessEnabled { get; set; } = true;
    /// <summary>Her mood changes by itself with the time and what happens (her late-night mood); 0.12.</summary>
    public bool AutoMood { get; set; } = true;
    /// <summary>She plays by herself when idle (moon, windows, the butterfly); 0.12.</summary>
    public bool PlaysAlone { get; set; } = true;
    public IReadOnlyList<string> BlockedApps { get; set; } = [];
    public bool WeatherEnabled { get; set; }
    /// <summary>The city you typed for the weather, and where Open-Meteo put it.</summary>
    public string? WeatherCity { get; set; }
    public double? WeatherLatitude { get; set; }
    public double? WeatherLongitude { get; set; }
    /// <summary>The city typed for prayer times (🕌), where it is, and how times are calculated (spec §7.1).</summary>
    public string? PrayerCity { get; set; }
    public double? PrayerLatitude { get; set; }
    public double? PrayerLongitude { get; set; }
    public PrayerMethod PrayerMethod { get; set; } = PrayerMethod.UmmAlQura;
    public bool PrayerHanafiAsr { get; set; }
    /// <summary>A reminder before each prayer (0.12: it can be switched off).</summary>
    public bool PrayerReminderOn { get; set; } = true;
    public int PrayerReminderMinutes { get; set; } = 10;
    /// <summary>📖 The reciter chosen and the surah played last (shown first).</summary>
    public string QuranReciter { get; set; } = "ar.alafasy";
    public int? QuranLastSurah { get; set; }
    /// <summary>Who she talks to (asked on the first start): her words agree with it, and she uses your name.</summary>
    public string? UserName { get; set; }
    public Gender UserGender { get; set; } = Gender.Unknown;
    public bool HelloDone { get; set; }
    /// <summary>Automatic update checks (2 min after start, then every 12 h); the tray item works either way.</summary>
    public bool UpdateCheckEnabled { get; set; } = true;
    /// <summary>Which release she reads for updates (the "test-build" pre-release for now).</summary>
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.TestBuild;
    /// <summary>She asked about an update: not again before this time (a day later; "later" or a closed bubble).</summary>
    public DateTimeOffset? UpdateAskAfter { get; set; }
    public SeasonalIcon Seasonal { get; set; } = SeasonalIcon.Auto;
    /// <summary>The little night sky with stars inside her (0.10a); on unless you turn it off.</summary>
    public bool InnerStars { get; set; } = true;
    /// <summary>The card's items that are on (ids of <c>MenuCatalog.ItemIds</c>); null = every available item (0.11).</summary>
    public IReadOnlyList<string>? MenuItems { get; set; }
    public MenuHeader MenuHeader { get; set; } = MenuHeader.Urgent;
    public MenuOpen MenuOpen { get; set; } = MenuOpen.Right;
    public MenuPlace MenuPlace { get; set; } = MenuPlace.Beside;
    /// <summary>The global shortcut that opens the card with the caret in the chat (spec §5).</summary>
    public string MenuShortcut { get; set; } = "Ctrl+Alt+S";
    public Appearance Appearance { get; set; } = Appearance.System;
    /// <summary>0.13 «النشاطات»: what she carries (all on but screen time).</summary>
    public bool ActMusic { get; set; } = true;
    public bool ActWeather { get; set; } = true;
    public bool ActClaude { get; set; } = true;
    public bool ActShelf { get; set; } = true;
    public bool ActSystem { get; set; } = true;
    public bool ActMicCam { get; set; } = true;
    public bool ActCopy { get; set; } = true;
    public bool ActAway { get; set; } = true;
    public bool ActScreenTime { get; set; }
    /// <summary>Lyrics from lrclib.net: off until you turn them on, and only after you said yes once (spec 0.13 §2).</summary>
    public bool LyricsEnabled { get; set; }
    public bool LyricsConsented { get; set; }
    public PausedKeep MusicPausedKeep { get; set; } = PausedKeep.TenMinutes;
    /// <summary>New screenshots land on her shelf too.</summary>
    public bool ShelfScreenshots { get; set; } = true;
    /// <summary>0.14 «عقلي»: on once a model is downloaded and verified (spec §12).</summary>
    public bool MindEnabled { get; set; }
    /// <summary>Unload the model after this many idle minutes (1 / 5 / 15 / 30).</summary>
    public int MindUnloadMinutes { get; set; } = 5;
    /// <summary>How often she speaks on her own (owner: often).</summary>
    public Sahar.Core.Mind.Initiative.InitiativeLevel MindInitiative { get; set; } = Sahar.Core.Mind.Initiative.InitiativeLevel.Often;
    /// <summary>«اعملي شغلاتي الصغيرة لحالي»: class-A self actions.</summary>
    public bool MindSelfActions { get; set; } = true;
    public Sahar.Core.Mind.Memory.MindVoice MindVoice { get; set; } = Sahar.Core.Mind.Memory.MindVoice.Complementary;
    public Sahar.Core.Mind.Runtime.Backend MindBackend { get; set; } = Sahar.Core.Mind.Runtime.Backend.Auto;
    public bool MindUseGpu { get; set; } = true;
    /// <summary>The four learning sources, each opt-in (owner: all four, each needs his yes).</summary>
    public bool MindLearnChats { get; set; }
    public bool MindLearnImports { get; set; }
    public bool MindLearnExamples { get; set; }
    public bool MindLearnMusic { get; set; }
    /// <summary>Raw chat episodes kept this many days (7 / 30 / 90), then summarised.</summary>
    public int MindEpisodeDays { get; set; } = 30;
}
