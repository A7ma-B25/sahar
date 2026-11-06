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
