using System.Text.Json;
using Sahar.Core.Menu;
using Sahar.Core.Prayer;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>Every edit the settings page sends is validated here before it is saved (plan 0.10c Task 1).</summary>
public class SettingsPatchTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static SaharSettings Apply(string key, string json, out string? error, SaharSettings? from = null) =>
        SettingsPatch.Apply(from ?? new SaharSettings(), key, J(json), out error);

    // One valid row per key: key, JSON value, a check of the result.
    private static readonly (string Key, string Json)[] Rows =
    [
        ("userName", "\"  Sami  \""),
        ("userGender", "\"female\""),
        ("size", "\"large\""),
        ("activity", "\"lively\""),
        ("awarenessEnabled", "false"),
        ("autoMood", "false"),
        ("playsAlone", "false"),
        ("weatherEnabled", "false"),
        ("innerStars", "false"),
        ("menuItems", "[\"play\",\"radio\",\"play\"]"),
        ("menuHeader", "\"none\""),
        ("menuOpen", "\"both\""),
        ("menuPlace", "\"center\""),
        ("menuShortcut", "\"ctrl+alt+k\""),
        ("appearance", "\"dark\""),
        ("language", "\"english\""),
        ("prayerMethod", "\"muslimWorldLeague\""),
        ("prayerHanafiAsr", "true"),
        ("prayerReminderOn", "false"),
        ("prayerReminderMinutes", "15"),
        ("quranReciter", "\"ar.husary\""),
        ("actMusic", "false"),
        ("actWeather", "false"),
        ("actClaude", "false"),
        ("actShelf", "false"),
        ("actSystem", "false"),
        ("actMicCam", "false"),
        ("actCopy", "false"),
        ("actAway", "false"),
        ("actScreenTime", "true"),
        ("lyricsEnabled", "true"),
        ("lyricsConsented", "true"),
        ("musicPausedKeep", "\"hour\""),
        ("shelfScreenshots", "false"),
        ("mindEnabled", "true"),
        ("mindUnloadMinutes", "15"),
        ("mindInitiative", "\"rare\""),
        ("mindSelfActions", "false"),
        ("mindVoice", "\"self\""),
        ("mindBackend", "\"cuda\""),
        ("mindUseGpu", "false"),
        ("mindLearnChats", "true"),
        ("mindLearnImports", "true"),
        ("mindLearnExamples", "true"),
        ("mindLearnMusic", "true"),
        ("mindEpisodeDays", "7"),
    ];

    public static TheoryData<string, string> Valid
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var (k, j) in Rows) d.Add(k, j);
            return d;
        }
    }

    [Theory]
    [MemberData(nameof(Valid))]
    public void ValidValue_Applies(string key, string json)
    {
        var from = new SaharSettings { WeatherEnabled = true, WeatherLatitude = 36.2, WeatherLongitude = 37.16 };
        var s = Apply(key, json, out var error, from);
        Assert.Null(error);
        Assert.NotEqual(SettingsStore.ToJson(from), SettingsStore.ToJson(s));
    }

    [Fact]
    public void EveryKey_HasAValidRow() =>
        Assert.Equal(SettingsPatch.Keys.OrderBy(k => k), Rows.Select(r => r.Key).OrderBy(k => k));

    [Fact]
    public void Values_LandInTheirFields()
    {
        Assert.Equal(SaharSize.Large, Apply("size", "\"large\"", out _).Size);
        Assert.Equal("Sami", Apply("userName", "\"  Sami  \"", out _).UserName);
        Assert.Null(Apply("userName", "\"\"", out _, new SaharSettings { UserName = "Sami" }).UserName);
        Assert.Equal(Gender.Female, Apply("userGender", "\"female\"", out _).UserGender);
        Assert.Equal(ActivityLevel.Lively, Apply("activity", "\"lively\"", out _).Activity);
        Assert.Equal(["play"], Apply("menuItems", "[\"play\",\"radio\",\"play\"]", out _).MenuItems!);
        Assert.Equal(MenuHeader.None, Apply("menuHeader", "\"none\"", out _).MenuHeader);
        Assert.Equal(MenuOpen.Both, Apply("menuOpen", "\"both\"", out _).MenuOpen);
        Assert.Equal(MenuPlace.Center, Apply("menuPlace", "\"center\"", out _).MenuPlace);
        Assert.Equal("Ctrl+Alt+K", Apply("menuShortcut", "\"ctrl+alt+k\"", out _).MenuShortcut);
        Assert.Equal(Appearance.Dark, Apply("appearance", "\"dark\"", out _).Appearance);
        Assert.Equal(UiLanguage.English, Apply("language", "\"english\"", out _).Language);
        Assert.Equal(PrayerMethod.MuslimWorldLeague, Apply("prayerMethod", "\"muslimWorldLeague\"", out _).PrayerMethod);
        Assert.True(Apply("prayerHanafiAsr", "true", out _).PrayerHanafiAsr);
        Assert.False(Apply("prayerReminderOn", "false", out _).PrayerReminderOn);
        Assert.Equal(15, Apply("prayerReminderMinutes", "15", out _).PrayerReminderMinutes);
        Assert.Equal("ar.husary", Apply("quranReciter", "\"ar.husary\"", out _).QuranReciter);
        Assert.False(Apply("innerStars", "false", out _).InnerStars);
        Assert.False(Apply("autoMood", "false", out _).AutoMood);
        Assert.False(Apply("playsAlone", "false", out _).PlaysAlone);
        Assert.False(Apply("awarenessEnabled", "false", out _).AwarenessEnabled);
    }

    [Fact]
    public void AnEmptyMenu_IsAllowed() => Assert.Empty(Apply("menuItems", "[]", out _).MenuItems!);

    [Fact]
    public void UserName_IsCleaned_To20Characters() =>
        Assert.Equal(20, Apply("userName", "\"" + new string('س', 30) + "\"", out _).UserName!.Length);

    [Theory]
    [InlineData("Ctrl+C", Shortcuts.AppsCommon)]
    [InlineData("Win+K", Shortcuts.WindowsOwn)]
    [InlineData("K", Shortcuts.NeedsCtrlOrAlt)]
    public void MenuShortcut_Refused_SaysWhy_AndKeepsTheOld(string value, string reason)
    {
        var before = new SaharSettings { MenuShortcut = "Ctrl+Alt+S" };
        var s = Apply("menuShortcut", $"\"{value}\"", out var error, before);
        Assert.Equal(reason, error);
        Assert.Equal(before, s);
    }

    [Fact]
    public void Weather_On_WithoutACity_NeedsOne()
    {
        var before = new SaharSettings();
        Assert.Equal(before, Apply("weatherEnabled", "true", out var error, before));
        Assert.Equal("needs-city", error);
        var withCity = new SaharSettings { WeatherCity = "حلب", WeatherLatitude = 36.2, WeatherLongitude = 37.16 };
        Assert.True(Apply("weatherEnabled", "true", out error, withCity).WeatherEnabled);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("prayerReminderMinutes", "12")]
    [InlineData("prayerReminderMinutes", "\"15\"")]
    [InlineData("quranReciter", "\"ar.nobody\"")]
    [InlineData("size", "\"huge\"")]
    [InlineData("size", "\"1\"")]
    [InlineData("size", "2")]
    [InlineData("userGender", "\"other\"")]
    [InlineData("innerStars", "\"yes\"")]
    [InlineData("menuItems", "\"play\"")]
    [InlineData("menuItems", "[1]")]
    [InlineData("userName", "42")]
    [InlineData("prayerMethod", "\"tehran\"")]
    public void BadValue_IsRefused_AndNothingChanges(string key, string json)
    {
        var before = new SaharSettings();
        Assert.Equal(before, Apply(key, json, out var error, before));
        Assert.Equal("bad-value", error);
    }

    [Theory]
    [InlineData("radio")]
    [InlineData("helloDone")]
    [InlineData("Size")]
    public void UnknownKey_IsRefused(string key)
    {
        var before = new SaharSettings();
        Assert.Equal(before, Apply(key, "true", out var error, before));
        Assert.Equal("bad-key", error);
    }

    [Fact]
    public void Patch_ActivityKeys_LandInTheirFields()
    {
        Assert.False(Apply("actClaude", "false", out _).ActClaude);
        Assert.True(Apply("actScreenTime", "true", out _).ActScreenTime);
        Assert.True(Apply("lyricsConsented", "true", out _).LyricsConsented);
        Assert.False(Apply("shelfScreenshots", "false", out _).ShelfScreenshots);
        Assert.Equal(Sahar.Core.Activities.PausedKeep.Always, Apply("musicPausedKeep", "\"always\"", out _).MusicPausedKeep);
    }

    [Theory]
    [InlineData("musicPausedKeep", "\"forever\"")]
    [InlineData("musicPausedKeep", "2")]
    [InlineData("actMusic", "\"no\"")]
    [InlineData("lyricsEnabled", "1")]
    public void Patch_ActivityKeys_BadValues(string key, string json)
    {
        Apply(key, json, out var error);
        Assert.Equal(SettingsPatch.BadValue, error);
    }

    [Fact]
    public void Patch_NoClaudeAnswerKeys() =>
        // Owner ruling 3 (2026-11-13): notify only, so there is nothing to answer from Sahar and no wait to set.
        Assert.DoesNotContain(SettingsPatch.Keys, k => k.Contains("Answer") || k.Contains("Wait"));

    [Fact]
    public void Patch_Weather_PrayerCityIsEnough()
    {
        // Owner ruling 4: the weather falls back to the prayer place.
        var prayerOnly = new SaharSettings { PrayerCity = "دمشق", PrayerLatitude = 33.5, PrayerLongitude = 36.3 };
        Assert.True(Apply("weatherEnabled", "true", out var error, prayerOnly).WeatherEnabled);
        Assert.Null(error);
    }

    [Fact]
    public void Defaults_AllOnButScreenTimeAndLyrics()
    {
        var s = new SaharSettings();
        Assert.True(s.ActMusic && s.ActWeather && s.ActClaude && s.ActShelf && s.ActSystem && s.ActMicCam && s.ActCopy && s.ActAway && s.ShelfScreenshots);
        Assert.False(s.ActScreenTime || s.LyricsEnabled || s.LyricsConsented);
        Assert.Equal(Sahar.Core.Activities.PausedKeep.TenMinutes, s.MusicPausedKeep);
    }

    [Fact]
    public void OldFile_KeepsActivityDefaults()
    {
        var s = SettingsStore.Load("settings.json", _ => """{"version":1,"size":"Large"}""");
        Assert.True(s.ActMusic && s.ActAway && s.ShelfScreenshots);
        Assert.False(s.ActScreenTime);
        Assert.Equal(Sahar.Core.Activities.PausedKeep.TenMinutes, s.MusicPausedKeep);
    }
}
