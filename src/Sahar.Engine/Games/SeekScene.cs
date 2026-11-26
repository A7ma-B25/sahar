using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Brain;
using Sahar.Core.Games;
using Sahar.Core.Geometry;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.UI.Composition;

namespace Sahar.Engine.Games;

/// <summary>
/// The look of «الغميضة» (0.16, spec §5, board V15Seek): the desktop she hides in (<see cref="World"/>), her clip
/// behind a window's edge or the taskbar, her paws over her eyes, the «٣ ٢ ١» discs and round pips, the decoy spark
/// trail, the «هِهِ» giggle, the warmer/colder chip beside the pointer, the found burst, the dashed "here I was"
/// outline and the «منعيدها؟» card. Porcelain tokens (spec §2), Rubik, no emoji. Never moves or covers his windows:
/// everything is drawn on her own overlay, which never covers a whole monitor.
/// </summary>
internal sealed class SeekScene : IDisposable
{
    private readonly CompositionHost _host;
    private readonly SaharVisual _v;
    private readonly Compositor _c;
    private readonly List<Piece> _pieces = [];
    private Piece? _chip, _ask, _pips;
    private SpriteVisual[] _paws = [];
    private SwapSurface? _pawSurface;
    private InsetClip? _clip;

    /// <summary>Light or dark (her appearance setting, else Windows' apps theme); reduced motion: no movement.</summary>
    public bool Dark { get; set; }
    public bool Reduced { get; set; }
    public float Dpi { get; set; } = 1f;
    /// <summary>Screen px of the «يلا» and «خلص» buttons while the card shows.</summary>
    public RectF AgainButton { get; private set; }
    public RectF DoneButton { get; private set; }
    public bool AskShown => _ask != null;
    public bool ClipOn => _clip != null;
    /// <summary>The last chip's warmth (test diagnostics).</summary>
    public Warmth? LastWhisper { get; private set; }

    public SeekScene(CompositionHost host, SaharVisual v)
    {
        (_host, _v, _c) = (host, v, host.Compositor);
    }

    // ---------- the desktop she hides in ----------

    private static readonly HashSet<string> Skip = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "tooltips_class32", "TaskListThumbnailWnd",
        "TaskListOverlayWnd", "Xaml_WindowedPopupClass", "NotifyIconOverflowWindow", "#32768", "ForegroundStaging",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>The game's monitor, its taskbar and the visible windows on it, top of the z-order first. Window handles
    /// only (never a title).</summary>
    public static unsafe SeekWorld World(MonitorInfo m, Vector2 body)
    {
        var bounds = ToF(m.Bounds);
        var (bar, edge, _) = Taskbar.Get(m.Handle);
        int barEdge = edge switch { Edge.Left => 0, Edge.Top => 1, Edge.Right => 2, _ => 3 };
        var windows = new List<SeekWindow>();
        int guard = 0;
        nint shell = Native.GetShellWindow();
        for (nint h = Native.GetTopWindow(0); h != 0 && guard++ < 4096; h = Native.GetWindow(h, 2 /* GW_HWNDNEXT */))
        {
            if (h == shell || Win.Own.Contains(h) || Native.IsWindowVisible(h) == 0 || Native.IsIconic(h) != 0) continue;
            int cloaked = 0;
            if (Native.DwmGetWindowAttribute(h, 14 /* DWMWA_CLOAKED */, &cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            long ex = Native.GetWindowLongPtr(h, -20 /* GWL_EXSTYLE */);
            if ((ex & Native.WS_EX_TRANSPARENT) != 0) continue;   // a click-through overlay is not a window to hide behind
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_NOACTIVATE) != 0) continue;
            string cls = Native.ClassName(h);
            if (Skip.Contains(cls)) continue;
            RECT r;
            // The frame as drawn (Windows 10/11 add invisible resize borders to GetWindowRect).
            if (Native.DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, (int*)&r, (uint)sizeof(RECT)) != 0
                && Native.GetWindowRect(h, &r) == 0) continue;
            var ri = Monitors.ToRect(r);
            if (ri.Width <= 0 || ri.Height <= 0) continue;
            var rf = ToF(ri);
            if (!rf.Intersects(bounds)) continue;
            long style = Native.GetWindowLongPtr(h, -16 /* GWL_STYLE */);
            var on = Monitors.Describe(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST));
            bool full = Fullscreen.IsFullscreen(ri, on.Bounds, cls, (style & 0x00C00000) == 0x00C00000, Native.IsZoomed(h) != 0,
                (style & 0x00040000) != 0);
            windows.Add(new SeekWindow(h, rf, full));
        }
        return new SeekWorld(bounds, m.WorkF, m.DpiScale, Monitors.All().Select(x => ToF(x.Bounds)).ToList(), bar, barEdge, windows, body);
    }

    private static RectF ToF(RectI r) => new(r.Left, r.Top, r.Width, r.Height);

    // ---------- her clip ----------

    /// <summary>What of her may draw (screen px): the side of a window's edge or of the taskbar's top she peeks from.
    /// Null for a screen edge (the screen itself hides the rest).</summary>
    public static RectF? Keep(HideSpot s)
    {
        const float Big = 100000f;
        return s.Kind switch
        {
            SpotKind.Window when s.Side == 0 => new RectF(-Big, -Big, s.Behind.X + Big, 2 * Big),
            SpotKind.Window => new RectF(s.Behind.Right, -Big, 2 * Big, 2 * Big),
            SpotKind.Taskbar => new RectF(-Big, -Big, 2 * Big, s.Behind.Y + Big),
            _ => null,
        };
    }

    /// <summary>Clips her (her whole canvas: body, leaf, stars) to <paramref name="keep"/> in screen px, allowing for her
    /// root's scale about its centre point; null removes the clip.</summary>
    public void Clip(RectF? keep)
    {
        var root = _v.Root;
        if (keep is not { } k)
        {
            if (_clip != null) { root.Clip = null; _clip.Dispose(); _clip = null; }
            return;
        }
        if (_clip == null) { _clip = _c.CreateInsetClip(); root.Clip = _clip; }
        var pos = _v.Position;
        var cp = new Vector2(root.CenterPoint.X, root.CenterPoint.Y);
        var s = new Vector2(MathF.Max(0.05f, root.Scale.X), MathF.Max(0.05f, root.Scale.Y));
        // Screen → her canvas: local = cp + (screen − (pos + cp)) / scale.
        Vector2 Local(Vector2 screen) => cp + (screen - (pos + cp)) / s;
        var a = Local(new Vector2(k.X, k.Y));
        var b = Local(new Vector2(k.Right, k.Bottom));
        float w = _v.CanvasPx.Width, h = _v.CanvasPx.Height;
        _clip.LeftInset = Math.Clamp(a.X, 0, w);
        _clip.TopInset = Math.Clamp(a.Y, 0, h);
        _clip.RightInset = Math.Clamp(w - b.X, 0, w);
        _clip.BottomInset = Math.Clamp(h - b.Y, 0, h);
    }

    // ---------- her paws (the cover pose) ----------

    /// <summary>Two soft paws over her eyes; the left one sits lower, so a sliver of eye shows.</summary>
    public void Paws(bool on)
    {
        foreach (var p in _paws) { _v.Extras.Children.Remove(p); p.Dispose(); }
        _paws = [];
        _pawSurface?.Dispose();
        _pawSurface = null;
        if (!on) return;
        float s = _v.Scale;
        var size = new Vector2(34 * s, 26 * s);
        _pawSurface = new SwapSurface(_host, new SizeI((int)MathF.Ceiling(size.X), (int)MathF.Ceiling(size.Y)));
        _pawSurface.DrawCurrent(dc =>
        {
            dc.Clear();
            var c = size / 2;
            dc.FillEllipse(c, size / 2 - new Vector2(1.5f * s), dc.SolidBrush(new D2DColor(Rgba.Parse("#F7F3FF"))));
            dc.DrawEllipse(c, size / 2 - new Vector2(1.5f * s), dc.SolidBrush(new D2DColor(Rgba.Parse("#CFC4F2"))), 1.4f * s, 0);
            var line = dc.SolidBrush(new D2DColor(Rgba.Parse("#CFC4F2")));
            for (int i = -1; i <= 1; i++)   // little fingers
                dc.FillEllipse(c + new Vector2(i * 7 * s, -7 * s), new Vector2(1.1f * s, 2.6f * s), line);
        });
        // Rig eyes sit at canvas units (130, 155) and (170, 155).
        foreach (var (x, y) in new[] { (130f, 162f), (170f, 154f) })
        {
            var sprite = _c.CreateSpriteVisual();
            sprite.Size = size;
            sprite.Brush = _pawSurface.Brushes[_pawSurface.Current];
            sprite.Offset = new Vector3(x * s - size.X / 2, y * s - size.Y / 2, 0);
            _v.Extras.Children.InsertAtTop(sprite);
            _paws = [.. _paws, sprite];
        }
    }

    // ---------- tokens ----------

    private Rgba T(string light, string dark) => Rgba.Parse(Dark ? dark : light);
    private Rgba Tint => T("#EEE8FF", "#3A3450");
    private Rgba Accent => T("#5B47C4", "#B6A9FF");
    private Rgba OnAccent => T("#FFFFFF", "#1C1733");
    private Rgba Surface => T("#FFFFFF", "#2A2630");
    private Rgba Ink => T("#24212B", "#EFECF4");
    private Rgba T3 => T("#6B6776", "#A29DAD");
    private Rgba Soft => T("#ECE7FF", "#3A3354");
    private Rgba SoftInk => T("#4636A6", "#D2C9FF");
    private Rgba Outline => T("#0000001A", "#FFFFFF1A");
    private Rgba WarmTint => T("#FBF1DC", "#3E3524");
    private Rgba GoldInk => T("#7A5A1C", "#F2C86A");
    private Rgba ColdTint => T("#E6EEF8", "#26303D");
    private Rgba Cold => T("#2F5F96", "#9CC2EE");
    private static readonly Rgba Gold = Rgba.Parse("#F2C86A");
    private float D(float dip) => dip * Dpi;

    // ---------- pieces ----------

    /// <summary>One drawn piece: a sprite at a screen centre, animated by <see cref="Step"/>.</summary>
    private sealed class Piece
    {
        public required SpriteVisual Visual;
        public required SwapSurface Surface;
        public Vector2 Centre;
        public float Age, Delay, Life = float.PositiveInfinity;
        public Action<Piece, float>? Animate;   // (piece, seconds since its delay)
    }

    private Piece Add(Vector2 size, Vector2 centre, Action<D2DContext, Vector2> draw, float life = float.PositiveInfinity, float delay = 0,
        Action<Piece, float>? animate = null)
    {
        var px = new SizeI(Math.Max(1, (int)MathF.Ceiling(size.X)), Math.Max(1, (int)MathF.Ceiling(size.Y)));
        var surface = new SwapSurface(_host, px);
        surface.DrawCurrent(dc => { dc.Clear(); draw(dc, new Vector2(px.Width, px.Height)); });
        var v = _c.CreateSpriteVisual();
        v.Size = new Vector2(px.Width, px.Height);
        v.Brush = surface.Brushes[surface.Current];
        v.AnchorPoint = new Vector2(0.5f);
        v.CenterPoint = new Vector3(v.Size / 2, 0);
        _host.Root.Children.InsertAtTop(v);
        var p = new Piece { Visual = v, Surface = surface, Centre = centre, Life = life, Delay = delay, Animate = animate };
        Place(p, centre);
        if (delay > 0) v.Opacity = 0;
        _pieces.Add(p);
        return p;
    }

    private void Place(Piece p, Vector2 centre)
    {
        p.Centre = centre;
        p.Visual.Offset = new Vector3(centre - _v.Origin, 0);
    }

    private void Drop(Piece? p)
    {
        if (p is null || !_pieces.Remove(p)) return;
        _host.Root.Children.Remove(p.Visual);
        p.Visual.Dispose();
        p.Surface.Dispose();
    }

    /// <summary>Advances every piece's animation; pieces past their life go.</summary>
    public void Step(float dt)
    {
        foreach (var p in _pieces.ToList())
        {
            p.Age += dt;
            float t = p.Age - p.Delay;
            if (t < 0) continue;
            if (t >= p.Life) { Drop(p); if (p == _chip) _chip = null; continue; }
            if (p.Animate != null) p.Animate(p, t);
            else p.Visual.Opacity = 1;
        }
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        t = Math.Clamp(t, 0, 1);
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    /// <summary>Opacity for a piece that fades in over <paramref name="fin"/> and out over the last <paramref name="fout"/> s.</summary>
    private static float Fade(float t, float life, float fin, float fout) =>
        Math.Clamp(MathF.Min(fin <= 0 ? 1 : t / fin, fout <= 0 ? 1 : (life - t) / fout), 0, 1);

    // ---------- «٣ ٢ ١» and the pips ----------

    public const float CountSeconds = 0.7f;

    /// <summary>Three tint discs over her head, one after another, 0.7 s each: 0.6 → 1 with an overshoot, then a fade.</summary>
    public void Countdown(Vector2 overHead)
    {
        float d = D(34);
        for (int i = 0; i < 3; i++)
        {
            string digit = HideSeekText.Count(3 - i);
            Add(new Vector2(d + 4, d + 4), overHead, (dc, size) =>
            {
                var c = size / 2;
                dc.FillEllipse(c, new Vector2(d / 2), dc.SolidBrush(new D2DColor(Tint)));
                dc.TextLayout(digit, D(17), 600, new D2DRect { Left = 0, Top = 0, Right = size.X, Bottom = size.Y },
                    dc.SolidBrush(new D2DColor(Accent)), TextAlign.Centre, rtl: true);
            }, life: CountSeconds, delay: i * CountSeconds, animate: (p, t) =>
            {
                float s = Reduced ? 1 : 0.6f + 0.4f * EaseOutBack(t / 0.25f);
                p.Visual.Scale = new Vector3(s, s, 1);
                p.Visual.Opacity = Fade(t, CountSeconds, Reduced ? 0 : 0.08f, 0.18f);
            });
        }
    }

    /// <summary>Three round pips under her: filled for rounds she was found in, a ring for the rest (accent for the
    /// round being played). Null hides them.</summary>
    public void Pips(Vector2? under, int round, IReadOnlyList<bool> results)
    {
        Drop(_pips);
        _pips = null;
        if (under is not { } at) return;
        float r = D(3.5f), gap = D(6);
        var size = new Vector2(3 * 2 * r + 2 * gap + 4, 2 * r + 4);
        var accent = Accent;
        var t3 = T3;
        bool[] found = [.. results];
        _pips = Add(size, at, (dc, s) =>
        {
            for (int i = 0; i < 3; i++)
            {
                // RTL: the first round is the rightmost pip.
                var c = new Vector2(s.X - 2 - r - i * (2 * r + gap), s.Y / 2);
                bool filled = i < found.Length && found[i];
                var colour = filled || i == round - 1 ? accent : t3;
                if (filled) dc.FillEllipse(c, new Vector2(r), dc.SolidBrush(new D2DColor(colour)));
                else dc.DrawEllipse(c, new Vector2(r - 0.6f), dc.SolidBrush(new D2DColor(colour)), 1.2f * Dpi, 0);
            }
        });
    }

    // ---------- sparks ----------

    private static readonly IReadOnlyList<PathCommand> SparkPath =
        PathData.Parse("M0 -10 Q1.8 -1.8 10 0 Q1.8 1.8 0 10 Q-1.8 1.8 -10 0 Q-1.8 -1.8 0 -10 Z");

    private Piece Spark(Vector2 at, float dip, float life, float delay, Action<Piece, float> animate) =>
        Add(new Vector2(D(dip) + 2, D(dip) + 2), at, (dc, size) =>
        {
            dc.PushTransform(Matrix3x2.CreateScale(D(dip) / 20f) * Matrix3x2.CreateTranslation(size / 2));
            dc.FillGeometry(dc.Path(SparkPath), dc.SolidBrush(new D2DColor(Gold)));
            dc.PopTransform();
        }, life, delay, animate);

    /// <summary>A curved flight's control point: off to one side, so the trail curves and does not point at her.</summary>
    public static Vector2 Curve(Vector2 from, Vector2 to, Random rng)
    {
        var mid = (from + to) / 2;
        var d = to - from;
        var normal = d.LengthSquared() < 1 ? Vector2.UnitY : Vector2.Normalize(new Vector2(-d.Y, d.X));
        return mid + normal * d.Length() * (0.35f + 0.25f * (float)rng.NextDouble()) * (rng.Next(2) == 0 ? 1 : -1);
    }

    public static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;

    /// <summary>Three tiny gold sparks dropped along the first part of her curved flight, each fading over 0.6 s.</summary>
    public void Trail(Vector2 from, Vector2 control, Vector2 to, float flightSeconds)
    {
        for (int i = 0; i < 3; i++)
        {
            float u = 0.15f + 0.22f * i;
            Spark(Bezier(from, control, to, u), 9 - i, 0.6f, u * flightSeconds, (p, t) =>
            {
                p.Visual.Opacity = 1 - t / 0.6f;
                float s = Reduced ? 1 : 1 - 0.4f * t / 0.6f;
                p.Visual.Scale = new Vector3(s, s, 1);
            });
        }
    }

    /// <summary>Found: four gold sparks burst out of her.</summary>
    public void Burst(Vector2 centre, float radius)
    {
        foreach (var dir in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1.2f, 0.2f), new Vector2(1.2f, 0.3f) })
        {
            var from = centre + Vector2.Normalize(dir) * radius * 0.7f;
            Spark(from, 11, 0.8f, 0, (p, t) =>
            {
                if (!Reduced) Place(p, from + Vector2.Normalize(dir) * D(26) * MathF.Min(1, t / 0.45f));
                p.Visual.Opacity = Fade(t, 0.8f, 0.05f, 0.3f);
            });
        }
    }

    // ---------- the giggle, the chip, the outline ----------

    /// <summary>«هِهِ» in gold near what shows of her, floating up a little and fading (1.2 s).</summary>
    public void Giggle(Vector2 at)
    {
        string text = HideSeekText.Giggle;
        var m = DWrite.Measure(text, D(15), 600, true);
        var gold = GoldInk;
        Add(new Vector2(m.X + D(8), m.Y + D(6)), at, (dc, size) =>
            dc.TextLayout(text, D(15), 600, new D2DRect { Left = 0, Top = 0, Right = size.X, Bottom = size.Y },
                dc.SolidBrush(new D2DColor(gold)), TextAlign.Centre, rtl: true),
            life: 1.2f, animate: (p, t) =>
            {
                if (!Reduced) Place(p, at - new Vector2(0, D(8) * t / 1.2f));
                p.Visual.Opacity = Fade(t, 1.2f, 0.15f, 0.35f);
            });
    }

    /// <summary>The whisper chip beside the pointer for 1.2 s: warm (gold dots) or cold (a snowflake).</summary>
    public void Whisper(Warmth w, Vector2 pointer, RectF work)
    {
        Drop(_chip);
        LastWhisper = w;
        string text = HideSeekText.Whisper(w);
        bool warm = w is Warmth.Hot or Warmth.Warm;
        var bg = warm ? WarmTint : ColdTint;
        var ink = warm ? GoldInk : Cold;
        float font = D(13), h = D(28), pad = D(12), icon = D(12), gap = D(6);
        var m = DWrite.Measure(text, font, 600, true);
        var size = new Vector2(pad + m.X + gap + icon + pad, h);
        // Beside the pointer (below-left, as drawn), inside the work area.
        var centre = new Vector2(pointer.X - D(14) - size.X / 2, pointer.Y + D(22) + h / 2);
        if (centre.X - size.X / 2 < work.X) centre.X = pointer.X + D(18) + size.X / 2;
        centre = new Vector2(Math.Clamp(centre.X, work.X + size.X / 2, work.Right - size.X / 2),
            Math.Clamp(centre.Y, work.Y + h / 2, work.Bottom - h / 2));
        _chip = Add(size + new Vector2(4, 4), centre, (dc, s) =>
        {
            var r = new D2DRect { Left = 2, Top = 2, Right = s.X - 2, Bottom = s.Y - 2 };
            dc.FillRoundedRect(r, h / 2, dc.SolidBrush(new D2DColor(bg)));
            dc.DrawRoundedRect(r, h / 2, dc.SolidBrush(new D2DColor(Outline)), 1);
            var inkBrush = dc.SolidBrush(new D2DColor(ink));
            // RTL: the icon leads, on the right.
            var ic = new Vector2(r.Right - pad - icon / 2, s.Y / 2);
            if (warm)
            {
                var dot = dc.SolidBrush(new D2DColor(Gold));
                dc.FillEllipse(ic + new Vector2(-icon * 0.22f, 0), new Vector2(icon * 0.17f), dot);
                dc.FillEllipse(ic + new Vector2(icon * 0.22f, 0), new Vector2(icon * 0.17f), dot);
            }
            else
            {
                var round = dc.StrokeStyle("round");
                for (int i = 0; i < 3; i++)
                {
                    float a = i * MathF.PI / 3;
                    var d = new Vector2(MathF.Cos(a), MathF.Sin(a)) * icon / 2;
                    dc.DrawGeometry(dc.Path(PathData.Parse(FormattableString.Invariant($"M{ic.X - d.X} {ic.Y - d.Y} L{ic.X + d.X} {ic.Y + d.Y}"))),
                        inkBrush, 1.5f * Dpi, round);
                }
            }
            dc.TextLayout(text, font, 600, new D2DRect { Left = r.Left + pad, Top = r.Top, Right = ic.X - icon / 2 - gap, Bottom = r.Bottom },
                inkBrush, TextAlign.Start, rtl: true);
        }, life: 1.2f, animate: (p, t) =>
        {
            p.Visual.Opacity = Fade(t, 1.2f, Reduced ? 0 : 0.15f, 0.25f);
            if (!Reduced) Place(p, centre + new Vector2(0, D(4) * (1 - MathF.Min(1, t / 0.15f))));
        });
    }

    /// <summary>The dashed outline where she was, for 1.5 s.</summary>
    public void Spot(RectF where)
    {
        float m = D(4);
        var size = new Vector2(where.Width + 2 * m, where.Height + 2 * m);
        var accent = Accent;
        Add(size, new Vector2(where.X + where.Width / 2, where.Y + where.Height / 2), (dc, s) =>
        {
            float w = 1.5f * Dpi;
            var r = new D2DRect { Left = w, Top = w, Right = s.X - w, Bottom = s.Y - w };
            var rr = MathF.Min(D(16), MathF.Min(s.X, s.Y) / 2 - w);
            var path = PathData.Parse(FormattableString.Invariant(
                $"M{r.Left + rr} {r.Top} H{r.Right - rr} A{rr} {rr} 0 0 1 {r.Right} {r.Top + rr} V{r.Bottom - rr} A{rr} {rr} 0 0 1 {r.Right - rr} {r.Bottom} H{r.Left + rr} A{rr} {rr} 0 0 1 {r.Left} {r.Bottom - rr} V{r.Top + rr} A{rr} {rr} 0 0 1 {r.Left + rr} {r.Top} Z"));
            dc.DrawGeometry(dc.Path(path), dc.SolidBrush(new D2DColor(accent)), w, dc.StrokeStyle("round", [2.5f, 2.5f]));
        }, life: 1.5f, animate: (p, t) => p.Visual.Opacity = Fade(t, 1.5f, 0.1f, 0.3f));
    }

    // ---------- «منعيدها؟» ----------

    /// <summary>The result card over her head: one line, «يلا» (accent) and «خلص» (soft), RTL, inside the work area.</summary>
    public void Ask(string line, RectF her, RectF work)
    {
        Drop(_ask);
        float font = D(15), pad = D(16), btnH = D(34), btnPad = D(18), gap = D(8), radius = D(18), tail = D(8);
        var lineSize = DWrite.Measure(line, font, 400, true);
        var again = DWrite.Measure(HideSeekText.Again, font, 600, true);
        var done = DWrite.Measure(HideSeekText.Done, font, 600, true);
        float bw1 = again.X + 2 * btnPad, bw2 = done.X + 2 * btnPad;
        float w = MathF.Max(lineSize.X, bw1 + gap + bw2) + 2 * pad;
        float h = pad + lineSize.Y + D(12) + btnH + pad;
        var size = new Vector2(w + 8, h + tail + 8);
        float cx = Math.Clamp(her.X + her.Width / 2, work.X + size.X / 2, work.Right - size.X / 2);
        float cy = her.Y - D(54) - size.Y / 2;   // above her leaf
        bool below = cy - size.Y / 2 < work.Y;
        if (below) cy = her.Bottom + D(28) + size.Y / 2;   // no room above: under her (and her pips), the tail up
        var centre = new Vector2(cx, cy);
        var origin = centre - size / 2;
        float top = below ? 4 + tail : 4;
        // The buttons sit at the card's start edge (right, RTL): «يلا» first.
        float by = top + pad + lineSize.Y + D(12);
        var againLocal = new D2DRect { Right = 4 + w - pad, Left = 4 + w - pad - bw1, Top = by, Bottom = by + btnH };
        var doneLocal = new D2DRect { Right = againLocal.Left - gap, Left = againLocal.Left - gap - bw2, Top = by, Bottom = by + btnH };
        AgainButton = new RectF(origin.X + againLocal.Left, origin.Y + againLocal.Top, bw1, btnH);
        DoneButton = new RectF(origin.X + doneLocal.Left, origin.Y + doneLocal.Top, bw2, btnH);
        float tailX = Math.Clamp(her.X + her.Width / 2 - origin.X, 4 + radius + tail, 4 + w - radius - tail);
        (Rgba surface, Rgba ink, Rgba accent, Rgba onAccent, Rgba soft, Rgba softInk, Rgba outline) = (Surface, Ink, Accent, OnAccent, Soft, SoftInk, Outline);
        _ask = Add(size, centre, (dc, s) =>
        {
            var card = new D2DRect { Left = 4, Top = top, Right = 4 + w, Bottom = top + h };
            var shadow = new D2DRect { Left = card.Left, Top = card.Top + 2, Right = card.Right, Bottom = card.Bottom + 2 };
            dc.FillRoundedRect(shadow, radius, dc.SolidBrush(new D2DColor(Rgba.Parse("#000000"), Dark ? 0.35f : 0.10f)));
            var fill = dc.SolidBrush(new D2DColor(surface));
            dc.FillRoundedRect(card, radius, fill);
            dc.DrawRoundedRect(card, radius, dc.SolidBrush(new D2DColor(outline)), 1);
            var tailPath = below
                ? FormattableString.Invariant($"M{tailX - tail} {card.Top + 1} L{tailX} {card.Top - tail} L{tailX + tail} {card.Top + 1} Z")
                : FormattableString.Invariant($"M{tailX - tail} {card.Bottom - 1} L{tailX} {card.Bottom + tail} L{tailX + tail} {card.Bottom - 1} Z");
            dc.FillGeometry(dc.Path(PathData.Parse(tailPath)), fill);
            dc.TextLayout(line, font, 400, new D2DRect { Left = card.Left + pad, Top = card.Top + pad, Right = card.Right - pad, Bottom = card.Top + pad + lineSize.Y },
                dc.SolidBrush(new D2DColor(ink)), TextAlign.Start, rtl: true);
            dc.FillRoundedRect(againLocal, D(10), dc.SolidBrush(new D2DColor(accent)));
            dc.TextLayout(HideSeekText.Again, font, 600, againLocal, dc.SolidBrush(new D2DColor(onAccent)), TextAlign.Centre, rtl: true);
            dc.FillRoundedRect(doneLocal, D(10), dc.SolidBrush(new D2DColor(soft)));
            dc.TextLayout(HideSeekText.Done, font, 600, doneLocal, dc.SolidBrush(new D2DColor(softInk)), TextAlign.Centre, rtl: true);
        }, animate: (p, t) =>
        {
            p.Visual.Opacity = Reduced ? 1 : MathF.Min(1, t / 0.22f);
            float k = Reduced ? 1 : 0.98f + 0.02f * MathF.Min(1, t / 0.22f);
            p.Visual.Scale = new Vector3(k, k, 1);
        });
    }

    public void CloseAsk()
    {
        Drop(_ask);
        _ask = null;
    }

    /// <summary>Everything off: pieces, paws, clip.</summary>
    public void Clear()
    {
        foreach (var p in _pieces.ToList()) Drop(p);
        _chip = _ask = _pips = null;
        LastWhisper = null;
        Paws(false);
        Clip(null);
    }

    public void Dispose() => Clear();
}
