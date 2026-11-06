using Sahar.Core.Senses;

namespace Sahar.Core.Tests.Senses;

public class ClipboardRulesTests
{
    [Fact]
    public void ClipboardRules_SkipsExcludedFormats()
    {
        Assert.False(ClipboardRules.ShouldKeep(["ExcludeClipboardContentFromMonitorProcessing"], null, "notepad.exe"));
        Assert.False(ClipboardRules.ShouldKeep(["Clipboard Viewer Ignore"], null, "notepad.exe"));
        Assert.True(ClipboardRules.ShouldKeep(["CF_UNICODETEXT"], null, "notepad.exe"));
    }

    [Fact]
    public void ClipboardRules_SkipsHistoryZero()
    {
        Assert.False(ClipboardRules.ShouldKeep([], 0, "notepad.exe"));
        Assert.True(ClipboardRules.ShouldKeep([], 1, "notepad.exe"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\KeePass Password Safe 2\KeePass.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\1Password\app\8\1Password.exe")]
    [InlineData(@"C:\Program Files\Bitwarden\Bitwarden.exe")]
    public void ClipboardRules_SkipsPasswordManagers(string owner) => Assert.False(ClipboardRules.ShouldKeep([], null, owner));

    [Fact]
    public void ClipboardRules_LinkVsText()
    {
        Assert.Equal(ClipKind.Link, ClipboardRules.KindOf("https://boomhub.io/x"));
        Assert.Equal(ClipKind.Link, ClipboardRules.KindOf("  www.example.com "));
        Assert.Equal(ClipKind.Text, ClipboardRules.KindOf("مرحبا https://x.y"));
    }
}

public class ClipboardHistoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-clip-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void ClipboardHistory_Keeps20NewestFirst()
    {
        var h = new ClipboardHistory(_dir);
        for (int i = 0; i < 25; i++) h.AddText($"t{i}", T0.AddSeconds(i));
        Assert.Equal(20, h.Items.Count);
        Assert.Equal("t24", h.Items[0].Text);
        Assert.Equal("t5", h.Items[19].Text);
    }

    [Fact]
    public void ClipboardHistory_CollapsesConsecutiveDuplicates()
    {
        var h = new ClipboardHistory(_dir);
        Assert.True(h.AddText("a", T0));
        Assert.False(h.AddText("a", T0.AddSeconds(1)));
        Assert.True(h.AddText("b", T0.AddSeconds(2)));
        Assert.True(h.AddText("a", T0.AddSeconds(3)));
        Assert.Equal(["a", "b", "a"], h.Items.Select(i => i.Text));
    }

    [Fact]
    public void ClipboardHistory_ImagesAndClear()
    {
        var h = new ClipboardHistory(_dir);
        Assert.True(h.AddImage(new byte[40 + 4], 1, 1, T0));
        var file = h.Items[0].ImageFile!;
        Assert.True(File.Exists(file));
        Assert.Equal(ClipKind.Image, h.Items[0].Kind);
        h.Clear();
        Assert.Empty(h.Items);
        Assert.False(File.Exists(file));
        Assert.Empty(new ClipboardHistory(_dir).Items);
    }

    [Fact]
    public void ClipboardHistory_TooBig_NotKept()
    {
        var h = new ClipboardHistory(_dir);
        Assert.False(h.AddImage(new byte[17 * 1024 * 1024], 3000, 2000, T0));
        Assert.False(h.AddText(new string('x', 100_001), T0));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void ClipboardHistory_SurvivesRestart()
    {
        new ClipboardHistory(_dir).AddText("نسخت هاد", T0);
        Assert.Equal("نسخت هاد", Assert.Single(new ClipboardHistory(_dir).Items).Text);
    }

    [Fact]
    public void ClipboardHistory_UnreadableNeverOverwritten()
    {
        new ClipboardHistory(_dir).AddText("old", T0);
        var path = Path.Combine(_dir, "clipboard.json");
        ClipboardHistory h;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            h = new ClipboardHistory(_dir);
            h.AddText("new", T0.AddSeconds(1));
        }
        h.AddText("later", T0.AddSeconds(2));
        Assert.Equal(["later", "new", "old"], new ClipboardHistory(_dir).Items.Select(i => i.Text));
    }
}

public class ScreenshotKeysTests
{
    private const int Win = 0x5B, Shift = 0x10, S = 0x53, PrintScreen = 0x2C;

    [Fact]
    public void ScreenshotKeys_PrintScreen() => Assert.True(new ScreenshotKeys().Update(PrintScreen, true));

    [Fact]
    public void ScreenshotKeys_WinShiftS()
    {
        var k = new ScreenshotKeys();
        Assert.False(k.Update(Win, true));
        Assert.False(k.Update(Shift, true));
        Assert.True(k.Update(S, true));
        Assert.False(k.Update(S, true));   // key repeat
    }

    [Fact]
    public void ScreenshotKeys_PlainS_No()
    {
        var k = new ScreenshotKeys();
        Assert.False(k.Update(S, true));
        k.Update(Shift, true);
        Assert.False(k.Update(S, true));
        k.Update(Shift, false);
        k.Update(Win, true);
        k.Update(Win, false);
        k.Update(Shift, true);
        Assert.False(k.Update(S, true));
    }
}

public class DownloadRulesTests
{
    [Theory]
    [InlineData("file.zip.crdownload", true)]
    [InlineData("movie.mp4.part", true)]
    [InlineData("a.partial", true)]
    [InlineData("x.download", true)]
    [InlineData("abc.tmp", true)]
    [InlineData("~$report.docx", true)]
    [InlineData("report.pdf", false)]
    public void DownloadRules_TempNames(string name, bool temp) => Assert.Equal(temp, DownloadRules.IsTemp(name));

    [Fact]
    public void DownloadRules_RenameFromTemp_Finished() => Assert.True(DownloadRules.FinishedByRename("a.zip.crdownload", "a.zip"));

    [Fact]
    public void DownloadRules_RenameBetweenFinals_No()
    {
        Assert.False(DownloadRules.FinishedByRename("a.zip", "b.zip"));
        Assert.False(DownloadRules.FinishedByRename("a.crdownload", "b.crdownload"));
    }
}

public class CpuHeatTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CpuHeat_TwoMinutesAbove85_Once()
    {
        var c = new CpuHeat();
        int fired = 0;
        for (int s = 0; s <= 300; s += 10) if (c.Update(T0.AddSeconds(s), 92)) fired++;
        Assert.Equal(1, fired);
        var d = new CpuHeat();
        Assert.False(d.Update(T0, 92));
        Assert.False(d.Update(T0.AddSeconds(110), 92));
        Assert.True(d.Update(T0.AddSeconds(120), 92));
    }

    [Fact]
    public void CpuHeat_ADipResets() 
    {
        var c = new CpuHeat();
        c.Update(T0, 92);
        c.Update(T0.AddSeconds(60), 50);
        Assert.False(c.Update(T0.AddSeconds(130), 92));
    }

    [Fact]
    public void CpuHeat_Rearms()
    {
        var c = new CpuHeat();
        for (int s = 0; s <= 120; s += 10) c.Update(T0.AddSeconds(s), 92);
        for (int s = 130; s <= 260; s += 10) c.Update(T0.AddSeconds(s), 40);
        bool again = false;
        for (int s = 270; s <= 400; s += 10) again |= c.Update(T0.AddSeconds(s), 95);
        Assert.True(again);
    }
}

public class CapabilityUseTests
{
    [Fact]
    public void CapabilityUse_ZeroStopMeansInUse()
    {
        Assert.True(CapabilityUse.InUse([133400000000000000, 0]));
        Assert.False(CapabilityUse.InUse([133400000000000000]));
        Assert.False(CapabilityUse.InUse([]));
    }
}

public class WeatherRulesTests
{
    [Fact]
    public void WeatherRules_RainCodes()
    {
        Assert.True(WeatherRules.RainSoon([0, 61], [0, 0]));
        Assert.True(WeatherRules.RainSoon([95], [0]));
        Assert.False(WeatherRules.RainSoon([0, 1, 3], [10, 20, 30]));
    }

    [Fact]
    public void WeatherRules_Probability() => Assert.True(WeatherRules.RainSoon([2, 3], [20, 70]));

    [Fact]
    public void WeatherRules_Hourly()
    {
        var t = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        Assert.True(WeatherRules.ShouldFetch(t, null));
        Assert.False(WeatherRules.ShouldFetch(t, t.AddMinutes(-30)));
        Assert.True(WeatherRules.ShouldFetch(t, t.AddMinutes(-61)));
    }
}

public class AppTimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void AppTime_ContinuousOnOneApp()
    {
        var a = new AppTime();
        a.Front("Code.exe", T0);
        Assert.Equal(TimeSpan.FromMinutes(90), a.Continuous(T0.AddMinutes(90)));
        Assert.Equal("Code.exe", a.Current);
    }

    [Fact]
    public void AppTime_SwitchResets()
    {
        var a = new AppTime();
        a.Front("Code.exe", T0);
        a.Front("chrome.exe", T0.AddMinutes(50));
        Assert.Equal(TimeSpan.FromMinutes(10), a.Continuous(T0.AddMinutes(60)));
    }

    [Fact]
    public void AppTime_ShortSwitchKeepsTheRun()
    {
        var a = new AppTime();
        a.Front("Code.exe", T0);
        a.Front("explorer.exe", T0.AddMinutes(50));
        a.Front("Code.exe", T0.AddMinutes(50.5));
        Assert.Equal(TimeSpan.FromMinutes(60), a.Continuous(T0.AddMinutes(60)));
    }

    [Fact]
    public void AppTime_AwayPauses()
    {
        var a = new AppTime();
        a.Front("Code.exe", T0);
        a.Away(T0.AddMinutes(30));
        a.Front("Code.exe", T0.AddMinutes(90));
        Assert.Equal(TimeSpan.FromMinutes(10), a.Continuous(T0.AddMinutes(100)));
    }

    [Fact]
    public void AppTime_NewDayResets()
    {
        var a = new AppTime();
        a.Front("Code.exe", T0.AddHours(14.5));   // 23:30
        Assert.True(a.Continuous(T0.AddHours(15.5)) <= TimeSpan.FromMinutes(60));
        Assert.True(a.FirstToday(T0.AddDays(1)));
    }

    [Fact]
    public void AppTime_BlockedAppsNotTracked()
    {
        var a = new AppTime();
        a.Front(@"C:\Program Files\KeePass Password Safe 2\KeePass.exe", T0);
        Assert.Null(a.Current);
    }
}

public class CommentRulesTests
{
    [Fact]
    public void CommentRules_Lines()
    {
        Assert.Equal("صار لك ساعتين على VS Code، كيف ماشي؟", CommentRules.Pick("Code.exe", TimeSpan.FromMinutes(125), 15, false));
        Assert.Equal("ساعة كاملة على Word… خود نفس 🌿", CommentRules.Pick("WINWORD.EXE", TimeSpan.FromMinutes(65), 15, false));
        Assert.Equal("صباح الخير ☀️", CommentRules.Pick("Code.exe", TimeSpan.FromMinutes(1), 9, true));
        Assert.Null(CommentRules.Pick("Code.exe", TimeSpan.FromMinutes(20), 15, false));
        Assert.Equal("صار لك ساعتين على نفس البرنامج، كيف ماشي؟", CommentRules.Pick("mystery.exe", TimeSpan.FromMinutes(130), 15, false));
    }

    [Fact]
    public void CommentRules_NotWhileBusy()
    {
        Assert.True(CommentRules.Allowed(typing: false, quiet: false, menuOpen: false, hidden: false));
        Assert.False(CommentRules.Allowed(typing: true, quiet: false, menuOpen: false, hidden: false));
        Assert.False(CommentRules.Allowed(typing: false, quiet: true, menuOpen: false, hidden: false));
        Assert.False(CommentRules.Allowed(typing: false, quiet: false, menuOpen: true, hidden: false));
        Assert.False(CommentRules.Allowed(typing: false, quiet: false, menuOpen: false, hidden: true));
    }

    [Fact]
    public void PrivacyLists_PrivateWindows()
    {
        Assert.True(PrivacyLists.IsPrivateWindow("New Tab - Google Chrome (Incognito)"));
        Assert.True(PrivacyLists.IsPrivateWindow("InPrivate - Microsoft Edge"));
        Assert.False(PrivacyLists.IsPrivateWindow("Inbox - Outlook"));
    }
}
