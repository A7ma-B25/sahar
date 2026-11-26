using Sahar.Core.Design;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Design;

/// <summary>Her speech bubble (spec 0.15 §4.1, board V15Talk): say up to 3 lines, long beyond (clamped, «كمّل بالمحادثة»).</summary>
public class BubbleTests
{
    [Theory]
    [InlineData("", 8000)]
    [InlineData("صباح الخير! جاهز نبلّش؟", 8000 + 22 * 60)]   // «لّ» is one character (the shadda rides on it)
    [InlineData("تشرفنا 💜", 8000 + 8 * 60)]            // an emoji is one character to read, not two code units
    [InlineData("👨‍👩‍👧 عيلة", 8000 + 6 * 60)]       // a ZWJ family too
    public void Duration_is_8s_plus_60ms_a_character(string text, int ms) =>
        Assert.Equal(TimeSpan.FromMilliseconds(ms), Bubble.Duration(text));

    [Fact]
    public void Duration_is_capped_at_14s() => Assert.Equal(TimeSpan.FromSeconds(14), Bubble.Duration(new string('ب', 500)));

    [Theory]
    [InlineData(1, BubbleKind.Say)]
    [InlineData(3, BubbleKind.Say)]
    [InlineData(4, BubbleKind.Long)]
    [InlineData(12, BubbleKind.Long)]
    public void Kind_by_lines(int lines, BubbleKind kind) => Assert.Equal(kind, Bubble.KindFor(lines));

    [Fact]
    public void Say_hugs_its_text_up_to_264()
    {
        var b = Bubble.Layout(BubbleKind.Say, textWidth: 150, lines: 1);
        Assert.Equal(150 + 2 * 15, b.Width);
        Assert.Equal(11 + Bubble.LineHeight + 12, b.Height, 3);
        Assert.Equal(new RectF(15, 11, 150, Bubble.LineHeight), b.Text);
        Assert.Null(b.Link);
        var wide = Bubble.Layout(BubbleKind.Say, textWidth: 400, lines: 3);
        Assert.Equal(264, wide.Width);
        Assert.Equal(234, wide.Text.Width);
        Assert.Equal(11 + 3 * Bubble.LineHeight + 12, wide.Height, 3);
    }

    [Fact]
    public void Long_is_264_wide_three_lines_and_the_link()
    {
        var b = Bubble.Layout(BubbleKind.Long, textWidth: 234, lines: 9);
        Assert.Equal(264, b.Width);
        Assert.Equal(3 * Bubble.LineHeight, b.Text.Height, 3);
        Assert.NotNull(b.Link);
        var link = b.Link!.Value;
        Assert.Equal(b.Text.Bottom + 6, link.Y, 3);
        Assert.Equal(32, link.Height);
        Assert.Equal(15 - 8, link.X);                      // the mockup's margin: 6px -8px -4px
        Assert.Equal(link.Bottom + 12 - 4, b.Height, 3);
    }

    [Fact]
    public void Place_centres_the_tail_on_her_leaf_above_her()
    {
        var work = new RectF(0, 0, 1920, 1040);
        var p = Bubble.Place(tip: new System.Numerics.Vector2(900, 500), work, new SizeF(200, 60), dpi: 1);
        Assert.Equal(800, p.Body.X);
        Assert.Equal(500 - Bubble.TailHeight - 60, p.Body.Y, 3);
        Assert.Equal(900, p.TailX);
        Assert.False(p.Below);
    }

    [Fact]
    public void Place_keeps_it_inside_the_work_area_and_the_tail_still_points_at_her()
    {
        var work = new RectF(0, 0, 1920, 1040);
        var p = Bubble.Place(tip: new System.Numerics.Vector2(1900, 500), work, new SizeF(264, 60), dpi: 1.5f);
        Assert.Equal(1920 - 264, p.Body.X);
        // The tail stays on the bubble, off its rounded corner.
        Assert.Equal(p.Body.Right - (Bubble.Radius + Bubble.TailWidth / 2) * 1.5f, p.TailX, 3);
        var left = Bubble.Place(tip: new System.Numerics.Vector2(10, 500), work, new SizeF(264, 60), dpi: 1);
        Assert.Equal(0, left.Body.X);
        Assert.Equal(Bubble.Radius + Bubble.TailWidth / 2, left.TailX, 3);
    }

    [Fact]
    public void Place_at_the_top_of_the_screen_goes_below_her_with_the_tail_up()
    {
        var work = new RectF(0, 40, 1920, 1000);
        var p = Bubble.Place(tip: new System.Numerics.Vector2(900, 70), work, new SizeF(200, 80), dpi: 1, under: 260);
        Assert.True(p.Below);
        Assert.Equal(260 + Bubble.TailHeight, p.Body.Y, 3);
        Assert.True(p.Body.Y >= work.Y);
    }

    [Fact]
    public void Clamp_keeps_whole_words_and_ends_with_an_ellipsis()
    {
        // A fake measure: 10 characters a line.
        int Lines(string s) => (s.Length + 9) / 10;
        var text = "واحد اتنين تلاتة اربعة خمسة ستة سبعة تمانة تسعة عشرة";
        var c = Bubble.Clamp(text, Lines, 3);
        Assert.EndsWith("…", c);
        Assert.True(Lines(c) <= 3);
        Assert.StartsWith(c[..^1], text);
        Assert.DoesNotContain(" …", c);
        var fits = text[..20];
        Assert.Same(fits, Bubble.Clamp(fits, Lines, 3));
    }

    [Fact]
    public void Clamp_never_cuts_an_emoji_in_half()
    {
        int Lines(string s) => (s.Length + 9) / 10;
        var text = "اهلا 💜💜💜💜💜💜💜💜💜💜💜💜💜💜💜💜💜💜";
        var c = Bubble.Clamp(text, Lines, 2);
        Assert.False(char.IsHighSurrogate(c[^2]));
        Assert.True(Lines(c) <= 2);
    }
}
