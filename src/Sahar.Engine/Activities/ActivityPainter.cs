using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;

namespace Sahar.Engine.Activities;

/// <summary>
/// Draws her activity tag, its thread and the activity cards in the Porcelain style of the approved mockups (ActTag,
/// ActCard, ActCards; light and dark), in DIPs — the caller's transform scales to pixels. Cards paint in three passes
/// over the same layout: measure (no context), background (card, tints, wells, bars) and foreground (text, icons), so
/// hover shapes sit between them, as on the menu card.
/// </summary>
internal static class ActivityPainter
{
    public const float CardWidth = 296, Pad = 8, Inner = 280;

    // ---- the tag ----

    /// <summary>The tag's width for this activity (padding 12, icon/cover/shot, the text, meta and dot; ≤ 220).</summary>
    public static float TagWidth(Activity a)
    {
        var (lead, text, meta, dot) = TagParts(a);
        float w = 24 + lead + (lead > 0 ? 8 : 0) + TextWidth(a, text) + (meta > 0 ? 8 + meta : 0) + (dot ? 15 : 0);
        return MathF.Min(MathF.Ceiling(w), TagPlace.MaxWidthDip);
    }

    private static (float Lead, string Text, float Meta, bool Dot) TagParts(Activity a)
    {
        float lead = a.Kind is ActivityKind.Music or ActivityKind.MusicPaused ? 24 : a.Kind == ActivityKind.Screenshot ? 30 : a.Icon.Length > 0 ? 18 : 0;
        float meta = a.Meta is { Length: > 0 } m ? DWrite.Measure(m, 13, 400, true).X : 0;
        return (lead, a.Text, meta, a.Kind == ActivityKind.CameraMic);
    }

    private static float TextWidth(Activity a, string text)
    {
        float w = DWrite.Measure(text, 13, 500, true).X;
        return a.Progress is not null ? MathF.Max(w, 72) : w;
    }

    /// <summary>The tag itself at (0,0) in a <paramref name="w"/> × 40 box (its shadow is the caller's).</summary>
    public static void Tag(D2DContext dc, Activity a, float w, ThemeTokens t, Pixels? cover, Rgba? coverColour, bool dark)
    {
        const float h = TagPlace.HeightDip;
        bool ask = a.Asking;
        var bg = ask ? t.AccentSoft : t.Menu;
        var fg = ask ? t.AccentInk : t.Ink;
        dc.FillRoundedRect(D(R(0, 0, w, h)), TagPlace.Radius, dc.SolidBrush(new D2DColor(bg)));
        dc.FillEllipse(new Vector2(w / 2, 5.5f), new Vector2(2.5f), dc.SolidBrush(new D2DColor(ask ? t.Menu : t.Sunken)));
        var (lead, text, metaW, dot) = TagParts(a);
        // RTL: from the right edge, lead → text → meta → dot.
        float x = w - 12;
        float cy = 21;
        if (lead > 0)
        {
            x -= lead;
            if (a.Kind is ActivityKind.Music or ActivityKind.MusicPaused) Cover(dc, R(x, cy - 12, 24, 24), 6, cover, coverColour, dark, t);
            else if (a.Kind == ActivityKind.Screenshot) Shot(dc, R(x, cy - 11, 30, 22), 5, dark, t);
            else
            {
                bool warm = a.Kind is ActivityKind.WeatherAlert or ActivityKind.BatteryLow;
                Icon(dc, a.Icon, R(x, cy - 9, 18, 18), 18, dc.SolidBrush(new D2DColor(warm ? t.Warn : ask ? t.AccentInk : t.Text2)));
            }
            x -= 8;
        }
        float avail = x - 12 - (metaW > 0 ? metaW + 8 : 0) - (dot ? 15 : 0);
        string shown = Fit(text, 13, 500, avail);
        float tw = MathF.Min(TextWidth(a, shown), avail);
        if (a.Progress is { } p)
        {
            dc.TextLayout(shown, 13, 500, D(R(x - tw, 10, tw, 17)), dc.SolidBrush(new D2DColor(fg)), TextAlign.Start, true);
            dc.FillRoundedRect(D(R(x - tw, 30, tw, 2)), 1, dc.SolidBrush(new D2DColor(t.Sunken)));
            float fill = tw * Math.Clamp(p, 0, 1);
            if (fill > 0.5f) dc.FillRoundedRect(D(R(x - fill, 30, fill, 2)), 1, dc.SolidBrush(new D2DColor(t.Accent)));
        }
        else dc.TextLayout(shown, 13, 500, D(R(x - tw, cy - 9, tw, 18)), dc.SolidBrush(new D2DColor(fg)), TextAlign.Start, true);
        x -= tw;
        if (metaW > 0)
        {
            x -= 8 + metaW;
            dc.TextLayout(a.Meta!, 13, 400, D(R(x, cy - 9, metaW + 1, 18)), dc.SolidBrush(new D2DColor(ask ? t.AccentInk : t.Text2)), TextAlign.Start, true);
        }
        if (dot) dc.FillEllipse(new Vector2(x - 8 - 3.5f, cy), new Vector2(3.5f), dc.SolidBrush(new D2DColor(t.Moon)));
    }

    /// <summary>The thread from <paramref name="from"/> (her) to <paramref name="to"/> (the eyelet), with up to three beads
    /// from her end and a knot after them (ActTag: 1.5 stroke, beads r 3.4 / stroke 1.3, a slight S-curve).</summary>
    public static void Thread(D2DContext dc, Vector2 from, Vector2 to, int beads, bool knot, ThemeTokens t)
    {
        var d = to - from;
        float len = d.Length();
        if (len < 1) return;
        var u = d / len;
        var n = new Vector2(-u.Y, u.X);
        var c1 = from + u * len * 0.35f + n * 1.2f;
        var c2 = from + u * len * 0.7f - n * 1.2f;
        var brush = dc.SolidBrush(new D2DColor(t.Boundary));
        var round = dc.StrokeStyle("round");
        dc.DrawGeometry(dc.Path(PathData.Parse(FormattableString.Invariant(
            $"M{from.X} {from.Y} C{c1.X} {c1.Y} {c2.X} {c2.Y} {to.X} {to.Y}"))), brush, 1.5f, round);
        var fill = dc.SolidBrush(new D2DColor(t.Menu));
        for (int i = 0; i < Math.Min(beads, 3); i++)
        {
            var at = from + u * (7 + i * 9) + n * (i % 2 == 1 ? 0.6f : -0.6f);
            dc.FillEllipse(at, new Vector2(3.4f), fill);
            dc.DrawEllipse(at, new Vector2(3.4f), brush, 1.3f, round);
        }
        if (knot) dc.FillEllipse(from + u * (7 + 3 * 9 - 2), new Vector2(2.2f), brush);
    }

    /// <summary>Where the beads are, from her end (for clicks): the i-th bead's centre.</summary>
    public static Vector2 Bead(Vector2 from, Vector2 to, int i)
    {
        var u = Vector2.Normalize(to - from);
        return from + u * (7 + i * 9);
    }

    // ---- the cards ----

    /// <summary>One pass of a card: <paramref name="layer"/> 0 measures, 1 draws the background, 2 the foreground.
    /// Returns the card's height; <paramref name="hits"/> gets each button's rect (DIP, card-relative).</summary>
    public static float Card(D2DContext? dc, int layer, CardData d, ThemeTokens t, bool dark, DateTimeOffset now, Dictionary<string, RectF> hits,
        float height = 0)
    {
        var p = new Pass(dc, layer, t, dark, hits);
        p.Fill(R(0, 0, CardWidth, height), 22, t.Menu);
        float y = d.Kind switch
        {
            CardKind.Music when d.Music is { } m => Music(p, m),
            CardKind.Weather when d.Weather is { } w => Weather(p, w, now),
            CardKind.Claude when d.Claude is { } c => Claude(p, c, now),
            CardKind.Ask when d.Claude is { } c => Ask(p, c),
            CardKind.Shelf when d.Shelf is { } s => Shelf(p, s),
            CardKind.Copy when d.Copy is { } c => Copy(p, c),
            CardKind.System when d.System is { } s => SystemCard(p, s),
            CardKind.Time when d.Time is { } tm => Time(p, tm),
            CardKind.Away when d.Away is { } a => Away(p, a, now),
            _ => p.HeadCol("…", "", 8),
        };
        return y + Pad;
    }

    private static float Music(Pass p, MusicCard m)
    {
        // Header: the cover (56) at the start, the title and «artist · app».
        float y = 8, h = 84;
        p.Fill(R(8, y, Inner, h), 14, p.T.HeaderTint());
        if (p.Bg) Cover(p.Dc!, R(p.X(8 + 16, 56), y + 14, 56, 56), 12, m.Cover, m.CoverColour, p.Dark, p.T);
        float tx = 8 + 16 + 56 + 14, tw = Inner - 16 - 56 - 14 - 16;
        p.Text(m.Title, 16, 600, R(p.X(tx, tw), y + 19, tw, 22), p.T.Ink);
        p.Text(string.Join(" · ", new[] { m.Artist, m.App }.Where(s => s.Length > 0)), 13, 400, R(p.X(tx, tw), y + 45, tw, 20), p.T.Text2);
        y += h;
        // The bar fills from the start (right in RTL); the position at the start, the length at the end.
        y += 14;
        float bx = 8 + 10, bw = Inner - 20;
        p.Fill(R(bx, y, bw, 4), 2, p.T.Sunken);
        float frac = m.Duration > TimeSpan.Zero ? (float)Math.Clamp(m.Position / m.Duration, 0, 1) : 0;
        if (frac > 0) p.Fill(R(p.X(bx, bw * frac), y, bw * frac, 4), 2, p.T.Accent);
        y += 4 + 6;
        p.Text(ActivityText.SongTime(m.Position), 12, 400, R(bx, y, bw, 18), p.T.Text3, TextAlign.Start);
        p.Text(ActivityText.SongTime(m.Duration), 12, 400, R(bx, y, bw, 18), p.T.Text3, TextAlign.End);
        y += 18 + 4;
        // Transport (LTR): previous, play/pause, next.
        y += 2;
        float cx = CardWidth / 2;
        p.Button("music-prev", R(cx - 26 - 10 - 44, y + 4, 44, 44));
        p.Icon("prev", R(cx - 26 - 10 - 44, y + 4, 44, 44), 20, p.T.Ink);
        p.Fill(R(cx - 26, y, 52, 52), 16, p.T.Accent);
        p.Button("music-toggle", R(cx - 26, y, 52, 52));
        p.Icon(m.Playing ? "pause" : "play", R(cx - 26, y, 52, 52), 22, p.T.OnAccent);
        p.Button("music-next", R(cx + 26 + 10, y + 4, 44, 44));
        p.Icon("next", R(cx + 26 + 10, y + 4, 44, 44), 20, p.T.Ink);
        y += 52 + 6;
        if (m.Lyrics == LyricsView.Lines || m.Lyrics == LyricsView.None || m.Lyrics == LyricsView.Loading)
        {
            y += 4;
            p.Fill(R(8, y, Inner, 81), 14, p.T.Sunken);
            string prev = m.Lyrics == LyricsView.Lines ? m.PreviousLine ?? "" : "";
            string now = m.Lyrics switch { LyricsView.Lines => m.Line ?? "…", LyricsView.None => "ما لقيت كلمات لهالأغنية", _ => "عم جيب الكلمات…" };
            p.Text(prev, 13, 400, R(8 + 14, y + 12, Inner - 28, 21), p.T.Text3);
            p.Text(now, 15, 500, R(8 + 14, y + 39, Inner - 28, 24), p.T.Ink);
            y += 81 + 2;
        }
        else if (m.Lyrics == LyricsView.Asking)
        {
            y += 4;
            float boxH = 12 + 42 + 10 + 34 + 12;
            p.Fill(R(8, y, Inner, boxH), 14, p.T.Sunken);
            p.Text("الكلمات بتجي من الإنترنت (lrclib.net). بيروح بس اسم الأغنية والفنان.", 13, 400, R(8 + 14, y + 12, Inner - 28, 42), p.T.Ink, wrap: true);
            float bx2 = 8 + 14, by = y + 12 + 42 + 10;
            float yesW = p.Measure("شغّلها", 14, 500) + 28;
            p.Fill(R(p.X(bx2, yesW), by, yesW, 34), 10, p.T.Accent);
            p.Button("lyrics-yes", R(p.X(bx2, yesW), by, yesW, 34));
            p.Text("شغّلها", 14, 500, R(p.X(bx2, yesW), by, yesW, 34), p.T.OnAccent, TextAlign.Centre);
            float noW = MathF.Max(44, p.Measure("لا", 14, 500) + 24);
            p.Button("lyrics-no", R(p.X(bx2 + yesW + 8, noW), by, noW, 34));
            p.Text("لا", 14, 500, R(p.X(bx2 + yesW + 8, noW), by, noW, 34), p.T.Accent, TextAlign.Centre);
            y += boxH + 2;
        }
        // Footer: the lyrics chip at the start, «Esc لسكّر» at the end.
        bool on = m.Lyrics is LyricsView.Lines or LyricsView.Loading or LyricsView.None;
        float chipW = 12 + 16 + 6 + p.Measure("الكلمات", 13, 500) + 12;
        float fy = y + 4 + 2;
        if (on) p.Fill(R(p.X(8 + 8, chipW), fy, chipW, 36), 10, p.T.AccentSoft);
        p.Button("lyrics", R(p.X(8 + 8, chipW), fy, chipW, 36));
        var ink = on ? p.T.AccentInk : p.T.Text2;
        p.Icon("notes", R(p.X(8 + 8 + 12, 16), fy + 10, 16, 16), 16, ink);
        p.Text("الكلمات", 13, 500, R(p.X(8 + 8 + 12 + 16 + 6, chipW - 46), fy, chipW - 46, 36), ink);
        p.Esc(y);
        return y + 4 + 40;
    }

    private static float Weather(Pass p, WeatherCard w, DateTimeOffset now)
    {
        var f = w.Forecast;
        float y = 8, h = 90;
        p.Fill(R(8, y, Inner, h), 14, p.T.HeaderTint());
        p.Text(ActivityText.Degrees(f.TempNow), 36, 600, R(p.X(8 + 16, 140), y + 14, 140, 40), p.T.Ink);
        p.Text($"{ActivityText.Sky(f.CodeNow)} · {w.Place.Label}", 13, 400, R(p.X(8 + 16, 190), y + 58, 190, 20), p.T.Text2);
        p.Icon(SkyIcon(f.CodeNow), R(p.X(8 + Inner - 16 - 40, 40), y + 25, 40, 40), 40, p.T.Moon);
        y += h;
        // The next five hours.
        var hours = f.Hours.Where(x => x.Time > now).Take(5).ToList();
        float col = (Inner - 8) / 5f;
        for (int i = 0; i < hours.Count; i++)
        {
            var hc = hours[i];
            var box = R(p.X(8 + 4 + i * col, col), y + 12, col, 18);
            p.Text(ActivityText.Hour(hc.Time), 12, 400, box, p.T.Text3, TextAlign.Centre);
            bool windy = hc.Gusts >= WeatherAlerts.GustKmh;
            var icon = windy ? "wind" : SkyIcon(hc.Code);
            p.Icon(icon, R(box.X + (col - 20) / 2, y + 12 + 18 + 6, 20, 20), 20, windy ? p.T.Warn : p.T.Text2);
            p.Text(ActivityText.Degrees(hc.Temp), 14, 500, R(box.X, y + 12 + 18 + 6 + 20 + 6, col, 21), p.T.Ink, TextAlign.Centre);
        }
        y += 12 + 18 + 6 + 20 + 6 + 21 + 6;
        if (w.Alerts.Count > 0)
        {
            var a = w.Alerts[0];
            y += 6;
            p.Fill(R(8, y, Inner, 63), 14, p.T.Sunken);
            p.Icon(ActivityIcons.For(ActivityKind.WeatherAlert, a), R(p.X(8 + 12, 20), y + 21, 20, 20), 20, p.T.Warn);
            float tx = 8 + 12 + 20 + 12, tw = Inner - 12 - 20 - 12 - 12;
            p.Text(ActivityText.AlertTag(a), 14, 500, R(p.X(tx, tw), y + 10, tw, 21), p.T.Ink);
            p.Text(ActivityText.AlertDetail(a, f), 13, 400, R(p.X(tx, tw), y + 33, tw, 20), p.T.Text2);
            y += 63;
        }
        float fy = y + 4;
        p.Text($"اليوم {ActivityText.Degrees(f.TodayMin)} – {ActivityText.Degrees(f.TodayMax)}", 13, 400, R(8 + 8, fy, Inner - 12, 36), p.T.Text2, TextAlign.Start);
        p.Text($"Open-Meteo · قبل {ActivityText.Duration(now - w.Fetched)}", 12, 400, R(8 + 4, fy, Inner - 12, 36), p.T.Text3, TextAlign.End);
        return y + 4 + 40;
    }

    private static string SkyIcon(int code) => code switch
    {
        0 or 1 => "sun",
        >= 51 and <= 67 or >= 80 and <= 86 or >= 95 => "rain",
        >= 71 and <= 77 => "snow",
        _ => "cloud",
    };

    private static float Claude(Pass p, ClaudeSession c, DateTimeOffset now)
    {
        string since = c.Started is { } s ? $"{c.Project} · من {ActivityText.Long(now - s)}" : c.Project;
        float y = p.HeadCol(c.Started is null ? ActivityText.ClaudeDone : "Claude عم يشتغل", since, 8);
        // Now: the step, and the file when it edits or reads one.
        y += 14;
        p.Icon("terminal", R(p.X(8 + 8, 18), y + 3, 18, 18), 18, p.T.Text2);
        string step = ActivityText.ClaudeStep(c.Step).Replace("Claude ", "");
        float sw = p.Measure(step, 14, 400);
        p.Text(step, 14, 400, R(p.X(8 + 8 + 18 + 8, sw + 2), y, sw + 2, 24), p.T.Ink);
        if (c.Target is { Length: > 0 } file)
        {
            float cx = 8 + 8 + 18 + 8 + sw + 8, cw = MathF.Min(Inner - cx + 8 - 8, file.Length * 7.2f + 16);
            p.Fill(R(p.X(cx, cw), y + 1, cw, 22), 7, p.T.Sunken);
            p.Code(file, R(p.X(cx, cw), y + 1, cw, 22), p.T.Ink);
        }
        y += 24 + 6;
        if (c.Todos.Count > 0)
        {
            y += 8;
            int done = c.Todos.Count(x => x.Status == "completed");
            p.Text("المهام", 13, 500, R(8 + 8, y, Inner - 16, 20), p.T.Text2, TextAlign.Start);
            p.Text($"{ActivityText.N(done)} من {ActivityText.N(c.Todos.Count)}", 13, 500, R(8 + 8, y, Inner - 16, 20), p.T.Text2, TextAlign.End);
            y += 20 + 6;
            foreach (var todo in c.Todos.Take(6))
            {
                bool isDone = todo.Status == "completed", isNow = todo.Status == "in_progress";
                var mark = R(p.X(8 + 6, 18), y + 6, 18, 18);
                if (isDone) { p.Fill(mark, 9, p.T.AccentSoft); p.Icon("check", mark, 14, p.T.AccentInk); }
                else if (isNow) { p.Fill(mark, 9, p.T.Accent); p.Dot(new Vector2(mark.X + 9, mark.Y + 9), 1.6f, p.T.OnAccent); }
                else p.Ring(mark, 1.5f, p.T.Boundary);
                float tx = 8 + 6 + 18 + 10, tw = Inner - 12 - 28;
                p.Text(todo.Text, 14, isNow ? 500 : 400, R(p.X(tx, tw), y, tw, 30), isDone ? p.T.Text2 : isNow ? p.T.Ink : p.T.Text3);
                y += 32;
            }
            y += 4;
        }
        p.Esc(y);
        return y + 4 + 40;
    }

    private static float Ask(Pass p, ClaudeSession c)
    {
        // Owner ruling 3: notify only — no Allow/Deny; it says where to answer.
        float y = 8, h = 72;
        p.Fill(R(8, y, Inner, h), 14, Tokens.HeaderTint(Mood.Alert, p.Dark));
        p.Icon("bell", R(p.X(8 + 16, 20), y + 26, 20, 20), 20, p.T.Warn);
        float tx = 8 + 16 + 20 + 12, tw = Inner - 16 - 20 - 12 - 16;
        p.Text("Claude بدّه إذنك", 16, 600, R(p.X(tx, tw), y + 14, tw, 23), p.T.Ink);
        p.Text($"بمشروع {c.Project}", 13, 400, R(p.X(tx, tw), y + 39, tw, 20), p.T.Text2);
        y += h + 12;
        string what = c.Step switch
        {
            ClaudeStep.Running => "بدّه يشغّل أمر.",
            ClaudeStep.Editing => "بدّه يعدّل ملف:",
            ClaudeStep.Reading => "بدّه يقرا ملف:",
            ClaudeStep.Web => "بدّه يطلع عالنت.",
            _ => "بدّه جوابك على شي.",
        };
        p.Text(what, 13, 400, R(8 + 8, y, Inner - 16, 20), p.T.Text2, TextAlign.Start);
        y += 20 + 8;
        if (c.Target is { Length: > 0 } file)
        {
            p.Fill(R(8 + 8, y, Inner - 16, 40), 10, p.T.Sunken);
            p.Code(file, R(8 + 8, y, Inner - 16, 40), p.T.Ink);
            y += 40 + 4;
        }
        y += 4;
        p.Icon("terminal", R(p.X(8 + 8, 18), y + 13, 18, 18), 18, p.T.Accent);
        p.Text(ActivityText.AskWhere, 14, 500, R(p.X(8 + 8 + 18 + 8, Inner - 42), y, Inner - 42, 44), p.T.Ink);
        return y + 44 + 6;
    }

    private static float Shelf(Pass p, ShelfCard s)
    {
        float y = p.HeadCol(ActivityText.Shelf(s.Count), "اسحب أي وحدة لمطرح ما بدك", 8);
        y += 12;
        float col = (Inner - 8 - 24) / 4f;
        for (int i = 0; i < Math.Min(4, s.Tiles.Count); i++)
        {
            var tile = s.Tiles[i];
            bool on = s.Selected.Contains(i);
            var cell = R(p.X(8 + 4 + i * (col + 8), col), y, col, 94);
            p.Button($"tile-{i}", cell);
            var thumb = R(cell.X + (col - 60) / 2, y + 4, 60, 60);
            if (on && p.Bg) p.Dc!.DrawRoundedRect(D(new RectF(thumb.X - 3, thumb.Y - 3, 66, 66)), 13, p.Dc.SolidBrush(new D2DColor(p.T.Accent)), 2);
            if (p.Bg) ShelfThumb(p, tile, thumb);
            if (tile.Thing == ShelfThing.Doc && tile.Thumb is null)
            {
                // ActCard: a document's icon (22) and its extension (12/600) on the sunken tile.
                string icon = tile.Ext is "TXT" or "MD" or "LOG" or "CSV" ? "text" : "zip";
                p.Icon(icon, R(thumb.X + 19, thumb.Y + 8, 22, 22), 22, p.T.Text2);
                p.Text(tile.Ext, 12, 600, R(thumb.X, thumb.Y + 34, 60, 18), p.T.Text2, TextAlign.Centre);
            }
            p.Text(tile.Label, 12, 400, R(cell.X, y + 4 + 60 + 6, col, 18), on ? p.T.Ink : p.T.Text2, TextAlign.Centre);
            if (on)
            {
                var badge = new Vector2(p.Rtl ? cell.Right - 2 - 10 : cell.X + 2 + 10, y + 10);
                p.Dot(badge, 12, p.T.Menu);
                p.Dot(badge, 10, p.T.Accent);
                p.Icon("check", R(badge.X - 6, badge.Y - 6, 12, 12), 12, p.T.OnAccent);
            }
        }
        y += 94 + 6;
        (string Id, string Icon, string Label)[] acts = [("act-share", "share", "شارك"), ("act-text", "text", "انسخ النص"), ("act-shrink", "shrink", "صغّرها"), ("act-zip", "zip", "اضغطهن")];
        float aw = (Inner - 12) / 4f;
        y += 4;
        for (int i = 0; i < acts.Length; i++)
        {
            var r = R(p.X(8 + i * (aw + 4), aw), y, aw, 58);
            p.Button(acts[i].Id, r);
            p.Icon(acts[i].Icon, R(r.X + (aw - 19) / 2, y + 9, 19, 19), 19, p.T.Text2);
            p.Text(acts[i].Label, 12, 400, R(r.X, y + 32, aw, 18), p.T.Text2, TextAlign.Centre);
        }
        y += 58 + 2;
        int n = s.Selected.Count;
        p.Text(n == 0 ? "على الكل" : $"على {ActivityText.N(n)} منهن", 13, 400, R(8 + 8, y + 4, Inner - 12, 36), p.T.Text2, TextAlign.Start);
        p.Ghost("shelf-clear", "sweep", "فضّي الرف", y, start: false);
        return y + 4 + 40;
    }

    private static void ShelfThumb(Pass p, ShelfTile tile, RectF r)
    {
        var dc = p.Dc!;
        if (tile.Thumb is { } px)
        {
            dc.FillRoundedImage(D(r), 10, dc.Bitmap(px.Bgra, px.Width, px.Height), px.Width, px.Height);
            return;
        }
        if (tile.Thing == ShelfThing.Shot) { Shot(dc, r, 10, p.Dark, p.T); return; }
        if (tile.Thing == ShelfThing.Photo)
        {
            dc.FillRoundedRect(D(r), 10, dc.SolidBrush(new D2DColor(Rgba.Parse(p.Dark ? "#4E6A86" : "#A9CBE8"))));
            dc.FillEllipse(new Vector2(r.X + 40, r.Y + 20), new Vector2(7), dc.SolidBrush(new D2DColor(Rgba.Parse("#FFF3D6"))));
            dc.FillGeometry(dc.Path(PathData.Parse(FormattableString.Invariant(
                $"M{r.X} {r.Y + 46} L{r.X + 18} {r.Y + 28} L{r.X + 30} {r.Y + 40} L{r.X + 40} {r.Y + 32} L{r.Right} {r.Y + 48} L{r.Right} {r.Bottom - 10} Q{r.Right} {r.Bottom} {r.Right - 10} {r.Bottom} L{r.X + 10} {r.Bottom} Q{r.X} {r.Bottom} {r.X} {r.Bottom - 10} Z"))),
                dc.SolidBrush(new D2DColor(Rgba.Parse(p.Dark ? "#3E6B52" : "#6FA585"))));
            return;
        }
        dc.FillRoundedRect(D(r), 10, dc.SolidBrush(new D2DColor(p.T.Sunken)));
    }

    private static float Copy(Pass p, CopyCard c)
    {
        float y = p.HeadCol(ActivityText.Copying, "من نافذة النسخ تبع الويندوز", 8);
        y += 16;
        p.Text(ActivityText.Percent(c.Percent), 32, 600, R(8 + 10, y, 140, 34), p.T.Ink, TextAlign.Start);
        if (c.Left is { } left)
            p.Text($"باقي {ActivityText.Long(left)} تقريباً", 13, 400, R(8 + 10, y + 12, Inner - 20, 20), p.T.Text2, TextAlign.End);
        y += 34 + 10;
        p.Fill(R(8 + 10, y, Inner - 20, 6), 3, p.T.Sunken);
        float fw = (Inner - 20) * (float)Math.Clamp(c.Percent / 100, 0, 1);
        if (fw > 0.5f) p.Fill(R(p.X(8 + 10, fw), y, fw, 6), 3, p.T.Accent);
        y += 6 + 8;
        p.Ghost("copy-show", "open", "فرجيني نافذة النسخ", y, start: true);
        return y + 4 + 40;
    }

    private static float SystemCard(Pass p, SystemCard s)
    {
        float y = p.HeadCol("الجهاز", s.Battery is < 20 && !s.Charging ? "البطارية ضعيفة" : "كل شي تمام", 8);
        y += 6;
        void Row(string icon, string label, Action<float, float> content)
        {
            p.Icon(icon, R(p.X(8 + 2 + 8, 20), y + 16, 20, 20), 20, p.T.Text2);
            p.Text(label, 14, 400, R(p.X(8 + 2 + 8 + 20 + 10, 64), y, 64, 52), p.T.Ink);
            content(8 + 2 + 8 + 20 + 10 + 64 + 10, Inner - 4 - 16 - 20 - 10 - 64 - 10);
            y += 52;
        }
        if (s.Volume is { } vol)
            Row("speaker", "الصوت", (x, w) =>
            {
                float tw = w - 24 - 10;
                p.Fill(R(p.X(x, tw), y + 24, tw, 4), 2, p.T.Sunken);
                float fw = tw * vol / 100f;
                if (fw > 0.5f) p.Fill(R(p.X(x, fw), y + 24, fw, 4), 2, p.T.Accent);
                var knob = new Vector2(p.Rtl ? CardWidth - x - fw : x + fw, y + 26);
                p.Dot(knob, 8.75f, p.T.Accent);
                p.Dot(knob, 7.25f, p.T.Menu);
                p.Text(ActivityText.N(vol), 14, 400, R(p.X(x + tw + 10, 24), y, 24, 52), p.T.Ink, TextAlign.End);
            });
        if (s.Battery is { } bat)
            Row("bolt", "البطارية", (x, w) =>
            {
                string top = ActivityText.Percent(bat) + (s.Charging ? " · عم يشحن" : "");
                p.Text(top, 14, 500, R(p.X(x, w), y + (s.ToFull is null ? 15 : 6), w, 21), p.T.Ink);
                if (s.ToFull is { } full) p.Text($"بتتعبّى بعد {ActivityText.Long(full)}", 13, 400, R(p.X(x, w), y + 28, w, 20), p.T.Text2);
            });
        Row("mic", "المايك", (x, w) => InUse(p, x, w, y, s.MicApp, "عم يسمع", "مطفي"));
        Row("camera", "الكاميرا", (x, w) => InUse(p, x, w, y, s.CameraApp, "عم يشوف", "مطفية"));
        return y + 2;
    }

    private static void InUse(Pass p, float x, float w, float y, string? app, string verb, string off)
    {
        if (app is null) { p.Text(off, 13, 400, R(p.X(x, w), y, w, 52), p.T.Text2); return; }
        p.Dot(new Vector2(p.Rtl ? CardWidth - x - 4 : x + 4, y + 26), 4, p.T.Moon);
        p.Text($"{app} {verb}", 14, 500, R(p.X(x + 16, w - 16), y, w - 16, 52), p.T.Ink);
    }

    private static float Time(Pass p, TimeCard tc)
    {
        string sub = tc.Yesterday is not { } yd ? "أول يوم بعدّ فيه"
            : tc.Total <= yd ? $"أقل من مبارح بـ {ActivityText.Long(yd - tc.Total)}" : $"أكتر من مبارح بـ {ActivityText.Long(tc.Total - yd)}";
        float y = 8, h = 81;
        p.Fill(R(8, y, Inner, h), 14, p.T.HeaderTint());
        p.Text($"{ActivityText.Today} {ActivityText.Duration(tc.Total)}", 22, 600, R(8 + 16, y + 14, Inner - 32, 29), p.T.Ink, TextAlign.Start);
        p.Text(sub, 13, 400, R(8 + 16, y + 47, Inner - 32, 20), p.T.Text2, TextAlign.Start);
        y += h + 12;
        double max = tc.Apps.Count > 0 ? tc.Apps.Max(a => a.Time.TotalSeconds) : 1;
        foreach (var (app, time) in tc.Apps.Take(4))
        {
            p.Text(app, 14, 500, R(8 + 8, y, Inner - 16, 21), p.T.Ink, TextAlign.Start);
            p.Text(ActivityText.Duration(time), 13, 400, R(8 + 8, y, Inner - 16, 21), p.T.Text2, TextAlign.End);
            y += 21 + 6;
            p.Fill(R(8 + 8, y, Inner - 16, 6), 3, p.T.Sunken);
            float fw = (Inner - 16) * (float)(time.TotalSeconds / max);
            if (fw > 0.5f) p.Fill(R(p.X(8 + 8, fw), y, fw, 6), 3, time.TotalSeconds >= max ? p.T.Accent : p.T.Boundary);
            y += 6 + 12;
        }
        p.Text("كل شي بيضل على جهازك.", 13, 400, R(8 + 8, y - 8 + 4, Inner - 12, 36), p.T.Text2, TextAlign.Start);
        return y - 8 + 4 + 40;
    }

    private static float Away(Pass p, AwaySummary a, DateTimeOffset now)
    {
        float y = p.HeadCol(ActivityText.Away, $"{ActivityText.Long(a.Away)} · صار {ActivityText.N(a.Total)} شغلات", 8);
        y += 8;
        foreach (var (kind, count, last) in a.Rows)
        {
            p.Icon(AwayIcon(kind), R(p.X(8 + 4 + 8, 18), y + 13, 18, 18), 18, p.T.Text2);
            p.Text(ActivityText.AwayRow(kind, count), 14, 400, R(p.X(8 + 4 + 8 + 18 + 10, Inner - 100), y, Inner - 100, 44), p.T.Ink);
            p.Text(ActivityText.Clock(last.ToLocalTime()), 12, 400, R(8 + 4 + 8, y, Inner - 24, 44), p.T.Text3, TextAlign.End);
            y += 44;
        }
        if (a.More > 0)
        {
            p.Text(ActivityText.More(a.More), 13, 400, R(8 + 4 + 8, y, Inner - 24, 32), p.T.Text2, TextAlign.Start);
            y += 32;
        }
        y += 2;
        p.Ghost("away-ok", "", "تمام", y, start: true);
        p.Text("بتروح لحالها بعد دقيقة", 12, 400, R(8 + 4, y + 4, Inner - 12, 36), p.T.Text3, TextAlign.End);
        _ = now;
        return y + 4 + 40;
    }

    private static string AwayIcon(ActivityKind k) => k switch
    {
        ActivityKind.ClaudeDone => "check",
        ActivityKind.CopyDone => "copy",
        ActivityKind.WeatherAlert => "rain",
        ActivityKind.Charging => "bolt",
        ActivityKind.BatteryLow => "battery",
        ActivityKind.ClaudeAsk => "bell",
        _ => "stack",
    };

    // ---- small pictures ----

    /// <summary>A cover: the real one, or the mockup's placeholder (a sun over hills) in the cover's colour.</summary>
    public static void Cover(D2DContext dc, RectF r, float radius, Pixels? cover, Rgba? colour, bool dark, ThemeTokens t)
    {
        if (cover is { } px) dc.FillRoundedImage(D(r), radius, dc.Bitmap(px.Bgra, px.Width, px.Height), px.Width, px.Height);
        else
        {
            float k = r.Width / 56;
            dc.FillRoundedRect(D(r), radius, dc.SolidBrush(new D2DColor(colour ?? Rgba.Parse(dark ? "#C9773F" : "#E38B4F"))));
            dc.FillEllipse(new Vector2(r.X + 34 * k, r.Y + 22 * k), new Vector2(10 * k), dc.SolidBrush(new D2DColor(Rgba.Parse("#FCE3C8"))));
            dc.FillGeometry(dc.Path(PathData.Parse(FormattableString.Invariant(
                $"M{r.X} {r.Y + 44 * k} C{r.X + 14 * k} {r.Y + 34 * k} {r.X + 26 * k} {r.Y + 34 * k} {r.Right} {r.Y + 46 * k} L{r.Right} {r.Bottom - radius} Q{r.Right} {r.Bottom} {r.Right - radius} {r.Bottom} L{r.X + radius} {r.Bottom} Q{r.X} {r.Bottom} {r.X} {r.Bottom - radius} Z"))),
                dc.SolidBrush(new D2DColor(Rgba.Parse(dark ? "#8A4524" : "#B5582A"))));
        }
        dc.DrawRoundedRect(D(new RectF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1)), radius - 0.5f,
            dc.SolidBrush(new D2DColor(dark ? new Rgba(255, 255, 255, 26) : new Rgba(0, 0, 0, 26))), 1);
    }

    /// <summary>The mockup's screenshot thumbnail (a window with a bar and two blocks).</summary>
    public static void Shot(D2DContext dc, RectF r, float radius, bool dark, ThemeTokens t)
    {
        var bar = dc.SolidBrush(new D2DColor(Rgba.Parse(dark ? "#57506A" : "#C9C3D8")));
        dc.FillRoundedRect(D(r), radius, dc.SolidBrush(new D2DColor(Rgba.Parse(dark ? "#3A3546" : "#E4E1EC"))));
        float kx = r.Width / 30, ky = r.Height / 22;
        dc.FillRoundedRect(D(new RectF(r.X + 3 * kx, r.Y + 3 * ky, 24 * kx, 4 * ky)), 1 * kx, bar);
        dc.FillRoundedRect(D(new RectF(r.X + 3 * kx, r.Y + 9 * ky, 14 * kx, 10 * ky)), 1.5f * kx, bar);
        dc.FillRoundedRect(D(new RectF(r.X + 19 * kx, r.Y + 9 * ky, 8 * kx, 4 * ky)), 1 * kx, bar);
        _ = t;
    }

    /// <summary>A wobbled stroke icon (24-unit grid, 1.7 stroke) at <paramref name="size"/> DIP centred in <paramref name="box"/>.</summary>
    public static void Icon(D2DContext dc, string key, RectF box, float size, nint brush)
    {
        if (!ActivityIcons.All.TryGetValue(key, out var icon)) return;
        float k = size / MenuIcons.Grid;
        dc.PushTransform(new Matrix3x2(k, 0, 0, k, box.X + (box.Width - size) / 2, box.Y + (box.Height - size) / 2));
        var round = dc.StrokeStyle("round");
        foreach (var path in icon.Paths) dc.DrawGeometry(dc.Path(Parsed(path)), brush, MenuIcons.Stroke, round);
        dc.PopTransform();
    }

    private static readonly Dictionary<string, IReadOnlyList<PathCommand>> ParsedPaths = [];
    private static IReadOnlyList<PathCommand> Parsed(string d)
    {
        lock (ParsedPaths)
        {
            if (!ParsedPaths.TryGetValue(d, out var p)) ParsedPaths[d] = p = PathData.Parse(d);
            return p;
        }
    }

    /// <summary>Cuts a line to fit <paramref name="max"/> DIP with an ellipsis.</summary>
    public static string Fit(string text, float size, int weight, float max)
    {
        if (max <= 0) return "";
        if (DWrite.Measure(text, size, weight, true).X <= max) return text;
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (DWrite.Measure(text[..mid].TrimEnd() + "…", size, weight, true).X <= max) lo = mid; else hi = mid - 1;
        }
        return text[..lo].TrimEnd() + "…";
    }

    private static RectF R(float x, float y, float w, float h) => new(x, y, w, h);
    private static D2DRect D(RectF r) => new() { Left = r.X, Top = r.Y, Right = r.Right, Bottom = r.Bottom };

    /// <summary>One pass over a card's layout: the same calls measure, draw the background, or draw the foreground.</summary>
    private sealed class Pass(D2DContext? dc, int layer, ThemeTokens t, bool dark, Dictionary<string, RectF> hits)
    {
        public D2DContext? Dc => dc;
        public ThemeTokens T => t;
        public bool Dark => dark;
        public bool Rtl => true;
        public bool Bg => dc is not null && layer == 1;
        private bool Fg => dc is not null && layer == 2;

        /// <summary>A rect at <paramref name="fromStart"/> from the card's start edge (the right, in RTL).</summary>
        public float X(float fromStart, float width) => Rtl ? CardWidth - fromStart - width : fromStart;

        public void Fill(RectF r, float radius, Rgba c)
        {
            if (Bg) dc!.FillRoundedRect(D(r), radius, dc.SolidBrush(new D2DColor(c)));
        }

        public void Ring(RectF r, float width, Rgba c)
        {
            if (Bg) dc!.DrawEllipse(new Vector2(r.X + r.Width / 2, r.Y + r.Height / 2), new Vector2(r.Width / 2 - width / 2), dc.SolidBrush(new D2DColor(c)), width, 0);
        }

        public void Dot(Vector2 c, float radius, Rgba colour)
        {
            if (Bg) dc!.FillEllipse(c, new Vector2(radius), dc.SolidBrush(new D2DColor(colour)));
        }

        public void Button(string id, RectF r) => hits[id] = r;

        public float Measure(string text, float size, int weight) => DWrite.Measure(text, size, weight, true).X;

        public void Text(string text, float size, int weight, RectF r, Rgba c, TextAlign align = TextAlign.Start, bool wrap = false)
        {
            if (!Fg || text.Length == 0) return;
            var shown = wrap ? text : Fit(text, size, weight, r.Width);
            dc!.TextLayout(shown, size, weight, D(r), dc.SolidBrush(new D2DColor(c)), align, Rtl, wrap: wrap);
        }

        public void Code(string text, RectF r, Rgba c)
        {
            if (Fg) dc!.Text(text, "Cascadia Mono", 12.5f, 400, D(r), dc.SolidBrush(new D2DColor(c)));
        }

        public void Icon(string key, RectF box, float size, Rgba c)
        {
            if (Fg) ActivityPainter.Icon(dc!, key, box, size, dc!.SolidBrush(new D2DColor(c)));
        }

        /// <summary>The usual header: tinted block, title 16/600 and a line under it; returns the y under the block.</summary>
        public float HeadCol(string title, string sub, float y)
        {
            float h = sub.Length > 0 ? 74 : 50;
            Fill(R(8, y, Inner, h), 14, T.HeaderTint());
            Text(title, 16, 600, R(8 + 16, y + 14, Inner - 32, 23), T.Ink);
            if (sub.Length > 0) Text(sub, 13, 400, R(8 + 16, y + 40, Inner - 32, 20), T.Text2);
            return y + h;
        }

        /// <summary>«Esc لسكّر» at the footer's end.</summary>
        public void Esc(float y) => Text("Esc لسكّر", 12, 400, R(8 + 4, y + 4, Inner - 12, 36), T.Text3, TextAlign.End);

        /// <summary>A ghost button (accent text, optional icon) in the footer, at its start or end.</summary>
        public void Ghost(string id, string icon, string label, float y, bool start)
        {
            float w = 10 + (icon.Length > 0 ? 16 + 6 : 0) + Measure(label, 14, 500) + 10;
            float fromStart = start ? 8 + 8 : CardWidth - 8 - 4 - w;
            var r = R(X(fromStart, w), y + 4 + 2, w, 36);
            Button(id, r);
            if (icon.Length > 0) Icon(icon, R(X(fromStart + 10, 16), r.Y + 10, 16, 16), 16, T.Accent);
            float tx = fromStart + 10 + (icon.Length > 0 ? 22 : 0);
            Text(label, 14, 500, R(X(tx, w - (tx - fromStart) - 8), r.Y, w - (tx - fromStart) - 8, 36), T.Accent);
        }
    }

    private static Rgba HeaderTint(this ThemeTokens t) => Tokens.HeaderTint(Mood.Calm, t == Tokens.Dark);
}
