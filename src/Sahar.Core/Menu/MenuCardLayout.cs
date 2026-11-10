using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Settings;

namespace Sahar.Core.Menu;

/// <summary>What the card shows: "main" or a drill id, the visible items, and the optional parts.</summary>
public sealed record MenuView(string View, IReadOnlySet<string> Visible, bool MicShown, bool SettingsShown, bool FactShown, MenuDrill? Drill);

/// <summary>The card's geometry in DIPs, card-relative. Rects hold every item id plus the parts "header", "line1", "chatRow",
/// "footer", "hint", "title", "track", "note-text"; FocusOrder is the keyboard's order; Rows group it by row (logical order).</summary>
public sealed record CardLayout(float Width, float Height, IReadOnlyDictionary<string, RectF> Rects, IReadOnlyList<string> FocusOrder,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public enum Corner { TopLeft, TopRight }

/// <summary>Where the card goes on screen (px), the corner nearest her, and where the moon clasp rests (px).</summary>
public sealed record CardPlacement(RectF Card, Corner Near, Vector2 MoonCentre);

/// <summary>
/// The menu card's layout (spec §5): 296 DIP wide, padding 8; header 84; chat row 44; tiles 86; tools 58; footer 36. A
/// drill replaces tiles, tools and footer with a title row, a segmented track or rows, and a note. RTL: the logical start
/// is the right edge.
/// </summary>
public static class MenuCardLayout
{
    public const float Width = 296, Padding = 8, Radius = 22, Gap = 12, MoonDip = 20;
    private const float Inner = Width - 2 * Padding;

    public static CardLayout Layout(MenuView v, bool rtl = true)
    {
        var rects = new Dictionary<string, RectF>();
        var rows = new List<IReadOnlyList<string>>();
        // A rect given from the logical start (x from the start edge), mirrored into card coordinates.
        RectF At(float startX, float y, float w, float h) => new(rtl ? Width - startX - w : startX, y, w, h);

        float y = Padding;
        rects["header"] = new RectF(Padding, y, Inner, 84);
        // Header text: padding 14/16; line 1 17 px × 1.5, gap 6, line 2 14 px × 1.5 — centred in the 84.
        float textTop = y + (84 - (25.5f + 6 + 21)) / 2;
        rects["line1"] = At(Padding + 16, textTop, Inner - 32, 25.5f);
        if (v.FactShown && v.Drill is null)
        {
            rects["fact"] = At(Padding + 16, textTop + 31.5f, Inner - 32, 21);
            rows.Add(["fact"]);
        }
        y += 84;

        bool chat = v.Visible.Contains("chat");
        if (chat)
        {
            y += 8;
            rects["chatRow"] = new RectF(Padding, y, Inner, 44);
            float end = Padding + Inner - 4, by = y + 4;   // logical end of the row (send sits there)
            var row = new List<string> { "chat" };
            var send = At(end - 36, by, 36, 36);
            float fieldEnd = end - 36 - 4;
            if (v.MicShown)
            {
                rects["mic"] = At(fieldEnd - 36, by, 36, 36);
                fieldEnd -= 40;
                row.Add("mic");
            }
            rects["send"] = send;
            row.Add("send");
            rects["chat"] = At(Padding + 4, by, fieldEnd - Padding - 4, 36);
            if (v.Drill is null) rows.Add(row);   // a drill's keys stay in the drill (back + options); Esc returns to the chat
            y += 44;
        }

        if (v.Drill is { } d)
        {
            float x0 = Padding + 8, w = Inner - 16;
            y += 12;
            rects["back"] = At(x0, y, 32, 32);
            rects["title"] = At(x0 + 40, y, w - 40, 32);
            rows.Add(["back"]);
            y += 32 + 12;
            var options = d.Options;
            int segs = d.Style == DrillStyle.Segmented ? Math.Min(4, options.Count) : 0;
            if (segs > 0)
            {
                rects["track"] = At(x0, y, w, 36);
                float sw = (w - 6) / segs;
                var row = new List<string>();
                for (int i = 0; i < segs; i++)
                {
                    rects[options[i].Id] = At(x0 + 3 + i * sw, y + 3, sw, 30);
                    row.Add(options[i].Id);
                }
                rows.Add(row);
                y += 36;
            }
            for (int i = segs; i < options.Count; i++)
            {
                if (i > 0) y += segs > 0 && i == segs ? 8 : 4;
                rects[options[i].Id] = At(x0, y, w, 44);
                rows.Add([options[i].Id]);
                y += 44;
            }
            if (d.Note is not null)
            {
                y += 12;
                rects["note-text"] = At(x0, y, w, 44);
                y += 44;
            }
            y += 10;
        }
        else
        {
            var tiles = MenuCatalog.Tiles(false).Select(t => t.Id).Where(v.Visible.Contains).ToList();
            if (tiles.Count > 0)
            {
                y += 10;
                float tw = (Inner - 8 * (tiles.Count - 1)) / tiles.Count;
                for (int i = 0; i < tiles.Count; i++) rects[tiles[i]] = At(Padding + i * (tw + 8), y, tw, 86);
                rows.Add(tiles);
                y += 86 + 4;
            }
            var tools = MenuCatalog.Tools.Select(t => t.Id).Where(v.Visible.Contains).ToList();
            if (tools.Count > 0)
            {
                y += 2;
                float tw = (Inner - 6 * (tools.Count - 1)) / tools.Count;
                for (int i = 0; i < tools.Count; i++) rects[tools[i]] = At(Padding + i * (tw + 6), y, tw, 58);
                rows.Add(tools);
                y += 58 + 6;
            }
            if (tiles.Count == 0 && tools.Count == 0) y += 8;
            rects["footer"] = new RectF(Padding, y, Inner, 36);
            if (v.SettingsShown)
            {
                rects["settings"] = At(Padding + 4, y, 132, 36);
                rows.Add(["settings"]);
            }
            rects["hint"] = At(Padding + Inner - 4 - 100, y, 100, 36);
            y += 36 + Padding;
        }

        var order = rows.SelectMany(r => r).ToList();
        return new CardLayout(Width, y, rects, order, rows);
    }

    /// <summary>Beside her on the roomier side (12 DIP from her body, top-aligned with her head), or centred on her screen;
    /// always inside the work area. All px.</summary>
    public static CardPlacement Place(RectF bodyPx, RectF workPx, Vector2 cardDip, MenuPlace place, float dpi)
    {
        var size = cardDip;   // already in px when the caller scaled it (cardDip × dpi); named for the plan's interface
        float w = size.X, h = size.Y, gap = Gap * dpi;
        float x, y;
        Corner near;
        if (place == MenuPlace.Center)
        {
            x = workPx.X + (workPx.Width - w) / 2;
            y = workPx.Y + (workPx.Height - h) / 2;
            near = bodyPx.X + bodyPx.Width / 2 < x + w / 2 ? Corner.TopLeft : Corner.TopRight;
        }
        else
        {
            float roomLeft = bodyPx.X - workPx.X, roomRight = workPx.Right - bodyPx.Right;
            if (roomRight >= roomLeft) { x = bodyPx.Right + gap; near = Corner.TopLeft; }
            else { x = bodyPx.X - gap - w; near = Corner.TopRight; }
            y = bodyPx.Y;
        }
        x = Math.Clamp(x, workPx.X, MathF.Max(workPx.X, workPx.Right - w));
        y = Math.Clamp(y, workPx.Y, MathF.Max(workPx.Y, workPx.Bottom - h));
        var card = new RectF(x, y, w, h);
        float inward = 2 * dpi;
        var moon = near == Corner.TopLeft ? new Vector2(card.X + inward, card.Y) : new Vector2(card.Right - inward, card.Y);
        return new CardPlacement(card, near, moon);
    }
}
