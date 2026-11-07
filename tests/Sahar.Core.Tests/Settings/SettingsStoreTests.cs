using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-settings-").FullName;
    private string PathOf => System.IO.Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Load_MissingFile_ReturnsDefaults() => Assert.Equal(new SaharSettings(), SettingsStore.Load(PathOf));

    [Fact]
    public void Defaults_MatchSpec()
    {
        var s = new SaharSettings();
        Assert.Equal((1, SaharSize.Medium, 80, ActivityLevel.Medium, SoundLevel.Light), (s.Version, s.Size, (int)s.Size, s.Activity, s.Sound));
        Assert.Empty(s.BlockedApps);
        Assert.Equal((UiLanguage.Auto, true, false, true, SeasonalIcon.Auto), (s.Language, s.AwarenessEnabled, s.WeatherEnabled, s.UpdateCheckEnabled, s.Seasonal));
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var s = new SaharSettings
        {
            Language = UiLanguage.Arabic, Size = SaharSize.Large, Activity = ActivityLevel.Calm, Sound = SoundLevel.Full,
            Volume = 0.3f, QuietFrom = new TimeOnly(23, 0), QuietTo = new TimeOnly(7, 30), BlockedApps = ["bank.exe", "1password.exe"],
        };
        SettingsStore.Save(PathOf, s);
        var back = SettingsStore.Load(PathOf);
        Assert.Equal(s with { BlockedApps = [] }, back with { BlockedApps = [] });
        Assert.Equal(s.BlockedApps, back.BlockedApps);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults_AndKeepsBackup()
    {
        File.WriteAllText(PathOf, "{ not json");
        Assert.Equal(new SaharSettings(), SettingsStore.Load(PathOf));
        Assert.Equal("{ not json", File.ReadAllText(PathOf + ".bad"));
    }

    [Fact]
    public void Load_UnknownFields_Ignored()
    {
        File.WriteAllText(PathOf, """{ "version": 1, "size": "Small", "futureThing": 42 }""");
        Assert.Equal(SaharSize.Small, SettingsStore.Load(PathOf).Size);
    }

    [Fact]
    public void Save_IsAtomic()
    {
        SettingsStore.Save(PathOf, new SaharSettings());
        SettingsStore.Save(PathOf, new SaharSettings { Size = SaharSize.Small });
        Assert.False(File.Exists(PathOf + ".tmp"));
        Assert.Equal(SaharSize.Small, SettingsStore.Load(PathOf).Size);
    }

    [Fact]
    public void Save_CreatesMissingFolder()
    {
        var nested = System.IO.Path.Combine(_dir, "a", "b", "settings.json");
        SettingsStore.Save(nested, new SaharSettings());
        Assert.True(File.Exists(nested));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"activity\": \"calm\"}")]
    [InlineData("{\"version\": 1, \"weatherCity\": \"X\", \"weatherLatitude\": 1.5}")]
    public void Load_OldFileMissingNewerSettings_KeepsTheirDefaults(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sahar-old-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            var s = SettingsStore.Load(path);
            var d = new SaharSettings();
            Assert.Equal(d.PrayerReminderMinutes, s.PrayerReminderMinutes);   // 10, not 0 (the 0.6.0 one-minute reminder)
            Assert.Equal(d.QuranReciter, s.QuranReciter);
            Assert.Equal(d.Volume, s.Volume);
            Assert.Equal(d.Size, s.Size);
            Assert.Equal(d.Sound, s.Sound);
            Assert.Equal(d.AwarenessEnabled, s.AwarenessEnabled);
            Assert.NotNull(s.BlockedApps);
        }
        finally { File.Delete(path); }
    }
}
