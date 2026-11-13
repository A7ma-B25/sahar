using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Autonomy;

/// <summary>C2: what she knows of the moment (spec §3.9). Never window titles, clipboard, file names or URLs; the app
/// only with «علّق على شغلك» on and the app not blocked; a song title only with the music learning source on.</summary>
public sealed class SituationRulesTests
{
    private static readonly DateTimeOffset Fri = new(2026, 11, 13, 22, 5, 0, TimeSpan.FromHours(3));

    private static SituationInputs In() => new(Fri)
    {
        NextPrayer = (Sahar.Core.Prayer.Prayer.Isha, TimeSpan.FromMinutes(42.4)),
        TimerLeft = TimeSpan.FromMinutes(12.2),
        Hushed = true,
        AppName = "VS Code", AppExe = "code", AppTime = TimeSpan.FromMinutes(95),
        WindowTitle = "SECRET-TITLE report.docx — https://bank.example/login",
        ClipboardText = "SECRET-CLIP 4111 1111 1111 1111",
        MusicPlaying = true, MusicTitle = "Fairuz — SECRET-SONG",
        ClaudeWorking = 1, ClaudeWaiting = 2,
        Weather = "rain-soon",
        BatteryPercent = 15, Charging = false, HasBattery = true,
        Mood = "focus", HerLinesToday = 4,
    };

    [Fact]
    public void Situation_FromTheMoment()
    {
        var s = SituationRules.From(In(), new SaharSettings { MindLearnMusic = true, WeatherEnabled = true });
        Assert.Equal("22:05", s.LocalTime);
        Assert.Equal("fri", s.Weekday);
        Assert.Equal("isha", s.NextPrayer);
        Assert.Equal(43, s.PrayerInMinutes);
        Assert.Equal(13, s.TimerMinutesLeft);
        Assert.True(s.Hushed);
        Assert.Equal("VS Code", s.App);
        Assert.Equal(95, s.AppMinutes);
        Assert.True(s.MusicPlaying);
        Assert.Equal("Fairuz — SECRET-SONG", s.MusicTitle);
        Assert.Equal((1, 2), (s.ClaudeWorking, s.ClaudeWaiting));
        Assert.Equal("rain-soon", s.Weather);
        Assert.Equal("low", s.Battery);
        Assert.Equal("focus", s.Mood);
        Assert.Equal(4, s.HerLinesToday);
    }

    [Fact]
    public void Situation_NeverTitlesOrClipboard()
    {
        foreach (var settings in new[] { new SaharSettings(), new SaharSettings { MindLearnMusic = true, AwarenessEnabled = true, WeatherEnabled = true } })
        {
            var json = MindWire.Encode(new MindMsg { T = "turn", Id = "1", Situation = SituationRules.From(In(), settings) });
            Assert.DoesNotContain("SECRET-TITLE", json);
            Assert.DoesNotContain("report.docx", json);
            Assert.DoesNotContain("https://", json);
            Assert.DoesNotContain("SECRET-CLIP", json);
            Assert.DoesNotContain("4111", json);
        }
    }

    [Fact]
    public void Situation_AppOnlyWithAwarenessOn()
    {
        Assert.Null(SituationRules.From(In(), new SaharSettings { AwarenessEnabled = false }).App);
        Assert.Null(SituationRules.From(In(), new SaharSettings { AwarenessEnabled = false }).AppMinutes);
        Assert.Equal("VS Code", SituationRules.From(In(), new SaharSettings { AwarenessEnabled = true }).App);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("CODE")]
    public void Situation_BlockedAppNeverNamed(string blocked) =>
        Assert.Null(SituationRules.From(In(), new SaharSettings { BlockedApps = [blocked] }).App);

    [Fact]
    public void Situation_PasswordManagersAndPrivateWindowsNeverNamed()
    {
        Assert.Null(SituationRules.From(In() with { AppName = "KeePassXC", AppExe = "keepassxc" }, new SaharSettings()).App);
        Assert.Null(SituationRules.From(In() with { AppName = "Edge", AppExe = "msedge", WindowTitle = "New tab - InPrivate" }, new SaharSettings()).App);
    }

    [Fact]
    public void Situation_MusicTitleOnlyWithMusicLearningOn()
    {
        var off = SituationRules.From(In(), new SaharSettings { MindLearnMusic = false });
        Assert.True(off.MusicPlaying);
        Assert.Null(off.MusicTitle);
        Assert.DoesNotContain("SECRET-SONG", JsonSerializer.Serialize(off.MusicTitle));
    }

    [Fact]
    public void Situation_WeatherOnlyWhenOn() =>
        Assert.Null(SituationRules.From(In(), new SaharSettings { WeatherEnabled = false }).Weather);

    [Theory]
    [InlineData(true, 80, false, "ok")]
    [InlineData(true, 15, false, "low")]
    [InlineData(true, 15, true, "charging")]
    [InlineData(false, 0, false, "ok")]
    public void Situation_BatteryBand(bool has, int pct, bool charging, string band) =>
        Assert.Equal(band, SituationRules.From(In() with { HasBattery = has, BatteryPercent = pct, Charging = charging }, new SaharSettings()).Battery);
}
