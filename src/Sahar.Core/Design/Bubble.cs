using System.Globalization;
using System.Numerics;
using Sahar.Core.Geometry;

namespace Sahar.Core.Design;

/// <summary>say: up to 3 lines. long: more, clamped to 3 lines plus «كمّل بالمحادثة» (spec 0.15 §4.1).</summary>
public enum BubbleKind { Say, Long }

/// <summary>Where the bubble's parts go, in DIPs from its top-left (the tail is drawn apart, under it).</summary>
public readonly record struct BubbleBox(float Width, float Height, RectF Text, RectF? Link);

/// <summary>Where the bubble goes on screen (pixels): its body, the tail's centre x, and whether it hangs below her.</summary>
public readonly record struct BubblePlace(RectF Body, float TailX, bool Below);

/// <summary>
/// Her speech bubble (spec 0.15 §4.1, approved board V15Talk / V15Bubble): the menu colour, radius 18, padding 11/15/12,
/// Rubik 15 at line-height 1.55, at most 264 wide; a 22×11 rounded tail points at her leaf. It stays 8 s plus 60 ms a
/// character, at most 14 s. Words and emoji are hers as written (owner, 2026-11-26: emoji stay, drawn in colour).
/// </summary>
public static class Bubble
{
    public const float MaxWidth = 264, Radius = 18, PadTop = 11, PadSide = 15, PadBottom = 12;
    public const float TextSize = 15, LineHeight = TextSize * 1.55f, TailWidth = 22, TailHeight = 11;
    public const int MaxLines = 3;
    /// <summary>«كمّل بالمحادثة»: 14/500 softInk, a 32-high row 6 under the text (mockup margin 6 -8 -4).</summary>
    public const float LinkGap = 6, LinkHeight = 32, LinkInset = 8, LinkPull = 4, LinkSize = 14;
    /// <summary>The tail's tip in her canvas units: just over her leaf, above her bud (rig: bud at 154,57; leaf tips from 38).</summary>
    public const float TipXUnits = 154, TipYUnits = 34;
    /// <summary>A gap between her and the tail's tip, DIPs.</summary>
    public const float GapDip = 2;

    public static TimeSpan Duration(string text)
    {
        int chars = text.Length == 0 ? 0 : new StringInfo(text).LengthInTextElements;
        return TimeSpan.FromMilliseconds(Math.Min(14000, 8000 + 60 * chars));
    }

    public static BubbleKind KindFor(int lines) => lines > MaxLines ? BubbleKind.Long : BubbleKind.Say;

    /// <param name="textWidth">The widest line as laid out at <see cref="MaxWidth"/> less the padding (DIP).</param>
    /// <param name="lines">Lines it takes at that width (a long one shows <see cref="MaxLines"/>).</param>
    public static BubbleBox Layout(BubbleKind kind, float textWidth, int lines)
    {
        float inner = MaxWidth - 2 * PadSide;
        if (kind == BubbleKind.Long)
        {
            var text = new RectF(PadSide, PadTop, inner, MaxLines * LineHeight);
            var link = new RectF(PadSide - LinkInset, text.Bottom + LinkGap, MaxWidth - 2 * (PadSide - LinkInset), LinkHeight);
            return new BubbleBox(MaxWidth, link.Bottom + PadBottom - LinkPull, text, link);
        }
        float w = MathF.Min(inner, MathF.Ceiling(textWidth));
        int n = Math.Clamp(lines, 1, MaxLines);
        return new BubbleBox(w + 2 * PadSide, PadTop + n * LineHeight + PadBottom, new RectF(PadSide, PadTop, w, n * LineHeight), null);
    }

    /// <summary>
    /// Above her, its tail's tip at <paramref name="tip"/> (screen px), kept inside <paramref name="work"/>; the tail moves
    /// along its bottom edge to keep pointing at her (never onto a rounded corner). With no room above, it hangs under
    /// her (<paramref name="under"/>: the screen y under her body), tail up; without that, it is pushed down to the top.
    /// </summary>
    public static BubblePlace Place(Vector2 tip, RectF work, SizeF size, float dpi, float under = float.NaN)
    {
        float tail = TailHeight * dpi;
        float x = Math.Clamp(tip.X - size.Width / 2, work.X, MathF.Max(work.X, work.Right - size.Width));
        float y = tip.Y - tail - size.Height;
        bool below = false;
        if (y < work.Y)
        {
            if (!float.IsNaN(under) && under + tail + size.Height <= work.Bottom) { y = under + tail; below = true; }
            else y = work.Y;
        }
        float edge = (Radius + TailWidth / 2) * dpi;
        float tailX = size.Width < 2 * edge ? x + size.Width / 2 : Math.Clamp(tip.X, x + edge, x + size.Width - edge);
        return new BubblePlace(new RectF(x, y, size.Width, size.Height), tailX, below);
    }

    /// <summary>
    /// The text cut to <paramref name="maxLines"/> (as <paramref name="lines"/> measures it) with an ellipsis: at a word's
    /// end when one fits, else between characters (an emoji or a joined sequence is never split). Text that fits comes back
    /// as it was.
    /// </summary>
    public static string Clamp(string text, Func<string, int> lines, int maxLines)
    {
        if (lines(text) <= maxLines) return text;
        const string ellipsis = "…";
        string? best = null;
        for (int i = text.IndexOf(' '); i > 0; i = text.IndexOf(' ', i + 1))
        {
            var candidate = text[..i].TrimEnd() + ellipsis;
            if (lines(candidate) > maxLines) break;
            best = candidate;
        }
        if (best is not null) return best;
        var starts = StringInfo.ParseCombiningCharacters(text);
        int lo = 0, hi = starts.Length;   // how many text elements to keep
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            int end = mid < starts.Length ? starts[mid] : text.Length;
            if (lines(text[..end] + ellipsis) <= maxLines) lo = mid; else hi = mid - 1;
        }
        int cut = lo < starts.Length ? starts[lo] : text.Length;
        return text[..cut].TrimEnd() + ellipsis;
    }
}
