using Sahar.Core.Design;
using Sahar.Core.Diagnostics;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>Review fix pass (2026-11-13): small rules the two apps follow.</summary>
public class SettingsAppRulesTests
{
    [Fact]
    public void BrowserArguments_StopBackgroundTraffic_InProductionToo()
    {
        foreach (var test in new[] { false, true })
        {
            var args = SettingsAppPolicy.BrowserArguments(test);
            foreach (var flag in new[] { "--disable-background-networking", "--disable-component-update", "--disable-domain-reliability",
                         "--disable-sync", "--no-pings", "--disable-client-side-phishing-detection" })
                Assert.Contains(flag, args);
        }
        Assert.DoesNotContain("--disable-renderer-backgrounding", SettingsAppPolicy.BrowserArguments(false));
        Assert.Contains("--disable-renderer-backgrounding", SettingsAppPolicy.BrowserArguments(true));
        foreach (var test in new[] { false, true })   // Chromium keeps only the last --disable-features
            Assert.Single(SettingsAppPolicy.BrowserArguments(test).Split(' '), a => a.StartsWith("--disable-features=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Appearance.Dark, false, true)]
    [InlineData(Appearance.Light, true, false)]
    [InlineData(Appearance.System, true, true)]
    [InlineData(Appearance.System, false, false)]
    public void StartLook_FollowsTheSettingThenWindows(Appearance a, bool systemDark, bool dark)
    {
        Assert.Equal(dark, SettingsAppPolicy.IsDark(a, systemDark));
        Assert.Equal(Tokens.For(dark).Bg, SettingsAppPolicy.Background(a, systemDark));
    }

    [Fact]
    public void StartFailure_SaysWebView2_OnlyWhenItIsMissing()
    {
        Assert.Contains("WebView2", SettingsAppPolicy.StartFailed(runtimeMissing: true));
        Assert.DoesNotContain("WebView2", SettingsAppPolicy.StartFailed(runtimeMissing: false));
    }

    [Theory]
    [InlineData(true, "idle", Cheer.None)]
    [InlineData(false, "idle", Cheer.HopOrFace)]
    [InlineData(false, "music", Cheer.HopOrFace)]
    [InlineData(false, "sick", Cheer.None)]
    [InlineData(false, "sad", Cheer.None)]
    [InlineData(false, "error", Cheer.None)]
    public void SettingsCheer_NeverForcesJoy_InTheAdhanOrOverAnotherFace(bool adhan, string expression, Cheer want) =>
        Assert.Equal(want, SettingsAppPolicy.CheerFor(adhan, expression));

    [Fact]
    public void LogDescribe_NeverTheMessage()
    {
        Exception e;
        try { throw new FileNotFoundException(@"C:\Users\Sami\AppData\Roaming\Sahar\notes.json", new IOException("Sami")); }
        catch (Exception x) { e = x; }
        var d = LogRules.Describe(e);
        Assert.DoesNotContain("Sami", d);
        Assert.Contains("FileNotFoundException", d);
        Assert.Contains("IOException", d);
        Assert.Equal("FileNotFoundException ← IOException", LogRules.Kind(e));
    }
}
