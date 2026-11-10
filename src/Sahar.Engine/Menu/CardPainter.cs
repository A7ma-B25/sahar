using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;

namespace Sahar.Engine.Menu;

/// <summary>Everything the card draws from (one frame of it).</summary>
internal sealed record CardContent(CardLayout Layout, MenuView View, ThemeTokens T, Rgba Tint, string Line1, HeaderFact? Fact, bool Hushed,
    bool Rtl);

/// <summary>
/// Draws the Porcelain menu card (spec §5 and the approved mockups PMenuSoul / PMenuSoulDark) with Direct2D, in DIPs
/// (the caller's transform scales to pixels). Two layers: the background (card, header tint, sunken fields, the active
/// tile) and the foreground (text and the hand-drawn icons); hover, press and focus are composition visuals between them.
/// Opaque and matte: no glass, no glow; only Sahar carries colour.
/// </summary>
internal static class CardPainter
{
    public static void Background(D2DContext dc, CardContent c)
    {
        var l = c.Layout;
        var t = c.T;
        dc.FillRoundedRect(R(new(0, 0, l.Width, l.Height)), MenuCardLayout.Radius, dc.SolidBrush(new D2DColor(t.Menu)));
        var header = l.Rects["header"];
        dc.FillRoundedRect(R(header), 14, dc.SolidBrush(new D2DColor(c.Tint)));
        // Three tiny star dots at the header's end side (mockup: 18,16 r1.5 · 34,30 r1 · 26,50 r1.25 from the end/top).
        var star = dc.SolidBrush(new D2DColor(t.StarDot));
        foreach (var (dx, dy, r) in new[] { (18f, 16f, 1.5f), (34f, 30f, 1f), (26f, 50f, 1.25f) })
        {
            float x = c.Rtl ? header.X + dx : header.Right - dx;
            dc.FillEllipse(new Vector2(x, header.Y + dy), new Vector2(r), star);
        }
        if (l.Rects.TryGetValue("chatRow", out var chat)) dc.FillRoundedRect(R(chat), 14, dc.SolidBrush(new D2DColor(t.Sunken)));
        if (c.View.Drill is null && c.Hushed && l.Rects.TryGetValue("dnd", out var dnd))
            dc.FillRoundedRect(R(dnd), 12, dc.SolidBrush(new D2DColor(t.AccentSoft)));
        if (l.Rects.TryGetValue("track", out var track)) dc.FillRoundedRect(R(track), 12, dc.SolidBrush(new D2DColor(t.Sunken)));
    }

    public static void Foreground(D2DContext dc, CardContent c)
    {
        var l = c.Layout;
        var t = c.T;
        bool rtl = c.Rtl;
        var ink = dc.SolidBrush(new D2DColor(t.Ink));
        var text2 = dc.SolidBrush(new D2DColor(t.Text2));
        var text3 = dc.SolidBrush(new D2DColor(t.Text3));
        var round = dc.StrokeStyle("round");

        dc.TextLayout(Address.ToYou(c.Line1), 17, 600, R(l.Rects["line1"]), ink, TextAlign.Start, rtl);
        if (c.Fact is { } fact && l.Rects.TryGetValue("fact", out var fr))
        {
            float dotX = rtl ? fr.Right - 4 : fr.X + 4;
            dc.FillEllipse(new Vector2(dotX, fr.Y + fr.Height / 2), new Vector2(4), dc.SolidBrush(new D2DColor(Rgba.Parse("#F2C86A"))));
            var textBox = rtl ? new RectF(fr.X, fr.Y, fr.Width - 16, fr.Height) : new RectF(fr.X + 16, fr.Y, fr.Width - 16, fr.Height);
            dc.TextLayout(Address.ToYou(fact.Text), 14, 500, R(textBox), dc.SolidBrush(new D2DColor(t.Moon)), TextAlign.Start, rtl);
        }

        if (l.Rects.TryGetValue("chat", out var field))
        {
            var ph = rtl ? new RectF(field.X, field.Y, field.Width - 10, field.Height) : new RectF(field.X + 10, field.Y, field.Width - 10, field.Height);
            dc.TextLayout("احكي معي…", 14, 400, R(ph), text3, TextAlign.Start, rtl);
            Icon(dc, "send", l.Rects["send"], 18, text2, round, rtl);
            if (l.Rects.TryGetValue("mic", out var mic)) Icon(dc, "mic", mic, 18, text2, round, rtl);
        }

        if (c.View.Drill is { } d)
        {
            Icon(dc, "back", l.Rects["back"], 16, ink, round, rtl);
            dc.TextLayout(Address.ToYou(d.Title), 16, 600, R(l.Rects["title"]), ink, TextAlign.Start, rtl);
            bool segmented = l.Rects.ContainsKey("track");
            for (int i = 0; i < d.Options.Count; i++)
            {
                var o = d.Options[i];
                var r = l.Rects[o.Id];
                if (segmented && i < 4)
                    dc.TextLayout(Address.ToYou(o.Label), 13, 400, R(r), text2, TextAlign.Centre, rtl);
                else
                {
                    var box = rtl ? new RectF(r.X, r.Y, r.Width - 12, r.Height) : new RectF(r.X + 12, r.Y, r.Width - 12, r.Height);
                    dc.TextLayout(Address.ToYou(o.Label), 15, 500, R(box), ink, TextAlign.Start, rtl);
                }
            }
            if (d.Note is { } note && l.Rects.TryGetValue("note-text", out var nr))
                dc.TextLayout(Address.ToYou(note), 13, 400, R(nr), text2, TextAlign.Start, rtl, wrap: true);
            return;
        }

        foreach (var tile in MenuCatalog.Tiles(c.Hushed))
        {
            if (!l.Rects.TryGetValue(tile.Id, out var r)) continue;
            var brush = tile.Id == "dnd" && c.Hushed ? dc.SolidBrush(new D2DColor(t.AccentInk)) : ink;
            float top = r.Y + (r.Height - (24 + 8 + 21)) / 2;
            Icon(dc, tile.Icon, new RectF(r.X + (r.Width - 24) / 2, top, 24, 24), 24, brush, round, rtl);
            dc.TextLayout(Address.ToYou(tile.Label), 14, 500, R(new RectF(r.X, top + 32, r.Width, 21)), brush, TextAlign.Centre, rtl);
        }
        foreach (var tool in MenuCatalog.Tools)
        {
            if (!l.Rects.TryGetValue(tool.Id, out var r)) continue;
            float top = r.Y + (r.Height - (19 + 4 + 19.5f)) / 2;
            Icon(dc, tool.Icon, new RectF(r.X + (r.Width - 19) / 2, top, 19, 19), 19, text2, round, rtl);
            dc.TextLayout(tool.Label, 13, 400, R(new RectF(r.X, top + 23, r.Width, 19.5f)), text2, TextAlign.Centre, rtl);
        }
        if (l.Rects.TryGetValue("settings", out var s))
        {
            // From the start: gear 17, 8, «إعداداتي» 14/500, 8, the start-pointing chevron 14.
            float x = rtl ? s.Right - 10 - 17 : s.X + 10;
            Icon(dc, "gear", new RectF(x, s.Y + (s.Height - 17) / 2, 17, 17), 17, ink, round, rtl);
            var label = MenuCatalog.Settings.Label;
            var size = DWrite.Measure(label, 14, 500, rtl);
            float tx = rtl ? x - 8 - size.X : x + 17 + 8;
            dc.TextLayout(label, 14, 500, R(new RectF(tx, s.Y, size.X + 1, s.Height)), ink, TextAlign.Start, rtl);
            float cx = rtl ? tx - 8 - 14 : tx + size.X + 8;
            Icon(dc, "chevronStart", new RectF(cx, s.Y + (s.Height - 14) / 2, 14, 14), 14, ink, round, rtl);
        }
        if (l.Rects.TryGetValue("hint", out var hint))
            dc.TextLayout("Esc لسكّر", 13, 400, R(hint), text3, TextAlign.End, rtl);
    }

    /// <summary>A stroke icon (24-unit grid, stroke 1.7, round) in <paramref name="box"/> at <paramref name="size"/> DIP;
    /// directional icons are mirrored in LTR.</summary>
    private static void Icon(D2DContext dc, string key, RectF box, float size, nint brush, nint round, bool rtl)
    {
        if (!MenuIcons.All.TryGetValue(key, out var icon)) return;
        float k = size / MenuIcons.Grid;
        float ox = box.X + (box.Width - size) / 2, oy = box.Y + (box.Height - size) / 2;
        bool mirror = !rtl && MenuIcons.Directional(key);
        var m = mirror
            ? new Matrix3x2(-k, 0, 0, k, ox + size, oy)
            : new Matrix3x2(k, 0, 0, k, ox, oy);
        dc.PushTransform(m);
        foreach (var p in icon.Paths) dc.DrawGeometry(dc.Path(PathData.Parse(p)), brush, MenuIcons.Stroke, round);
        foreach (var (cx, cy, r) in icon.Circles) dc.DrawEllipse(new Vector2(cx, cy), new Vector2(r), brush, MenuIcons.Stroke, round);
        dc.PopTransform();
    }

    private static D2DRect R(RectF r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };
}
