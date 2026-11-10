using Sahar.Core.Prayer;

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
    public bool AwarenessEnabled { get; set; } = true;
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
    public int PrayerReminderMinutes { get; set; } = 10;
    /// <summary>📖 The reciter chosen and the surah played last (shown first).</summary>
    public string QuranReciter { get; set; } = "ar.alafasy";
    public int? QuranLastSurah { get; set; }
    /// <summary>Who she talks to (asked on the first start): her words agree with it, and she uses your name.</summary>
    public string? UserName { get; set; }
    public Gender UserGender { get; set; } = Gender.Unknown;
    public bool HelloDone { get; set; }
    public bool UpdateCheckEnabled { get; set; } = true;
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
}
