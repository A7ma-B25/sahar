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
    // ---- Two writers (owner ruling 2026-11-10): the engine and the settings app share settings.json ----

    [Fact]
    public void Save_OverAnUnreadableFile_IsRefused_AndTheFileIsKept()
    {
        File.WriteAllText(PathOf, "{ not json");
        Assert.False(SettingsStore.TrySave(PathOf, new SaharSettings { Size = SaharSize.Large }));
        Assert.Equal("{ not json", File.ReadAllText(PathOf));
        Assert.Equal("{ not json", File.ReadAllText(PathOf + ".bad"));
        Assert.Throws<SettingsSaveRefusedException>(() => SettingsStore.Save(PathOf, new SaharSettings()));
        Assert.Equal("{ not json", File.ReadAllText(PathOf));
    }

    [Fact]
    public void TrySave_GoodFile_Saves()
    {
        SettingsStore.Save(PathOf, new SaharSettings());
        Assert.True(SettingsStore.TrySave(PathOf, new SaharSettings { Size = SaharSize.Large }));
        Assert.Equal(SaharSize.Large, SettingsStore.Load(PathOf).Size);
    }

    [Fact]
    public void Save_ManyWritersAndReaders_NeverThrow_AndTheFileStaysWhole()
    {
        SettingsStore.Save(PathOf, new SaharSettings());
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var writers = Enumerable.Range(0, 8).Select(w => new Thread(() =>
        {
            for (int i = 0; i < 25; i++)
                try { SettingsStore.Save(PathOf, new SaharSettings { Activity = (ActivityLevel)(w % 3), QuranLastSurah = w * 100 + i }); }
                catch (Exception e) { errors.Add(e); }
        })).ToList();
        var readers = Enumerable.Range(0, 2).Select(r => new Thread(() =>
        {
            for (int i = 0; i < 60; i++)
                try { _ = SettingsStore.Load(PathOf); }
                catch (Exception e) { errors.Add(e); }
        })).ToList();
        foreach (var t in writers.Concat(readers)) t.Start();
        foreach (var t in writers.Concat(readers)) t.Join();
        Assert.Empty(errors);
        Assert.False(File.Exists(PathOf + ".bad"), "a reader saw a half-written file");
        var last = SettingsStore.Load(PathOf);
        Assert.InRange(last.QuranLastSurah ?? -1, 0, 724);
    }

    [Fact]
    public void Retry_SharingViolations_ThenGivesUp()
    {
        int calls = 0;
        Assert.Equal(7, SettingsStore.Retry(() => { if (++calls < 3) throw new IOException("in use"); return 7; }));
        Assert.Equal(3, calls);
        calls = 0;
        Assert.Throws<IOException>(() => SettingsStore.Retry<int>(() => { calls++; throw new IOException("in use"); }));
        Assert.Equal(SettingsStore.Tries, calls);
        Assert.Throws<FileNotFoundException>(() => SettingsStore.Retry<int>(() => throw new FileNotFoundException()));   // not a sharing problem: no retry
    }
    [Fact]
    public void OldFile_InnerStarsDefaultsOn()
    {
        File.WriteAllText(PathOf, """{ "version": 1, "size": "small" }""");
        Assert.True(SettingsStore.Load(PathOf).InnerStars);
        File.WriteAllText(PathOf, """{ "innerStars": false }""");
        Assert.False(SettingsStore.Load(PathOf).InnerStars);
    }
    // ---- Fresh review (2026-11-10) ----

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n ")]
    public void EmptyFile_CountsAsMissing(string text)
    {
        File.WriteAllText(PathOf, text);
        Assert.Equal(new SaharSettings(), SettingsStore.Load(PathOf));
        Assert.False(File.Exists(PathOf + ".bad"));
        Assert.True(SettingsStore.TrySave(PathOf, new SaharSettings { Size = SaharSize.Large }));
        Assert.Equal(SaharSize.Large, SettingsStore.Load(PathOf).Size);
    }

    [Fact]
    public void AFailedLoad_RefusesSaves_UntilALoadSucceeds()
    {
        SettingsStore.Save(PathOf, new SaharSettings { Size = SaharSize.Large, UserName = "Sami" });
        SettingsStore.LoadFailed(PathOf);   // what Load records when every read attempt failed
        Assert.False(SettingsStore.TrySave(PathOf, new SaharSettings()));
        Assert.Equal("Sami", SettingsStore.Load(PathOf).UserName);   // the good file is untouched, and this load succeeds
        Assert.True(SettingsStore.TrySave(PathOf, new SaharSettings { UserName = "Sami", Size = SaharSize.Small }));
    }

    [Fact]
    public void Load_ThatKeepsFailing_IsRemembered()
    {
        SettingsStore.Save(PathOf, new SaharSettings { UserName = "Sami" });
        Assert.ThrowsAny<IOException>(() => SettingsStore.Load(PathOf, _ => throw new IOException("in use")));
        Assert.False(SettingsStore.TrySave(PathOf, new SaharSettings()));
        Assert.Equal("Sami", SettingsStore.Load(PathOf).UserName);
    }

    [Fact]
    public void Retry_AlsoRetriesAccessDenied_WhileAFileIsBeingReplaced()
    {
        int calls = 0;
        Assert.Equal(5, SettingsStore.Retry(() => { if (++calls < 3) throw new UnauthorizedAccessException(); return 5; }));
        Assert.Equal(3, calls);
    }
}
