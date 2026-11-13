using Sahar.Core.Settings;
using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

public class UpdateScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly UpdateOffer Offer = new(new SemVersion(0, 12, 0, null),
        new ReleaseAsset("Sahar-Setup-0.12.0.exe", "https://github.com/a", 1), new ReleaseAsset("Sahar-Setup-0.12.0.exe.sha256", "https://github.com/b", 1));

    [Fact]
    public void FirstCheck_TwoMinutesAfterStart()
    {
        var s = new UpdateSchedule(T0);
        Assert.False(s.Due(T0 + TimeSpan.FromSeconds(119)));
        Assert.True(s.Due(T0 + TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void ThenEveryTwelveHours()
    {
        var s = new UpdateSchedule(T0);
        var at = T0 + TimeSpan.FromMinutes(2);
        s.Checked(at);
        Assert.False(s.Due(at + TimeSpan.FromHours(11.9)));
        Assert.True(s.Due(at + TimeSpan.FromHours(12)));
    }

    [Fact]
    public void Failures_BackOff_CappedAtTwelveHours_ResetBySuccess()
    {
        var s = new UpdateSchedule(T0);
        var now = T0;
        var gaps = new List<TimeSpan>();
        for (int i = 0; i < 8; i++)
        {
            s.Failed(now);
            gaps.Add(s.NextCheck - now);
            now = s.NextCheck;
        }
        Assert.Equal([15, 30, 60, 120, 240, 480, 720, 720], gaps.Select(g => (int)g.TotalMinutes));
        s.Checked(now);
        s.Failed(now);
        Assert.Equal(TimeSpan.FromMinutes(15), s.NextCheck - now);
    }

    [Fact]
    public void Busy_TriesAgainInTenMinutes()
    {
        var s = new UpdateSchedule(T0);
        s.Busy(T0);
        Assert.Equal(T0 + TimeSpan.FromMinutes(10), s.NextCheck);
    }

    [Theory]
    // offer, manual, busy, snoozed → step
    [InlineData(true, false, false, false, UpdateStep.Ask)]
    [InlineData(true, false, true, false, UpdateStep.Later)]
    [InlineData(true, false, false, true, UpdateStep.Snoozed)]
    [InlineData(true, false, true, true, UpdateStep.Snoozed)]
    [InlineData(false, false, false, false, UpdateStep.Nothing)]
    [InlineData(false, false, true, true, UpdateStep.Nothing)]
    [InlineData(true, true, false, false, UpdateStep.Ask)]
    [InlineData(true, true, true, true, UpdateStep.Ask)]   // the tray item: you asked, so she answers
    [InlineData(false, true, false, false, UpdateStep.UpToDate)]
    [InlineData(false, true, true, true, UpdateStep.UpToDate)]
    public void Decide(bool offer, bool manual, bool busy, bool snoozed, UpdateStep expected)
    {
        DateTimeOffset? askAfter = snoozed ? T0 + TimeSpan.FromHours(1) : T0 - TimeSpan.FromHours(1);
        Assert.Equal(expected, UpdateSchedule.Decide(offer ? Offer : null, manual, busy, T0, askAfter));
    }

    [Fact]
    public void NeverAsked_IsNotSnoozed() => Assert.Equal(UpdateStep.Ask, UpdateSchedule.Decide(Offer, false, false, T0, null));

    [Fact]
    public void Asking_SnoozesForADay()
    {
        var after = UpdateSchedule.AskedAt(T0);
        Assert.Equal(T0 + TimeSpan.FromHours(24), after);
        Assert.Equal(UpdateStep.Snoozed, UpdateSchedule.Decide(Offer, false, false, T0 + TimeSpan.FromHours(23), after));
        Assert.Equal(UpdateStep.Ask, UpdateSchedule.Decide(Offer, false, false, T0 + TimeSpan.FromHours(24), after));
    }

    [Theory]
    [InlineData(UpdateChannel.TestBuild, null, "https://api.github.com/repos/A7ma-B25/sahar/releases/tags/test-build")]
    [InlineData(UpdateChannel.Stable, null, "https://api.github.com/repos/A7ma-B25/sahar/releases/latest")]
    [InlineData(UpdateChannel.TestBuild, "http://127.0.0.1:8765", "http://127.0.0.1:8765/repos/A7ma-B25/sahar/releases/tags/test-build")]
    [InlineData(UpdateChannel.TestBuild, "http://127.0.0.1:8765/", "http://127.0.0.1:8765/repos/A7ma-B25/sahar/releases/tags/test-build")]
    public void ReleaseApi(UpdateChannel channel, string? apiBase, string expected) =>
        Assert.Equal(expected, UpdateRules.ReleaseApi(channel, apiBase).ToString());

    [Fact]
    public void UserAgent_IsSaharAndTheVersion_Only() => Assert.Equal($"Sahar/{CoreInfo.Version}", UpdateRules.UserAgent);

    [Fact]
    public void Settings_Defaults_TestBuildChannel_NeverAsked()
    {
        var s = new SaharSettings();
        Assert.Equal(UpdateChannel.TestBuild, s.UpdateChannel);
        Assert.Null(s.UpdateAskAfter);
        Assert.True(s.UpdateCheckEnabled);
    }

    [Fact]
    public void Settings_RoundTrip_AndAnOldFileGetsTheDefaults()
    {
        var dir = Directory.CreateTempSubdirectory("sahar-update-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            SettingsStore.Save(path, new SaharSettings { UpdateChannel = UpdateChannel.Stable, UpdateAskAfter = T0 });
            var text = File.ReadAllText(path);
            Assert.Contains("\"updateChannel\": \"Stable\"", text);
            var back = SettingsStore.Load(path);
            Assert.Equal(UpdateChannel.Stable, back.UpdateChannel);
            Assert.Equal(T0, back.UpdateAskAfter);

            File.WriteAllText(path, "{\"version\":1,\"helloDone\":true}");
            var old = SettingsStore.Load(path);
            Assert.Equal(UpdateChannel.TestBuild, old.UpdateChannel);
            Assert.Null(old.UpdateAskAfter);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
