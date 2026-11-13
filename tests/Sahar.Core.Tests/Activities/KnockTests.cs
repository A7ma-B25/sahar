using Sahar.Core.Activities;
using Sahar.Core.Brain;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Activities;

public class KnockTests
{
    [Theory]
    [InlineData(ActivityKind.ClaudeAsk, null, FullscreenAct.Knock)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Storm, FullscreenAct.Knock)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Wind, FullscreenAct.Knock)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Heat, FullscreenAct.Knock)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Rain, FullscreenAct.Keep)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Frost, FullscreenAct.Keep)]
    [InlineData(ActivityKind.WeatherAlert, WeatherAlert.Air, FullscreenAct.Keep)]
    [InlineData(ActivityKind.BatteryLow, null, FullscreenAct.Knock)]
    [InlineData(ActivityKind.ClaudeDone, null, FullscreenAct.Keep)]
    [InlineData(ActivityKind.CopyDone, null, FullscreenAct.Keep)]
    [InlineData(ActivityKind.Music, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.ClaudeWorking, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.Copy, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.Shelf, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.Volume, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.ScreenTime, null, FullscreenAct.Nothing)]
    [InlineData(ActivityKind.Screenshot, null, FullscreenAct.Nothing)]
    public void Decide_OnHerMonitor(ActivityKind kind, WeatherAlert? alert, FullscreenAct act) =>
        Assert.Equal(act, Knock.Decide(kind, FullscreenWhere.Hers, alert));

    [Theory]
    [InlineData(ActivityKind.ClaudeAsk)]
    [InlineData(ActivityKind.Music)]
    [InlineData(ActivityKind.Volume)]
    public void Decide_ElsewhereOrNone_Show(ActivityKind kind)
    {
        Assert.Equal(FullscreenAct.Show, Knock.Decide(kind, FullscreenWhere.Elsewhere));
        Assert.Equal(FullscreenAct.Show, Knock.Decide(kind, FullscreenWhere.None));
    }

    [Fact]
    public void Sounds_OnlyWithNothingFullscreen()
    {
        Assert.True(Knock.Sounds(FullscreenWhere.None));
        Assert.False(Knock.Sounds(FullscreenWhere.Elsewhere));
        Assert.False(Knock.Sounds(FullscreenWhere.Hers));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void KnockRect_TopCentre_NeverWholeMonitor(float dpi)
    {
        var m = new RectI(1920, 0, 1920 + 2560, 1440);
        var r = Knock.Rect(m, new SizeI((int)(200 * dpi), (int)(40 * dpi)), dpi);
        Assert.Equal(m.Top, r.Top);
        Assert.InRange((r.Left + r.Right) / 2, m.Left + m.Width / 2 - 1, m.Left + m.Width / 2 + 1);
        Assert.False(OverlayPlan.Covers(r, m));
        Assert.True(r.Left >= m.Left && r.Right <= m.Right && r.Bottom < m.Bottom);
        Assert.True(r.Height >= (int)(40 * dpi));
    }

    [Fact]
    public void KnockRect_TinyMonitor_StillShort() =>
        Assert.False(OverlayPlan.Covers(Knock.Rect(new RectI(0, 0, 100, 50), new SizeI(300, 80), 1), new RectI(0, 0, 100, 50)));

    [Fact]
    public void KnockLasts_FourSeconds() => Assert.Equal(TimeSpan.FromSeconds(4), Knock.Length);
}
