using System.Numerics;
using Sahar.Core.Art;
using Windows.UI.Composition;
using ExtrasFlags = Sahar.Core.Expressions.Extras;

namespace Sahar.Engine.Render;

/// <summary>
/// The little things around her during an expression (expressions board): notes, steam, hearts, rain, snow,
/// confetti, zzz, sweat and extra stars as looping compositor-animated sprites; headphones on her head.
/// Positions and timings are the board's, in design units.
/// </summary>
internal sealed class Extras : IDisposable
{
    private readonly SaharVisual _v;
    private readonly List<SwapSurface> _surfaces = [];
    private readonly List<Visual> _visuals = [];
    private ExtrasFlags _flags;

    private sealed record Particle(Vector2 At, Vector2 From, Vector2 To, double Seconds, double Begin, float[]? Opacity);

    public Extras(SaharVisual v) => _v = v;

    public void Set(ExtrasFlags flags)
    {
        _flags = flags;
        Rebuild();
    }

    /// <summary>Recreates every sprite for the current flags (after a size, DPI or device change).</summary>
    public void Rebuild()
    {
        Clear();
        var f = _flags;
        _v.Headphones.Container.IsVisible = f.HasFlag(ExtrasFlags.Headphones);

        if (f.HasFlag(ExtrasFlags.Notes))
            Group(20, (dc, s) => Fill(dc, s, "M2 -10 v18 a6 5 0 1 1 -4 -5 v-13 Z", "#B062C0", 20),
                new Particle(new(38, 80), new(0, 10), new(0, -16), 2.0, 0.6, [0, 1, 0]),
                new Particle(new(240, 74), new(0, 10), new(0, -16), 2.0, 1.4, [0, 1, 0]));
        if (f.HasFlag(ExtrasFlags.Hearts))
            Group(40, (dc, s) => Fill(dc, s, "M0 -6 c-6 -8 -18 -2 -12 8 l12 12 l12 -12 c6 -10 -6 -16 -12 -8 Z", "#FF8FA3", 40),
                new Particle(new(60, 76), new(0, 8), new(0, -10), 2.4, 0, [0, 1, 0]));
        if (f.HasFlag(ExtrasFlags.Steam))
            Group(50, (dc, s) => Stroke(dc, s, "M0 20 q-8 -10 0 -20 q8 -10 0 -20", "#E9A487", 5, 50),
                [.. new[] { (118f, 0.0), (150f, 0.7), (182f, 1.4) }.Select(p =>
                    new Particle(new(p.Item1, 40), new(0, 10), new(0, -14), 2.0, p.Item2, [0, 0.9f, 0]))]);
        if (f.HasFlag(ExtrasFlags.Rain))
            Group(18, (dc, s) => Stroke(dc, s, "M2 -6 l-4 12", "#7FA8D9", 3, 18),
                [.. new[] { (40f, 0.0), (100f, 0.4), (170f, 0.2), (230f, 0.7), (280f, 0.5) }.Select(p =>
                    new Particle(new(p.Item1 - 2, 6), Vector2.Zero, new(-10, 260), 1.2, p.Item2, null))]);
        if (f.HasFlag(ExtrasFlags.Snow))
            Group(10, (dc, s) => Snowflake(dc, s),
                [.. new[] { (40f, 4.0, 0.0), (90f, 5.0, 1.0), (200f, 4.5, 0.5), (250f, 3.8, 1.5), (150f, 5.0, 2.0) }.Select(p =>
                    new Particle(new(p.Item1, 0), Vector2.Zero, new(6, 260), p.Item2, p.Item3, null))]);
        if (f.HasFlag(ExtrasFlags.Confetti))
            foreach (var (x, color, secs, begin) in new[] { (40f, "#FF8FA3", 3.0, 0.0), (90f, "#FFC24A", 3.4, 0.6),
                         (160f, "#7FD6CF", 2.8, 0.3), (220f, "#B9B0FF", 3.2, 1.0), (270f, "#9EE493", 3.0, 0.5) })
                Group(14, (dc, s) => Confetti(dc, s, color), new Particle(new(x + 3, 5), Vector2.Zero, new(0, 260), secs, begin, null));
        if (f.HasFlag(ExtrasFlags.Zzz))
        {
            Group(30, (dc, s) => Letter(dc, s, "z", 24, 30), new Particle(new(207, 56), new(0, 6), new(6, -10), 2.5, 0, [0, 1, 0]));
            Group(30, (dc, s) => Letter(dc, s, "z", 16, 30), new Particle(new(232, 36), new(0, 6), new(6, -10), 2.5, 1, [0, 1, 0]));
        }
        if (f.HasFlag(ExtrasFlags.Sweat))
            Group(24, (dc, s) => Fill(dc, s, "M0 -7 q-6 10 0 14 q6 -4 0 -14", "#8CC8F2", 24),
                new Particle(new(200, 103), Vector2.Zero, new(0, 14), 1.4, 0, [1, 0]));
        if (f.HasFlag(ExtrasFlags.Stars))
            foreach (var (x, y, k, secs) in new[] { (40f, 40f, 0.9f, 2.0), (260f, 30f, 0.7f, 2.6), (270f, 200f, 0.6f, 3.0), (30f, 190f, 0.7f, 2.4) })
            {
                var sprite = Group(PartRenderer.StarUnits, (dc, s) => PartRenderer.StarShape(dc, s),
                    new Particle(new(x, y), Vector2.Zero, Vector2.Zero, secs, 0, [0.2f, 1, 0.2f]));
                sprite.Scale = new Vector3(k, k, 1);
            }
    }

    /// <summary>One surface drawn once, shared by a sprite per particle, each looping its own path.</summary>
    private SpriteVisual Group(float units, Action<D2DContext, float> draw, params Particle[] particles)
    {
        var c = _v.Root.Compositor;
        float s = _v.Scale;
        var surface = new SwapSurface(_v.Host, _v.SmallPx(units));
        surface.DrawCurrent(dc => draw(dc, s));
        _surfaces.Add(surface);
        SpriteVisual last = null!;
        foreach (var p in particles)
        {
            var sprite = c.CreateSpriteVisual();
            sprite.Brush = surface.Brushes[0];
            sprite.Size = new Vector2(surface.Size.Width, surface.Size.Height);
            sprite.AnchorPoint = new Vector2(0.5f);
            sprite.CenterPoint = new Vector3(surface.Size.Width / 2f, surface.Size.Height / 2f, 0);
            var start = (p.At + p.From) * s;
            sprite.Offset = new Vector3(start, 0);
            if (p.To != p.From)
            {
                var move = c.CreateVector3KeyFrameAnimation();
                move.InsertKeyFrame(0f, new Vector3(start, 0));
                move.InsertKeyFrame(1f, new Vector3((p.At + p.To) * s, 0), c.CreateLinearEasingFunction());
                Loop(move, p);
                sprite.StartAnimation("Offset", move);
            }
            if (p.Opacity is { } o)
            {
                var fade = c.CreateScalarKeyFrameAnimation();
                for (int i = 0; i < o.Length; i++) fade.InsertKeyFrame(o.Length == 1 ? 0 : i / (float)(o.Length - 1), o[i]);
                Loop(fade, p);
                sprite.Opacity = o[0];
                sprite.StartAnimation("Opacity", fade);
            }
            _v.Extras.Children.InsertAtTop(sprite);
            _visuals.Add(sprite);
            last = sprite;
        }
        return last;
    }

    private static void Loop(KeyFrameAnimation a, Particle p)
    {
        a.Duration = TimeSpan.FromSeconds(p.Seconds);
        a.DelayTime = TimeSpan.FromSeconds(p.Begin);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        a.IterationBehavior = AnimationIterationBehavior.Forever;
    }

    private static void Centre(D2DContext dc, float s, float units) =>
        dc.SetTransform(Matrix3x2.CreateTranslation(units / 2, units / 2) * Matrix3x2.CreateScale(s));

    private static void Fill(D2DContext dc, float s, string d, string color, float units)
    {
        Centre(dc, s, units);
        dc.FillGeometry(dc.Path(PathData.Parse(d)), dc.SolidBrush(new D2DColor(Rgba.Parse(color))));
    }

    private static void Stroke(D2DContext dc, float s, string d, string color, float width, float units)
    {
        Centre(dc, s, units);
        dc.DrawGeometry(dc.Path(PathData.Parse(d)), dc.SolidBrush(new D2DColor(Rgba.Parse(color))), width, dc.StrokeStyle("round"));
    }

    private static void Snowflake(D2DContext dc, float s)
    {
        Centre(dc, s, 10);
        dc.FillEllipse(Vector2.Zero, new Vector2(3), dc.SolidBrush(new D2DColor(Rgba.Parse("#FFFFFF"))));
        dc.DrawEllipse(Vector2.Zero, new Vector2(3), dc.SolidBrush(new D2DColor(Rgba.Parse("#BFDDF7"))), 1, dc.StrokeStyle("butt"));
    }

    private static void Confetti(D2DContext dc, float s, string color)
    {
        Centre(dc, s, 14);
        dc.FillGeometry(dc.Path(PathData.Parse("M-3 -3 a2 2 0 0 1 2 -2 h2 a2 2 0 0 1 2 2 v6 a2 2 0 0 1 -2 2 h-2 a2 2 0 0 1 -2 -2 Z")),
            dc.SolidBrush(new D2DColor(Rgba.Parse(color))));
    }

    private static void Letter(D2DContext dc, float s, string text, float size, float units)
    {
        Centre(dc, s, units);
        var half = units / 2;
        dc.Text(text, "Segoe UI", size, 600, new D2DRect { Left = -half, Top = -half, Right = half, Bottom = half },
            dc.SolidBrush(new D2DColor(Rgba.Parse("#B4B9DA"))));
    }

    private void Clear()
    {
        foreach (var v in _visuals) { _v.Extras.Children.Remove(v); v.Dispose(); }
        _visuals.Clear();
        foreach (var s in _surfaces) s.Dispose();
        _surfaces.Clear();
    }

    public void Dispose() => Clear();
}
