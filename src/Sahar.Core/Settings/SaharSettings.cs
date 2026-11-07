using Sahar.Core.Prayer;

namespace Sahar.Core.Settings;

public enum UiLanguage { Auto, Arabic, English }

/// <summary>Body width in DIPs (spec §4).</summary>
public enum SaharSize { Small = 48, Medium = 80, Large = 120 }

public enum ActivityLevel { Calm, Medium, Lively }

public enum SoundLevel { Silent, Light, Full }

public enum SeasonalIcon { Auto, On, Off }

/// <summary>Everything the user can set. Stored in %AppData%\Sahar\settings.json.</summary>
public sealed record SaharSettings
{
    public int Version { get; init; } = 1;
    public UiLanguage Language { get; init; } = UiLanguage.Auto;
    public SaharSize Size { get; init; } = SaharSize.Medium;
    public ActivityLevel Activity { get; init; } = ActivityLevel.Medium;
    public SoundLevel Sound { get; init; } = SoundLevel.Light;
    public float Volume { get; init; } = 0.6f;
    public TimeOnly? QuietFrom { get; init; }
    public TimeOnly? QuietTo { get; init; }
    public bool AwarenessEnabled { get; init; } = true;
    public IReadOnlyList<string> BlockedApps { get; init; } = [];
    public bool WeatherEnabled { get; init; }
    /// <summary>The city you typed for the weather, and where Open-Meteo put it.</summary>
    public string? WeatherCity { get; init; }
    public double? WeatherLatitude { get; init; }
    public double? WeatherLongitude { get; init; }
    /// <summary>The city typed for prayer times (🕌), where it is, and how times are calculated (spec §7.1).</summary>
    public string? PrayerCity { get; init; }
    public double? PrayerLatitude { get; init; }
    public double? PrayerLongitude { get; init; }
    public PrayerMethod PrayerMethod { get; init; } = PrayerMethod.UmmAlQura;
    public bool PrayerHanafiAsr { get; init; }
    public int PrayerReminderMinutes { get; init; } = 10;
    /// <summary>📖 The reciter chosen and the surah played last (shown first).</summary>
    public string QuranReciter { get; init; } = "ar.alafasy";
    public int? QuranLastSurah { get; init; }
    public bool UpdateCheckEnabled { get; init; } = true;
    public SeasonalIcon Seasonal { get; init; } = SeasonalIcon.Auto;
}
