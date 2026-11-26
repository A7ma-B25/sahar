using System.Numerics;
using System.Runtime.InteropServices;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;

namespace Sahar.Engine.Render;

/// <summary>What one bubble shows: its words (clamped for a long one), its kind and its parts (DIPs).</summary>
internal sealed record BubbleText(string Shown, BubbleKind Kind, BubbleBox Box, int Lines, float LinkTextWidth)
{
    /// <summary>The «كمّل بالمحادثة» button (text, gap, chevron, padding) inside the link row, at the logical start (right).</summary>
    public RectF? LinkButton => Box.Link is { } row
        ? new RectF(row.Right - (BubblePainter.LinkPad * 2 + LinkTextWidth + BubblePainter.LinkIconGap + BubblePainter.LinkIcon), row.Y,
            BubblePainter.LinkPad * 2 + LinkTextWidth + BubblePainter.LinkIconGap + BubblePainter.LinkIcon, row.Height)
        : null;
}

/// <summary>
/// Draws her speech bubble (spec 0.15 §4.1, the approved boards V15Talk / V15TalkDark) with Direct2D in DIPs (the caller
/// scales to pixels): the menu-colour body (radius 18) and its 22×11 rounded tail, Rubik 15 at line-height 1.55, right to
/// left. Emoji are hers and draw in colour (DirectWrite falls back to Segoe UI Emoji; D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT).
/// </summary>
internal static unsafe class BubblePainter
{
    public const string LinkLabel = "كمّل بالمحادثة";
    public const float LinkPad = 8, LinkIconGap = 6, LinkIcon = 16;
    /// <summary>Rubik's typographic ascent/descent (OS/2, USE_TYPO_METRICS: 935/−250 per 1000): the baseline that centres
    /// a line in its 1.55 box, as CSS does.</summary>
    private const float Ascent = 0.935f, Descent = 0.25f;
    /// <summary>The tail (V15Bubble's SVG), 22×11, its tip at (11, 10).</summary>
    private const string TailPath = "M0 0 H22 C 15 0 13 10 11 10 C 9 10 7 0 0 0 Z";

    /// <summary>Lays out <paramref name="text"/> as the bubble will show it: lines at the bubble's text width, a long text
    /// clamped to three lines with an ellipsis.</summary>
    public static BubbleText Measure(string text)
    {
        float inner = Bubble.MaxWidth - 2 * Bubble.PadSide;
        var (lines, width) = Lines(text, inner);
        var kind = Bubble.KindFor(lines);
        string shown = text;
        if (kind == BubbleKind.Long)
        {
            shown = Bubble.Clamp(text, s => Lines(s, inner).Lines, Bubble.MaxLines);
            (lines, width) = Lines(shown, inner);
        }
        float link = kind == BubbleKind.Long ? DWrite.Measure(LinkLabel, Bubble.LinkSize, 500, rtl: true).X : 0;
        return new BubbleText(shown, kind, Bubble.Layout(kind, width, lines), lines, link);
    }

    /// <summary>The body and the tail (below the body, or above it when <paramref name="up"/>), the tail's centre at
    /// <paramref name="tailX"/> DIPs from the body's left.</summary>
    public static void Draw(D2DContext dc, BubbleText b, ThemeTokens t, float tailX, bool up)
    {
        var box = b.Box;
        float top = up ? Bubble.TailHeight : 0;
        var menu = dc.SolidBrush(new D2DColor(t.Menu));
        dc.FillRoundedRect(new D2DRect { Left = 0, Top = top, Right = box.Width, Bottom = top + box.Height }, Bubble.Radius, menu);
        // The tail overlaps the body by 1 (the mockup's margin-top: -1px), so no seam shows.
        var tail = up
            ? new Matrix3x2(1, 0, 0, -1, tailX - Bubble.TailWidth / 2, Bubble.TailHeight + 1)
            : Matrix3x2.CreateTranslation(tailX - Bubble.TailWidth / 2, box.Height - 1);
        dc.PushTransform(tail);
        dc.FillGeometry(dc.Path(Parsed(TailPath)), menu);
        dc.PopTransform();

        var ink = dc.SolidBrush(new D2DColor(t.Ink));
        var text = box.Text;
        nint layout = Paragraph(b.Shown, Bubble.MaxWidth - 2 * Bubble.PadSide);
        try
        {
            // The layout is the full text width, aligned right (RTL leading): its right edge on the text box's right edge.
            dc.DrawLayout(layout, new Vector2(text.Right - (Bubble.MaxWidth - 2 * Bubble.PadSide), top + text.Y), ink);
        }
        finally { Marshal.Release(layout); }

        if (b.LinkButton is { } btn)
        {
            var softInk = dc.SolidBrush(new D2DColor(t.AccentInk));
            float textRight = btn.Right - LinkPad;
            dc.TextLayout(LinkLabel, Bubble.LinkSize, 500,
                new D2DRect { Left = textRight - b.LinkTextWidth - 1, Top = top + btn.Y, Right = textRight, Bottom = top + btn.Bottom },
                softInk, TextAlign.Start, rtl: true);
            var icon = new RectF(textRight - b.LinkTextWidth - LinkIconGap - LinkIcon, top + btn.Y + (btn.Height - LinkIcon) / 2, LinkIcon, LinkIcon);
            Icon(dc, "chevronStart", icon, softInk);
        }
    }

    /// <summary>The bubble's full drawing size in DIPs (body plus tail).</summary>
    public static SizeF Size(BubbleText b) => new(b.Box.Width, b.Box.Height + Bubble.TailHeight);

    /// <summary>A wrapped right-to-left paragraph in Rubik 15 at the bubble's line height (caller releases).</summary>
    public static nint Paragraph(string text, float width)
    {
        // Running words: proportional digits («٥:٢٠» reads as one time; the card's fixed digit cells are for counters).
        nint layout = DWrite.Layout(text, Bubble.TextSize, 400, width, 10000, TextAlign.Start, rtl: true, digitCell: 0, wrap: true);
        // IDWriteTextFormat (the layout is one) ::SetParagraphAlignment(NEAR), ::SetLineSpacing(UNIFORM, height, baseline):
        // every line the same height, emoji lines too (Segoe UI Emoji is taller than Rubik).
        ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Slot(layout, 4))(layout, 0);
        float lh = Bubble.LineHeight, baseline = lh / 2 + (Ascent - Descent) / 2 * Bubble.TextSize;
        ((delegate* unmanaged[Stdcall]<nint, int, float, float, int>)Com.Slot(layout, 10))(layout, 1, lh, baseline);
        return layout;
    }

    /// <summary>How many lines <paramref name="text"/> takes at <paramref name="width"/>, and its widest line.</summary>
    public static (int Lines, float Width) Lines(string text, float width)
    {
        if (text.Length == 0) return (1, 0);
        nint layout = Paragraph(text, width);
        try
        {
            DWriteTextMetrics m;
            int hr = ((delegate* unmanaged[Stdcall]<nint, DWriteTextMetrics*, int>)Com.Slot(layout, 60))(layout, &m);
            return hr < 0 ? (1, width) : ((int)Math.Max(1, m.LineCount), MathF.Min(width, m.Width));
        }
        finally { Marshal.Release(layout); }
    }

    private static void Icon(D2DContext dc, string key, RectF box, nint brush)
    {
        if (!MenuIcons.All.TryGetValue(key, out var icon)) return;
        float k = box.Width / MenuIcons.Grid;
        dc.PushTransform(new Matrix3x2(k, 0, 0, k, box.X, box.Y));
        var round = dc.StrokeStyle("round");
        foreach (var p in icon.Paths) dc.DrawGeometry(dc.Path(Parsed(p)), brush, MenuIcons.Stroke, round);
        dc.PopTransform();
    }

    private static readonly Dictionary<string, IReadOnlyList<PathCommand>> ParsedPaths = [];
    private static IReadOnlyList<PathCommand> Parsed(string d)
    {
        if (!ParsedPaths.TryGetValue(d, out var p)) ParsedPaths[d] = p = PathData.Parse(d);
        return p;
    }
}
