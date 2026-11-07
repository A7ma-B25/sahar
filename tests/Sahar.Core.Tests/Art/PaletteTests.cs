using Sahar.Core.Art;

namespace Sahar.Core.Tests.Art;

public class PaletteTests
{
    [Fact]
    public void Rgba_Parse_Hex()
    {
        Assert.Equal(new Rgba(0xF1, 0xEC, 0xFF), Rgba.Parse("#F1ECFF"));
        Assert.Equal(new Rgba(1, 2, 3, 4), Rgba.Parse("#01020304"));
        Assert.Throws<FormatException>(() => Rgba.Parse("F1ECFF"));
    }

    [Fact]
    public void Palette_Calm_MatchesDesign()
    {
        var c = Palette.Get(Mood.Calm);
        Assert.Equal(Rgba.Parse("#FFFFFF"), c.Top);
        Assert.Equal(Rgba.Parse("#F1ECFF"), c.Mid);
        Assert.Equal(Rgba.Parse("#CBC1F0"), c.Bottom);
        Assert.Equal(Rgba.Parse("#B9B0FF"), c.Glow);
        Assert.Equal(Rgba.Parse("#C9B8FF"), c.Bud);
        Assert.Equal(Rgba.Parse("#FFB8A0"), c.Blush);
    }

    [Fact]
    public void Palette_AllFourteenMoods_Defined()
    {
        Assert.Equal(16, Enum.GetValues<Mood>().Length);
        foreach (var m in Enum.GetValues<Mood>()) Assert.NotNull(Palette.Get(m));
        Assert.Equal(Rgba.Parse("#7C69C9"), Palette.Get(Mood.Mystic).Mid);
        Assert.Equal(Rgba.Parse("#8E88B8"), Palette.Get(Mood.Sleepy).Bottom);
        Assert.Equal(Rgba.Parse("#FF8A5C"), Palette.Get(Mood.Hot).Glow);
    }
}
