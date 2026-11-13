using System.Text.Json;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>The settings page ↔ settings.json ↔ her (plan 0.10c Task 3): every message the page sends.</summary>
public sealed class SettingsBridgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-bridge-").FullName;
    private string PathOf => Path.Combine(_dir, "settings.json");
    private readonly FakeEngine _engine = new();
    private readonly FakeLocator _locator = new();
    private DateTimeOffset _now = new(2026, 11, 13, 13, 0, 0, TimeSpan.FromHours(3));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private SettingsBridge Bridge() => new(PathOf, _engine, _locator, () => _now);

    private async Task<JsonElement> Ask(string json)
    {
        var reply = await Bridge().HandleAsync(json);
        return JsonDocument.Parse(reply).RootElement.Clone();
    }

    private sealed class FakeEngine : IEngineLink
    {
        public bool Running { get; set; } = true;
        public int Reloads, HelloAsks, Clears, Erases;
        public List<string> Shortcuts = [];
        public (bool Ok, string? Reason) ShortcutAnswer = (true, null);
        public CityResult City = new(new GeoPlace("Aleppo", 36.2, 37.16), null);

        public Task<bool> ReloadAsync(bool askHello = false)
        {
            Reloads++;
            if (askHello) HelloAsks++;
            return Task.FromResult(Running);
        }

        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value)
        {
            Shortcuts.Add(value);
            return Task.FromResult(ShortcutAnswer);
        }

        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(City);
        public Task<bool> ClearClipboardAsync() { Clears++; return Task.FromResult(Running); }
        public Task<bool> EraseAllAsync() { Erases++; return Task.FromResult(Running); }
    }

    private sealed class FakeLocator : ILocator
    {
        public (double Lat, double Lon)? Where = (33.51, 36.29);
        public Task<(double Lat, double Lon)?> LocateAsync() => Task.FromResult(Where);
    }

    [Fact]
    public async Task Get_ReturnsSettingsCapsAndPairs()
    {
        SettingsStore.Save(PathOf, new SaharSettings { Size = SaharSize.Large });
        var r = await Ask("""{"id":1,"type":"get"}""");
        Assert.Equal(1, r.GetProperty("id").GetInt32());
        Assert.Equal("settings", r.GetProperty("type").GetString());
        Assert.Equal("Large", r.GetProperty("value").GetProperty("size").GetString(), ignoreCase: true);
        var caps = r.GetProperty("caps");
        Assert.True(caps.GetProperty("engine").GetBoolean());
        Assert.False(caps.GetProperty("broken").GetBoolean());
        Assert.Equal(["sahar", "menu", "brain", "models", "you", "today", "prayer", "listen", "activities", "general", "privacy", "about"],
            caps.GetProperty("pages").EnumerateArray().Select(p => p.GetString()!));
        Assert.Equal(6, caps.GetProperty("reciters").GetArrayLength());
        Assert.Equal("ar.alafasy", caps.GetProperty("reciters")[0].GetProperty("id").GetString());
        Assert.Equal(5, caps.GetProperty("methods").GetArrayLength());
        Assert.Equal("muslimWorldLeague", caps.GetProperty("methods")[1].GetProperty("id").GetString());
        Assert.Equal("رابطة العالم الإسلامي", caps.GetProperty("methods")[1].GetProperty("name").GetString());
        Assert.Equal(8, caps.GetProperty("items").GetArrayLength());
        Assert.Equal(Sahar.Core.CoreInfo.Version, caps.GetProperty("version").GetString());
        Assert.Equal("الضهر", caps.GetProperty("prayerNames").GetProperty("dhuhr").GetString());
        Assert.Contains(r.GetProperty("pairs").EnumerateArray(), p => p[0].GetString() == "اكتب" && p[1].GetString() == "اكتبي");
    }

    [Fact]
    public async Task Get_UnreadableFile_SaysBroken_AndShowsDefaults()
    {
        File.WriteAllText(PathOf, "{ not json");
        var r = await Ask("""{"id":2,"type":"get"}""");
        Assert.True(r.GetProperty("caps").GetProperty("broken").GetBoolean());
        Assert.Equal("Medium", r.GetProperty("value").GetProperty("size").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Set_Valid_SavesAndReloadsEngine()
    {
        var r = await Ask("""{"id":3,"type":"set","key":"size","value":"large"}""");
        Assert.Equal(3, r.GetProperty("id").GetInt32());
        Assert.Equal("saved", r.GetProperty("type").GetString());
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Contains("\"size\": \"Large\"", File.ReadAllText(PathOf), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, _engine.Reloads);
        Assert.Equal("Large", r.GetProperty("value").GetProperty("size").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Set_Invalid_DoesNotSave()
    {
        SettingsStore.Save(PathOf, new SaharSettings());
        var before = File.ReadAllText(PathOf);
        var r = await Ask("""{"id":4,"type":"set","key":"prayerReminderMinutes","value":12}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-value", r.GetProperty("error").GetString());
        Assert.Equal(before, File.ReadAllText(PathOf));
        Assert.Equal(0, _engine.Reloads);
    }

    [Fact]
    public async Task Set_OnAnUnreadableFile_IsRefused_AndTheFileKept()
    {
        File.WriteAllText(PathOf, "{ not json");
        var r = await Ask("""{"id":5,"type":"set","key":"size","value":"large"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("unreadable", r.GetProperty("error").GetString());
        Assert.Equal("{ not json", File.ReadAllText(PathOf));
    }

    [Fact]
    public async Task Set_KeepsWhatTheEngineSavedMeanwhile()
    {
        SettingsStore.Save(PathOf, new SaharSettings());
        SettingsStore.Update(PathOf, s => s with { Activity = ActivityLevel.Lively });   // the engine, from the tray
        await Ask("""{"id":6,"type":"set","key":"size","value":"small"}""");
        var s = SettingsStore.Load(PathOf);
        Assert.Equal((ActivityLevel.Lively, SaharSize.Small), (s.Activity, s.Size));
    }

    [Fact]
    public async Task City_Prayer_StoresCoordinates()
    {
        var r = await Ask("""{"id":7,"type":"city","text":"حلب","for":"prayer"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("Aleppo", r.GetProperty("name").GetString());
        var s = SettingsStore.Load(PathOf);
        Assert.Equal(("Aleppo", 36.2, 37.16), (s.PrayerCity, s.PrayerLatitude, s.PrayerLongitude));
        Assert.Equal(1, _engine.Reloads);
    }

    [Fact]
    public async Task City_Weather_StoresCoordinates_AndTurnsItOn()
    {
        var r = await Ask("""{"id":8,"type":"city","text":"حلب","for":"weather"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var s = SettingsStore.Load(PathOf);
        Assert.Equal(("Aleppo", 36.2, 37.16, true), (s.WeatherCity, s.WeatherLatitude, s.WeatherLongitude, s.WeatherEnabled));
    }

    [Fact]
    public async Task City_NotFound_SaysTheGeocodersWords()
    {
        _engine.City = new CityResult(null, "ما لقيت هالمدينة");
        var r = await Ask("""{"id":9,"type":"city","text":"xyz","for":"prayer"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("ما لقيت هالمدينة", r.GetProperty("reason").GetString());
        Assert.False(File.Exists(PathOf));
    }

    [Fact]
    public async Task City_WithoutEngine_SaysSo()
    {
        _engine.Running = false;
        var r = await Ask("""{"id":10,"type":"city","text":"حلب","for":"prayer"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(SettingsBridge.NoEngineCity, r.GetProperty("reason").GetString());
        Assert.Equal("شغّلني أول لدوّر عالمدينة.", SettingsBridge.NoEngineCity);
    }

    [Fact]
    public async Task Bridge_WithoutEngine_SetStillSaves()
    {
        _engine.Running = false;
        var r = await Ask("""{"id":11,"type":"set","key":"innerStars","value":false}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.False(SettingsStore.Load(PathOf).InnerStars);
    }

    [Fact]
    public async Task Locate_StoresWindowsLocation()
    {
        var r = await Ask("""{"id":12,"type":"locate"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var s = SettingsStore.Load(PathOf);
        Assert.Equal((SettingsBridge.WindowsLocation, 33.51, 36.29), (s.PrayerCity, s.PrayerLatitude, s.PrayerLongitude));
        _locator.Where = null;
        Assert.False((await Ask("""{"id":13,"type":"locate"}""")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task PrayerToday_FiveCellsAndNext()
    {
        SettingsStore.Save(PathOf, new SaharSettings { PrayerLatitude = 36.2, PrayerLongitude = 37.16, PrayerCity = "Aleppo" });
        var r = await Ask("""{"id":14,"type":"prayer-today"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var times = r.GetProperty("times").EnumerateArray().ToList();
        Assert.Equal(["fajr", "dhuhr", "asr", "maghrib", "isha"], times.Select(t => t.GetProperty("prayer").GetString()!));
        Assert.All(times, t => Assert.Matches(@"^\d\d:\d\d$", t.GetProperty("at").GetString()!));
        var next = r.GetProperty("next");
        Assert.Equal("asr", next.GetProperty("prayer").GetString());   // 13:00 in Aleppo, mid-November: Asr is next
        Assert.InRange(next.GetProperty("seconds").GetInt32(), 1, 4 * 3600);
    }

    [Fact]
    public async Task PrayerToday_WithoutACity_IsNotOk() =>
        Assert.False((await Ask("""{"id":15,"type":"prayer-today"}""")).GetProperty("ok").GetBoolean());

    [Fact]
    public async Task Shortcut_RefusedByCore_NotSent()
    {
        var r = await Ask("""{"id":16,"type":"shortcut","value":"Ctrl+C"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(Sahar.Core.Menu.Shortcuts.AppsCommon, r.GetProperty("reason").GetString());
        Assert.Empty(_engine.Shortcuts);
        Assert.False(File.Exists(PathOf));
    }

    [Fact]
    public async Task Shortcut_TakenByEngine_NotSaved()
    {
        _engine.ShortcutAnswer = (false, Sahar.Core.Menu.Shortcuts.Taken);
        var r = await Ask("""{"id":17,"type":"shortcut","value":"Ctrl+Alt+K"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal(Sahar.Core.Menu.Shortcuts.Taken, r.GetProperty("reason").GetString());
        Assert.Equal(["Ctrl+Alt+K"], _engine.Shortcuts);
        Assert.Equal("Ctrl+Alt+S", SettingsStore.Load(PathOf).MenuShortcut);
    }

    [Fact]
    public async Task Shortcut_Free_IsSaved()
    {
        var r = await Ask("""{"id":18,"type":"shortcut","value":"ctrl+alt+k"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("Ctrl+Alt+K", SettingsStore.Load(PathOf).MenuShortcut);
        Assert.Equal("Ctrl+Alt+K", r.GetProperty("value").GetProperty("menuShortcut").GetString());
    }

    [Fact]
    public async Task ClipboardClear_And_Erase_CallTheEngine()
    {
        Assert.True((await Ask("""{"id":19,"type":"clipboard-clear"}""")).GetProperty("ok").GetBoolean());
        Assert.Equal(1, _engine.Clears);
        var r = await Ask("""{"id":20,"type":"erase"}""");
        Assert.Equal("erase", r.GetProperty("type").GetString());
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(1, _engine.Erases);
    }

    [Fact]
    public async Task Reset_KeepsTheBadFile_StartsFresh_AndSheAsksTheNameAgain()
    {
        File.WriteAllText(PathOf, "{ not json");
        var r = await Ask("""{"id":21,"type":"reset"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal("{ not json", File.ReadAllText(PathOf + ".bad"));
        Assert.True(SettingsStore.Readable(PathOf));
        Assert.False(SettingsStore.Load(PathOf).HelloDone);
        Assert.Equal(1, _engine.HelloAsks);
        Assert.False(r.GetProperty("value").GetProperty("helloDone").GetBoolean());
    }

    [Theory]
    [InlineData("""{"id":22,"type":"radio"}""")]
    [InlineData("""{"id":22}""")]
    public async Task Unknown_Type_IsAnError(string json)
    {
        var r = await Ask(json);
        Assert.Equal(22, r.GetProperty("id").GetInt32());
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-type", r.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NotJson_IsAnError_NeverAThrow()
    {
        var r = await Ask("not json");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("bad-request", r.GetProperty("error").GetString());
    }

    // ---- 0.13 «النشاطات» ----

    private SettingsBridge ActBridge() => new(PathOf, _engine, _locator, () => _now)
    {
        EnginePath = @"C:\Apps\Sahar\Sahar.Engine.exe",
        ClaudeSettingsPath = Path.Combine(_dir, ".claude", "settings.json"),
    };

    private async Task<JsonElement> ActAsk(string json) => JsonDocument.Parse(await ActBridge().HandleAsync(json)).RootElement.Clone();

    [Fact]
    public void Pages_ActivitiesAfterListening() =>
        Assert.Equal(["sahar", "menu", "brain", "models", "you", "today", "prayer", "listen", "activities", "general", "privacy", "about"], SettingsBridge.Pages);   // 0.14: her brain's four after the menu

    [Fact]
    public async Task Claude_LinkStatusUnlink()
    {
        var status = await ActAsk("""{"id":1,"type":"claude-status"}""");
        Assert.False(status.GetProperty("linked").GetBoolean());
        var linked = await ActAsk("""{"id":2,"type":"claude-link"}""");
        Assert.True(linked.GetProperty("ok").GetBoolean());
        Assert.True(linked.GetProperty("linked").GetBoolean());
        Assert.True(ClaudeLinkFileLinked());
        var unlinked = await ActAsk("""{"id":3,"type":"claude-unlink"}""");
        Assert.True(unlinked.GetProperty("ok").GetBoolean());
        Assert.False(unlinked.GetProperty("linked").GetBoolean());
        Assert.False(ClaudeLinkFileLinked());
    }

    private bool ClaudeLinkFileLinked() =>
        Sahar.Core.Activities.ClaudeLinkFile.Linked(Path.Combine(_dir, ".claude", "settings.json"), @"C:\Apps\Sahar\Sahar.Engine.exe");

    [Fact]
    public async Task Claude_Link_UnreadableFile_Refused()
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".claude"));
        File.WriteAllText(Path.Combine(_dir, ".claude", "settings.json"), "{oops");
        var r = await ActAsk("""{"id":1,"type":"claude-link"}""");
        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid", r.GetProperty("error").GetString());
        Assert.Equal("{oops", File.ReadAllText(Path.Combine(_dir, ".claude", "settings.json")));
    }

    [Fact]
    public async Task Claude_NoEnginePath_Refused()
    {
        var r = JsonDocument.Parse(await Bridge().HandleAsync("""{"id":1,"type":"claude-link"}""")).RootElement;
        Assert.False(r.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task WeatherPrayer_ClearsTheWeathersOwnCity()
    {
        SettingsStore.Save(PathOf, new SaharSettings { WeatherCity = "حلب", WeatherLatitude = 36.2, WeatherLongitude = 37.1, WeatherEnabled = true });
        var r = await ActAsk("""{"id":1,"type":"weather-prayer"}""");
        Assert.True(r.GetProperty("ok").GetBoolean());
        var s = SettingsStore.Load(PathOf);
        Assert.Null(s.WeatherCity);
        Assert.Null(s.WeatherLatitude);
        Assert.True(s.WeatherEnabled);
        Assert.True(_engine.Reloads > 0);
    }
}
