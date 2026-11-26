using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Text;

namespace Sahar.Core.Games;

/// <summary>Where she hides: behind a real window's edge, behind the taskbar, or at a screen edge (spec §5.3).</summary>
public enum SpotKind { Window, Taskbar, Edge }

/// <summary>How close the pointer is to what shows of her (spec §5.5).</summary>
public enum Warmth { Hot, Warm, Cold, Far }

/// <summary>A visible top-level window that is not hers, in screen pixels. Lists run top of the z-order first.
/// <paramref name="Id"/> is an opaque handle (never a title).</summary>
public readonly record struct SeekWindow(long Id, RectF Rect, bool Fullscreen = false);

/// <summary>What the game knows of the desktop when she hides, all in screen pixels: the game's monitor and its work
/// area, every monitor, the taskbar (its rect and edge 0 left, 1 top, 2 right, 3 bottom; −1 none; an auto-hidden bar
/// has no thickness), the windows (top first) and her body's size.</summary>
public sealed record SeekWorld(RectF Monitor, RectF Work, float Dpi, IReadOnlyList<RectF> Monitors, RectF Taskbar, int TaskbarEdge,
    IReadOnlyList<SeekWindow> Windows, Vector2 Body);

/// <summary>One hiding spot. <paramref name="TopLeft"/> is her body's top-left; <paramref name="Visible"/> is the part of
/// her that shows (the rest is clipped away, as if behind <paramref name="Behind"/>); <paramref name="Side"/> is the side
/// of <paramref name="Behind"/> she peeks from (window: 0 left / 2 right; taskbar: 1 its top; edge: the screen edge).</summary>
public readonly record struct HideSpot(SpotKind Kind, Vector2 TopLeft, RectF Visible, long WindowId, RectF Behind, int Side);

/// <summary>
/// «الغميضة» (0.9, smart in 0.16). She never moves or covers his windows: behind one, her own visual is only clipped
/// at its edge (owner ruling 4). Rules that always hold (Review Focus 5): no spot is near the pointer, off her monitor,
/// spilling onto another monitor, or under a full-screen app; a spot is not used twice in a game.
/// </summary>
public static class HideSeek
{
    public const int Rounds = 3;
    public const double HintSeconds = 20, GiveUpSeconds = 60;

    /// <summary>A spot is at least this far (DIP) from the pointer when she picks it.</summary>
    public const float PointerDip = 300f;
    /// <summary>A spot this close (DIP) to one already used this game counts as the same spot.</summary>
    public const float RepeatDip = 150f;
    /// <summary>Within this (DIP) she trembles from the giggle.</summary>
    public const float TrembleDip = 120f;

    private const float Shown = 0.27f;         // a screen edge: about a quarter of her
    private const float WindowShown = 0.38f;   // a window's edge: one eye and a cheek
    private const float CrownShown = 0.12f;    // the taskbar: the top of her head...
    private const float LeafUnits = 0.38f;     // ...and her leaf above it (58 of her body's 154 rig units)
    private const float JitterDip = 2f;
    private static readonly float[] Along = [0.12f, 0.3f, 0.5f, 0.7f, 0.88f];
    private static readonly float[] AlongWindow = [0.25f, 0.5f, 0.75f];

    /// <summary>The weights of the first pick: window 50 %, taskbar 30 %, edge 20 % (shared out among the kinds that
    /// have a spot).</summary>
    public static float Weight(SpotKind k) => k switch { SpotKind.Window => 0.5f, SpotKind.Taskbar => 0.3f, _ => 0.2f };

    // ---------- warmer or colder ----------

    public static Warmth Warmth(float dip) => dip switch
    {
        < 200 => Sahar.Core.Games.Warmth.Hot,
        < 450 => Sahar.Core.Games.Warmth.Warm,
        < 900 => Sahar.Core.Games.Warmth.Cold,
        _ => Sahar.Core.Games.Warmth.Far,
    };

    public static bool Trembles(float dip) => dip < TrembleDip;

    /// <summary>The pointer's distance (DIP) to what shows of her.</summary>
    public static float DistanceDip(HideSpot s, Vector2 pointer, float dpi) => Distance(s.Visible, pointer) / dpi;

    /// <summary>A click on what shows of her (6 DIP of slack), never on her hidden part.</summary>
    public static bool Hit(HideSpot s, Vector2 click, float dpi)
    {
        float m = 6 * dpi;
        return new RectF(s.Visible.X - m, s.Visible.Y - m, s.Visible.Width + 2 * m, s.Visible.Height + 2 * m).Contains(click.X, click.Y);
    }

    /// <summary>Seconds to the next giggle: 8 ± 2.</summary>
    public static double NextGiggle(Random rng) => 6 + 4 * rng.NextDouble();

    public static bool Hint(double seconds) => seconds >= HintSeconds;
    public static bool GiveUp(double seconds) => seconds >= GiveUpSeconds;

    // ---------- the spots ----------

    /// <summary>
    /// Every good spot, ranked: kinds in a weighted random order (50/30/20), the spots of one kind shuffled. The first is
    /// her pick, the rest are fallbacks. Empty when nothing qualifies (the pointer is near every spot, a full-screen app
    /// covers the screen): then she waits rather than hide near the pointer.
    /// </summary>
    public static List<HideSpot> Spots(SeekWorld w, Vector2 pointer, Random rng, IReadOnlyList<HideSpot>? used = null)
    {
        var all = Candidates(w, pointer, used);
        var kinds = all.Select(s => s.Kind).Distinct().ToList();
        var ranked = new List<HideSpot>(all.Count);
        while (kinds.Count > 0)
        {
            float total = kinds.Sum(Weight), r = (float)rng.NextDouble() * total;
            int i = 0;
            for (; i < kinds.Count - 1; i++)
            {
                r -= Weight(kinds[i]);
                if (r < 0) break;
            }
            var kind = kinds[i];
            kinds.RemoveAt(i);
            var mine = all.Where(s => s.Kind == kind).ToList();
            for (int n = mine.Count - 1; n > 0; n--)
            {
                int j = rng.Next(n + 1);
                (mine[n], mine[j]) = (mine[j], mine[n]);
            }
            ranked.AddRange(mine);
        }
        return ranked;
    }

    /// <summary>The window or taskbar she is behind moved, closed, went full-screen or got covered: false, and she
    /// slides to <see cref="Fallback"/>.</summary>
    public static bool StillThere(HideSpot s, SeekWorld now)
    {
        if (UnderFullscreen(s.Visible, now)) return false;
        float jitter = JitterDip * now.Dpi;
        switch (s.Kind)
        {
            case SpotKind.Window:
                int at = -1;
                for (int i = 0; i < now.Windows.Count; i++) if (now.Windows[i].Id == s.WindowId) { at = i; break; }
                if (at < 0 || now.Windows[at].Fullscreen || !Near(now.Windows[at].Rect, s.Behind, jitter)) return false;
                for (int i = 0; i < at; i++) if (now.Windows[i].Rect.Intersects(s.Visible)) return false;
                return true;
            case SpotKind.Taskbar:
                return now.TaskbarEdge == 3 && now.Taskbar.Height > 0 && Near(now.Taskbar, s.Behind, jitter);
            default:
                return true;
        }
    }

    /// <summary>The nearest edge spot to where she is (<paramref name="from"/>, her top-left) that keeps every rule.</summary>
    public static HideSpot? Fallback(SeekWorld w, Vector2 pointer, Vector2 from, IReadOnlyList<HideSpot>? used = null)
    {
        HideSpot? best = null;
        float bestD = float.MaxValue;
        foreach (var s in Candidates(w, pointer, used, onlyEdges: true))
        {
            float d = Vector2.Distance(s.TopLeft, from);
            if (d < bestD) (best, bestD) = (s, d);
        }
        return best;
    }

    private static List<HideSpot> Candidates(SeekWorld w, Vector2 pointer, IReadOnlyList<HideSpot>? used, bool onlyEdges = false)
    {
        var raw = new List<HideSpot>();
        if (!onlyEdges)
        {
            WindowSpots(w, raw);
            TaskbarSpots(w, raw);
        }
        EdgeSpots(w, raw);
        float near = PointerDip * w.Dpi, again = RepeatDip * w.Dpi;
        return raw.Where(s =>
                s.Visible.Width > 0 && s.Visible.Height > 0 && w.Work.Contains(s.Visible)
                && !Spills(Body(s, w), w) && !UnderFullscreen(s.Visible, w)
                && Distance(s.Visible, pointer) >= near
                && (used is null || used.All(u => Vector2.Distance(Centre(u.Visible), Centre(s.Visible)) >= again)))
            .ToList();
    }

    private static void WindowSpots(SeekWorld w, List<HideSpot> into)
    {
        var b = w.Body;
        float margin = 8 * w.Dpi;
        for (int i = 0; i < w.Windows.Count; i++)
        {
            var win = w.Windows[i];
            var r = win.Rect;
            if (win.Fullscreen || !r.Intersects(w.Monitor)) continue;
            if (r.Width < b.X * 2.5f || r.Height < b.Y * (1.6f + LeafUnits)) continue;   // too small to hide behind
            foreach (int side in (ReadOnlySpan<int>)[0, 2])
                foreach (float along in AlongWindow)
                {
                    // Her body (and her leaf above it) stays within the window's height: the clip is one straight edge.
                    float cy = r.Y + r.Height * along;
                    float lo = r.Y + b.Y * LeafUnits + margin, hi = r.Bottom - b.Y - margin;
                    if (lo > hi) continue;
                    float y = Math.Clamp(cy - b.Y / 2, lo, hi);
                    float x = side == 0 ? r.X - b.X * WindowShown : r.Right - b.X * (1 - WindowShown);
                    var visible = side == 0 ? new RectF(x, y, r.X - x, b.Y) : new RectF(r.Right, y, x + b.X - r.Right, b.Y);
                    bool covered = false;
                    for (int j = 0; j < i && !covered; j++) covered = w.Windows[j].Rect.Intersects(visible);
                    if (!covered) into.Add(new HideSpot(SpotKind.Window, new Vector2(x, y), visible, win.Id, r, side));
                }
        }
    }

    private static void TaskbarSpots(SeekWorld w, List<HideSpot> into)
    {
        // Only a bottom bar: her leaf pokes up above it (a side or top bar would turn her sideways).
        if (w.TaskbarEdge != 3 || w.Taskbar.Height <= 0) return;
        var b = w.Body;
        var bar = w.Taskbar;
        float y = bar.Y - b.Y * CrownShown;
        float top = y - b.Y * LeafUnits;
        foreach (float along in Along)
        {
            float x = bar.X + (bar.Width - b.X) * along;
            var visible = new RectF(x + b.X * 0.3f, top, b.X * 0.4f, bar.Y - top);
            into.Add(new HideSpot(SpotKind.Taskbar, new Vector2(x, y), visible, 0, bar, 1));
        }
    }

    private static void EdgeSpots(SeekWorld w, List<HideSpot> into)
    {
        foreach (int edge in OpenEdges(w.Monitor, w.Monitors, w.TaskbarEdge))
            foreach (float along in Along)
            {
                var p = Spot(w.Work, w.Body, edge, along);
                var visible = new RectF(p.X, p.Y, w.Body.X, w.Body.Y).Intersect(w.Work);
                into.Add(new HideSpot(SpotKind.Edge, p, visible, 0, w.Work, edge));
            }
    }

    /// <summary>A screen-edge spot with about a quarter of her showing.</summary>
    /// <param name="edge">0 left, 1 top, 2 right, 3 bottom.</param>
    /// <param name="along">0–1 along that edge.</param>
    public static Vector2 Spot(RectF work, Vector2 body, int edge, float along)
    {
        along = Math.Clamp(along, 0.08f, 0.92f);
        float x = work.X + (work.Width - body.X) * along, y = work.Y + (work.Height - body.Y) * along;
        return (edge & 3) switch
        {
            0 => new Vector2(work.X - body.X * (1 - Shown), y),
            1 => new Vector2(x, work.Y - body.Y * (1 - Shown)),
            2 => new Vector2(work.Right - body.X * Shown, y),
            _ => new Vector2(x, work.Bottom - body.Y * Shown),
        };
    }

    /// <summary>Edges she can hide past: no other screen beyond them (her hidden part would show there) and not the
    /// taskbar's edge (0 left, 1 top, 2 right, 3 bottom; −1 no taskbar on this screen).</summary>
    public static List<int> OpenEdges(RectF monitor, IReadOnlyList<RectF> all, int taskbarEdge)
    {
        var open = new List<int>();
        for (int edge = 0; edge < 4; edge++)
        {
            if (edge == taskbarEdge) continue;
            float cx = monitor.X + monitor.Width / 2, cy = monitor.Y + monitor.Height / 2;
            var (px, py) = edge switch
            {
                0 => (monitor.X - 20, cy), 1 => (cx, monitor.Y - 20), 2 => (monitor.Right + 20, cy), _ => (cx, monitor.Bottom + 20),
            };
            if (!all.Any(m => m.Contains(px, py))) open.Add(edge);
        }
        return open;
    }

    // ---------- helpers ----------

    private static RectF Body(HideSpot s, SeekWorld w) => new(s.TopLeft.X, s.TopLeft.Y, w.Body.X, w.Body.Y);

    /// <summary>Any of her body on another monitor (where the clip would not hide it).</summary>
    private static bool Spills(RectF body, SeekWorld w) => w.Monitors.Any(m => m != w.Monitor && body.Intersects(m));

    private static bool UnderFullscreen(RectF r, SeekWorld w) => w.Windows.Any(x => x.Fullscreen && x.Rect.Intersects(r));

    private static bool Near(RectF a, RectF b, float tol) =>
        MathF.Abs(a.X - b.X) <= tol && MathF.Abs(a.Y - b.Y) <= tol && MathF.Abs(a.Right - b.Right) <= tol && MathF.Abs(a.Bottom - b.Bottom) <= tol;

    private static Vector2 Centre(RectF r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    private static float Distance(RectF r, Vector2 p)
    {
        float dx = MathF.Max(MathF.Max(r.X - p.X, 0), p.X - r.Right), dy = MathF.Max(MathF.Max(r.Y - p.Y, 0), p.Y - r.Bottom);
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// When the warmer/colder chip may show (spec §5.5): the pointer stopped (within 4 DIP) for 0.4 s, at most once every
/// 3 s, once per stop, never in the first 5 s of hiding.
/// </summary>
public sealed class WhisperGate
{
    public const double QuietSeconds = 5, StillSeconds = 0.4, EverySeconds = 3;
    private const float StillDip = 4f;
    private const double Eps = 1e-6;

    private Vector2? _anchor;
    private double _stillSince, _last = double.NegativeInfinity;
    private bool _shownThisStop;

    /// <param name="t">Seconds since she hid.</param>
    public bool Update(double t, Vector2 pointer, float dpi)
    {
        if (_anchor is not { } a || Vector2.Distance(a, pointer) > StillDip * dpi)
        {
            _anchor = pointer;
            _stillSince = t;
            _shownThisStop = false;
        }
        if (t < QuietSeconds || _shownThisStop) return false;
        if (t - _stillSince < StillSeconds - Eps || t - _last < EverySeconds - Eps) return false;
        _last = t;
        _shownThisStop = true;
        return true;
    }

    public void Reset() => (_anchor, _last, _shownThisStop) = (null, double.NegativeInfinity, false);
}

/// <summary>Her hide-and-seek words: short Syrian Arabic, written to a man (Address turns them), no emoji (ruling 3).</summary>
public static class HideSeekText
{
    public const string GaveUp = "هون كنت!";
    public const string Giggle = "هِهِ";
    public const string Again = "يلا";
    public const string Done = "خلص";

    public static string Whisper(Warmth w) => w switch
    {
        Warmth.Hot => "دافي… دافي كتير!",
        Warmth.Warm => "دافي…",
        Warmth.Cold => "برد…",
        _ => "برد… بعيد كتير",
    };

    /// <summary>«لقيتني! بـ ١٢ ثانية» (a find under a second is still one).</summary>
    public static string Found(double seconds) =>
        $"لقيتني! بـ {Digits.ToArabicIndic(Math.Max(1, (int)Math.Round(seconds)).ToString(System.Globalization.CultureInfo.InvariantCulture))} ثانية";

    /// <summary>The one-line result after three rounds, with «منعيدها؟».</summary>
    public static string End(int found) => found switch
    {
        <= 0 => "ما لقيتني ولا مرة. منعيدها؟",
        1 => "لقيتني مرة من تلاتة. منعيدها؟",
        2 => "لقيتني مرتين من تلاتة. منعيدها؟",
        _ => "لقيتني بالتلاتة! منعيدها؟",
    };

    /// <summary>The countdown disc's digit: «٣», «٢», «١».</summary>
    public static string Count(int n) => Digits.ToArabicIndic(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
