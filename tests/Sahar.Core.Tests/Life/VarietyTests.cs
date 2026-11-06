using System.Numerics;
using Sahar.Core.Expressions;
using Sahar.Core.Life;

namespace Sahar.Core.Tests.Life;

public class VarietyTests
{
    private static readonly FlightPattern[] Loops = [FlightPattern.FigureEight, FlightPattern.BigCircle, FlightPattern.Wave, FlightPattern.TaskbarPatrol];

    private static LifeContext Ctx(bool video = false, bool music = false, bool resting = false, int hour = 14) =>
        new(false, video, music, false, false, false, hour, true, resting);

    private static Dictionary<LifeAction, int> Draw(LifeContext c, int n)
    {
        var rng = new Random(9);
        var d = Enum.GetValues<LifeAction>().ToDictionary(a => a, _ => 0);
        for (int i = 0; i < n; i++) d[LifeChooser.Choose(c, _ => 1f, rng)]++;
        return d;
    }

    [Fact]
    public void Flight_LoopsReturnHome()
    {
        foreach (var p in Loops) Assert.True(FlightPath.Offset(p, 1f, 80).Length() < 0.5f, $"{p}");
        foreach (var p in Enum.GetValues<FlightPattern>()) Assert.True(FlightPath.Offset(p, 0f, 80).Length() < 0.01f, $"{p} start");
    }

    [Fact]
    public void Flight_StaysWithinBound()
    {
        foreach (var p in Enum.GetValues<FlightPattern>())
            for (float t = 0; t <= 1.0001f; t += 0.01f)
                Assert.True(FlightPath.Offset(p, t, 80).Length() <= 260.01f, $"{p} at {t}: {FlightPath.Offset(p, t, 80)}");
    }

    [Fact]
    public void Flight_IsSmooth()
    {
        foreach (var p in Enum.GetValues<FlightPattern>())
            for (float t = 0; t < 1f; t += 0.005f)
                Assert.True(Vector2.Distance(FlightPath.Offset(p, t, 80), FlightPath.Offset(p, t + 0.005f, 80)) < 20f, $"{p} jumps at {t}");
    }

    [Fact]
    public void Flight_Seconds_InRange()
    {
        foreach (var p in Enum.GetValues<FlightPattern>()) Assert.InRange(FlightPath.Seconds(p), 3.5f, 7f);
    }

    [Fact]
    public void Moments_FaceForEach()
    {
        foreach (var k in Enum.GetValues<MomentKind>())
        {
            var (_, _, s) = Moments.Face(k);
            Assert.InRange(s, 1f, 3f);
        }
        Assert.Equal((Eyes.Wink, Mouth.Smile), (Moments.Face(MomentKind.Wink).Eyes, Moments.Face(MomentKind.Wink).Mouth));
        Assert.Equal(Mouth.Yawn, Moments.Face(MomentKind.Drowsy).Mouth);
    }

    [Fact]
    public void Choose_BrowserInFront_OnlySmallThings()
    {
        var d = Draw(Ctx(video: true, music: true), 2000);
        Assert.Equal(2000, d[LifeAction.Moment] + d[LifeAction.Breathe]);
        Assert.True(d[LifeAction.Moment] > 0 && d[LifeAction.Breathe] > 0);
    }

    [Fact]
    public void Choose_Dance_OnlyWithMusic()
    {
        Assert.Equal(0, Draw(Ctx(), 2000)[LifeAction.Dance]);
        Assert.True(Draw(Ctx(music: true), 2000)[LifeAction.Dance] > 300);
    }

    [Fact]
    public void Choose_Orbit_OnlyWhenCursorRests()
    {
        Assert.Equal(0, Draw(Ctx(), 2000)[LifeAction.OrbitCursor]);
        Assert.True(Draw(Ctx(resting: true), 2000)[LifeAction.OrbitCursor] > 0);
    }

    [Fact]
    public void Choose_Variety() => Assert.True(Draw(Ctx(), 4000).Count(kv => kv.Value > 0) >= 8);
}

public class CursorRestTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly System.Numerics.Vector2 Her = new(1000, 500);

    [Fact]
    public void Resting_AfterTwoStillSecondsNearHer()
    {
        var c = new CursorRest();
        Assert.False(c.Update(T0, new(1100, 500), Her, 1f));
        Assert.False(c.Update(T0.AddSeconds(1), new(1100, 500), Her, 1f));
        Assert.True(c.Update(T0.AddSeconds(2), new(1100, 500), Her, 1f));
    }

    [Fact]
    public void NotResting_WhenItMoves()
    {
        var c = new CursorRest();
        c.Update(T0, new(1100, 500), Her, 1f);
        c.Update(T0.AddSeconds(1), new(1100, 500), Her, 1f);
        Assert.False(c.Update(T0.AddSeconds(2), new(1150, 500), Her, 1f));
        Assert.False(c.Update(T0.AddSeconds(3), new(1150, 500), Her, 1f));
        Assert.True(c.Update(T0.AddSeconds(4), new(1150, 500), Her, 1f));
    }

    [Fact]
    public void NotResting_WhenFarFromHer_ScaledByDpi()
    {
        var c = new CursorRest();
        c.Update(T0, new(1400, 500), Her, 1f);
        Assert.False(c.Update(T0.AddSeconds(3), new(1400, 500), Her, 1f));   // 400 px at 100 %: too far
        var d = new CursorRest();
        d.Update(T0, new(1400, 500), Her, 1.5f);
        Assert.True(d.Update(T0.AddSeconds(3), new(1400, 500), Her, 1.5f));  // 400 px at 150 % is 267 DIP
    }

    [Fact]
    public void TinyJitter_StillCountsAsResting()
    {
        var c = new CursorRest();
        c.Update(T0, new(1100, 500), Her, 1f);
        c.Update(T0.AddSeconds(1), new(1102, 501), Her, 1f);
        Assert.True(c.Update(T0.AddSeconds(2), new(1101, 500), Her, 1f));
    }
}

public class FlightPlanTests
{
    private static readonly Sahar.Core.Geometry.RectF Work = new(0, 0, 1920, 1040);
    private static Sahar.Core.Geometry.RectF Body(float x, float y) => new(x, y, 100, 100);

    [Fact]
    public void Plan_WithRoom_KeepsTheBoardsShape_TowardMoreRoom()
    {
        Assert.Equal(new Vector2(1, 1), FlightPath.Plan(FlightPattern.Dive, Body(600, 300), Work, 1f, []));
        Assert.Equal(new Vector2(-1, 1), FlightPath.Plan(FlightPattern.Dive, Body(1500, 300), Work, 1f, []));
    }

    [Theory]
    [InlineData(FlightPattern.Dive)]
    [InlineData(FlightPattern.FallingLeaf)]
    public void Plan_AtTheBottom_FliesUpInstead(FlightPattern p) =>
        Assert.Equal(-1, FlightPath.Plan(p, Body(1500, 900), Work, 1f, [])!.Value.Y);

    [Fact]
    public void Plan_AtTheTop_TheCircleIsMirroredDownward() =>
        Assert.Equal(-1, FlightPath.Plan(FlightPattern.BigCircle, Body(900, 20), Work, 1f, [])!.Value.Y);

    [Fact]
    public void Plan_NeverEndsUnderThePointer()
    {
        // A dash to the right would end at x 850..950: the pointer is there.
        var plan = FlightPath.Plan(FlightPattern.DashAndStop, Body(600, 300), Work, 1f, [new(860, 320, 60, 60)]);
        Assert.Equal(-1, plan!.Value.X);
    }

    [Fact]
    public void Plan_FromASeatOnTheTaskbar_StillFlies()
    {
        // Sitting on the taskbar edge (or peeking, or on a window top) her body pokes out of the work area.
        Assert.NotNull(FlightPath.Plan(FlightPattern.TaskbarPatrol, Body(1500, 990), Work, 1f, []));
        Assert.NotNull(FlightPath.Plan(FlightPattern.GlideHigh, Body(1700, 1010), Work, 1f, []));
    }

    [Fact]
    public void Plan_LoopsEndWhereTheyStarted_SoAPointerThereIsFine() =>
        Assert.NotNull(FlightPath.Plan(FlightPattern.FigureEight, Body(900, 400), Work, 1f, [new(920, 420, 40, 40)]));

    [Fact]
    public void Plan_NoRoom_IsNull_ScaledByDpi()
    {
        Assert.Null(FlightPath.Plan(FlightPattern.BigCircle, Body(100, 100), new(0, 0, 300, 300), 1f, []));
        Assert.NotNull(FlightPath.Plan(FlightPattern.GlideHigh, Body(0, 200), new(0, 0, 400, 300), 1f, []));
        Assert.Null(FlightPath.Plan(FlightPattern.GlideHigh, Body(0, 200), new(0, 0, 400, 300), 1.5f, []));
    }
}

public class BrowserProcessTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")]
    [InlineData(@"C:\Program Files\Mozilla Firefox\firefox.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\BraveSoftware\Brave-Browser\Application\BRAVE.EXE")]
    [InlineData(@"C:\Users\a\AppData\Local\Programs\Opera\opera.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\Vivaldi\Application\vivaldi.exe")]
    public void Browsers_AreBrowsers(string path) => Assert.True(MediaEdges.IsBrowserProcess(path));

    [Theory]
    [InlineData(@"C:\Users\a\AppData\Local\Programs\Microsoft VS Code\Code.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\Discord\app-1.0\Discord.exe")]
    [InlineData(@"C:\Users\a\AppData\Local\AnthropicClaude\claude.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\5319275A.WhatsAppDesktop\WhatsApp.exe")]
    [InlineData("")]
    public void ElectronApps_AreNotBrowsers(string path) => Assert.False(MediaEdges.IsBrowserProcess(path));
}
