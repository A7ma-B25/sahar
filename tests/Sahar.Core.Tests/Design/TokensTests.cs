using Sahar.Core.Art;
using Sahar.Core.Design;

namespace Sahar.Core.Tests.Design;

public class TokensTests
{
    [Fact]
    public void Light_IsSpec7()
    {
        var t = Tokens.Light;
        Assert.Equal("#F5F4F8 #FFFFFF #FFFFFF #ECEAF2 #F1EFF6 #E8E5F0 #24212B #5C5867 #6B6776 #8F8A9B #5B47C4 #FFFFFF #ECE7FF #4636A6 #7A5A1C #2F7A4A #C2363B #9A5418 #B9A4F0",
            string.Join(' ', Hexes(t)));
    }

    [Fact]
    public void Dark_IsSpec7()
    {
        var t = Tokens.Dark;
        Assert.Equal("#18161C #221F27 #2A2630 #1C1A20 #353140 #3D3848 #EFECF4 #B8B3C2 #A29DAD #7D7889 #B6A9FF #1C1733 #3A3354 #D2C9FF #F2C86A #8ED3A0 #FF8A8D #F2A06E #E9D9A6",
            string.Join(' ', Hexes(t)));
        Assert.Same(Tokens.Dark, Tokens.For(true));
    }

    private static IEnumerable<string> Hexes(ThemeTokens t) =>
        new[] { t.Bg, t.Surface, t.Menu, t.Sunken, t.Hover, t.Pressed, t.Ink, t.Text2, t.Text3, t.Boundary, t.Accent, t.OnAccent,
            t.AccentSoft, t.AccentInk, t.Moon, t.Leaf, t.Error, t.Warn, t.StarDot }.Select(c => c.ToString());

    [Theory]
    [InlineData(false), InlineData(true)]
    public void TextPairs_AllPass(bool dark)
    {
        var t = Tokens.For(dark);
        foreach (var (fg, bg) in Tokens.TextPairs)
            Assert.True(ColorMath.Contrast(Tokens.Get(t, fg), Tokens.Get(t, bg)) >= 4.5, $"{fg} on {bg} ({(dark ? "dark" : "light")})");
        Assert.True(Tokens.TextPairs.Count >= 30);
    }

    [Theory]
    [InlineData(false), InlineData(true)]
    public void NonText_AtLeast3(bool dark)
    {
        var t = Tokens.For(dark);
        Assert.True(ColorMath.Contrast(t.Boundary, t.Surface) >= 3);
        Assert.True(ColorMath.Contrast(t.Boundary, t.Menu) >= 3);
        Assert.True(ColorMath.Contrast(t.Accent, t.Menu) >= 3);
    }

    [Fact]
    public void HeaderTint_Calm() =>
        Assert.Equal(("#EEE8FF", "#3A3450"), (Tokens.HeaderTint(Mood.Calm, false).ToString(), Tokens.HeaderTint(Mood.Calm, true).ToString()));

    [Fact]
    public void HeaderTint_EveryMood_Reads()
    {
        foreach (var mood in Enum.GetValues<Mood>())
            foreach (var dark in new[] { false, true })
            {
                var t = Tokens.For(dark); var tint = Tokens.HeaderTint(mood, dark);
                Assert.True(ColorMath.Contrast(t.Ink, tint) >= 4.5, $"ink on {mood} {dark}");
                Assert.True(ColorMath.Contrast(t.Moon, tint) >= 4.5, $"moon on {mood} {dark}");
            }
    }

    [Fact]
    public void ColorMath_Basics()
    {
        Assert.Equal(21, ColorMath.Contrast(Rgba.Parse("#000000"), Rgba.Parse("#FFFFFF")), 2);
        Assert.Equal(Rgba.Parse("#808080"), ColorMath.Lerp(Rgba.Parse("#000000"), Rgba.Parse("#FFFFFF"), 0.5f) with { R = 128, G = 128, B = 128 });
        Assert.Equal(Rgba.Parse("#FFFFFF"), ColorMath.Over(new Rgba(0, 0, 0, 0), Rgba.Parse("#FFFFFF")));
        Assert.Equal(Rgba.Parse("#000000"), ColorMath.Over(Rgba.Parse("#000000"), Rgba.Parse("#FFFFFF")));
    }
}
