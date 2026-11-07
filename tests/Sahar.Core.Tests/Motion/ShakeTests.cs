using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Life;
using Sahar.Core.Motion;

namespace Sahar.Core.Tests.Motion;

public class ShakeTests
{
    /// <summary>Swings between two x positions: each swing takes <paramref name="swingSeconds"/>, sampled every 16 ms.</summary>
    private static ShakeDetector Swing(int swings, float width, double swingSeconds, float start = 500)
    {
        var d = new ShakeDetector();
        double t = 0;
        float from = start;
        for (int s = 0; s < swings; s++)
        {
            float to = s % 2 == 0 ? start + width : start;
            for (double u = 0; u < swingSeconds; u += 0.016)
            {
                d.Add(t, from + (to - from) * (float)(u / swingSeconds));
                t += 0.016;
            }
            from = to;
        }
        d.Add(t, from);
        return d;
    }

    [Fact]
    public void Shake_FourQuickSwings_Is()
    {
        var d = Swing(6, 160, 0.15);
        Assert.True(d.Shaken);
        Assert.True(d.Strength >= 4);
    }

    [Fact]
    public void Shake_SlowDragBackAndForth_IsNot() => Assert.False(Swing(6, 300, 0.9).Shaken);   // 5 reversals over 5 s

    [Fact]
    public void Shake_SmallJitter_IsNot() => Assert.False(Swing(12, 20, 0.08).Shaken);   // swings under 40 DIP

    [Fact]
    public void Shake_ThreeReversals_IsNot() => Assert.False(Swing(4, 160, 0.15).Shaken);

    [Fact]
    public void Shake_Strength_CountsTheBestWindow()
    {
        var d = Swing(11, 160, 0.15);   // 10 reversals in 1.6 s
        Assert.Equal(10, d.Strength);
        d.Reset();
        Assert.False(d.Shaken);
        Assert.Equal(0, d.Strength);
    }

    [Theory]
    [InlineData(3, 0, 0.0, ShakeReaction.None)]
    [InlineData(5, 1, 0.69, ShakeReaction.Angry)]    // shaken again soon: mostly angry
    [InlineData(5, 1, 0.70, ShakeReaction.Dizzy)]
    [InlineData(8, 0, 0.59, ShakeReaction.Sick)]     // a hard shake: often sick
    [InlineData(8, 0, 0.60, ShakeReaction.Dizzy)]
    [InlineData(5, 0, 0.14, ShakeReaction.Angry)]    // a first, normal shake: rarely angry
    [InlineData(5, 0, 0.15, ShakeReaction.Dizzy)]
    public void ShakeMood_Pick(int strength, int recent, double roll, ShakeReaction expected) =>
        Assert.Equal(expected, ShakeMood.Pick(strength, recent, roll));

    [Fact]
    public void Faces_DizzySickAngry()
    {
        Assert.Equal(30, ExpressionCatalog.All.Count);
        var d = ExpressionCatalog.Get("dizzy");
        Assert.Equal((27, Mood.Calm, Eyes.Dizzy, Mouth.Wobble), (d.Id, d.Mood, d.Eyes, d.Mouth));
        Assert.True(d.Extras.HasFlag(Extras.Stars));
        var s = ExpressionCatalog.Get("sick");
        Assert.Equal((28, Mood.Sick, Eyes.Dizzy, Mouth.Puff), (s.Id, s.Mood, s.Eyes, s.Mouth));
        Assert.True(s.Extras.HasFlag(Extras.Sweat));
        var a = ExpressionCatalog.Get("angry");
        Assert.Equal((29, Mood.Angry, Eyes.Half, Mouth.Puff), (a.Id, a.Mood, a.Eyes, a.Mouth));
        Assert.True(a.Extras.HasFlag(Extras.Steam));
    }

    [Fact]
    public void Moods_SickIsGreen_AngryIsRed()
    {
        Assert.Equal(Rgba.Parse("#6CC25A"), Palette.Get(Mood.Sick).Glow);
        Assert.Equal(Rgba.Parse("#E0322B"), Palette.Get(Mood.Angry).Glow);
    }

    [Fact]
    public void Motions_26To36_HaveShowcaseSteps()
    {
        Assert.True(Enum.IsDefined((MotionKind)36));
        foreach (var k in new[] { "Dizzy", "Angry", "WalkTaskbar", "Stretch", "Yawn", "ScratchHead", "WatchYou", "TypeAlong", "HopWindows", "MoonToss", "ChaseButterfly" })
            Assert.Contains(Showcase.Steps, s => s.Id == k && s.Kind == ShowcaseKind.Motion && !s.Caption.Contains(k));
        Assert.Contains(Showcase.Steps, s => s.Id == "angry" && s.Caption.Contains("معصّبة"));
        Assert.Contains(Showcase.Steps, s => s.Id == "Angry" && s.Caption.Contains("بتعصّب"));
    }
}
