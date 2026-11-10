using Sahar.Core.Art;

namespace Sahar.Core.Tests.Art;

/// <summary>The night inside her: Calm exactly as the spec, every other mood from one reviewed hex table
/// (owner ruling 2026-11-10: no colour maths in C#).</summary>
public class NightSkyTests
{
    [Fact]
    public void NightSky_Calm_IsTheSpecExactly()
    {
        var w = NightSky.For(Mood.Calm);
        Assert.Equal((Rgba.Parse("#8E7BE0"), Rgba.Parse("#A897EA"), Rgba.Parse("#CBC1F0")), (w.Inner, w.Mid, w.Outer));
    }

    public static TheoryData<Mood> Moods() => new(Enum.GetValues<Mood>());

    [Theory]
    [MemberData(nameof(Moods))]
    public void NightSky_EveryMood_HasStops(Mood m)
    {
        var w = NightSky.For(m);
        Assert.NotEqual(w.Inner, w.Mid);
    }

    [Theory]
    [MemberData(nameof(Moods))]
    public void NightSky_Outer_IsBottom(Mood m) => Assert.Equal(Palette.Get(m).Bottom, NightSky.For(m).Outer);

    [Fact]
    public void NightSky_Centre_IsTheWashCentre() => Assert.Equal(new System.Numerics.Vector2(120, 196), NightSky.Centre);
}
