using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Windows.UI.Composition;

namespace Sahar.Engine.Activities;

/// <summary>Where the tag and its thread are now (screen px).</summary>
internal readonly record struct TagGeometry(RectF Tag, Vector2 From, Vector2 To, TagSide Side);

/// <summary>
/// Her tag on the overlay (spec 0.13 §1.1; ActTag): the thread (no shadow) with its beads, and the porcelain tag with the
/// tag shadow (light 0 1 2 .08 + 0 4 14 .10; dark 0 1 2 .5 + 0 6 18 .42). Drawn once per change into fixed-size
/// surfaces; moved per frame to whole pixels. It enters with opacity and a 4 px drop (0.22 s ease-out) and leaves in
/// 0.15 s; with reduced motion it just appears and goes.
/// </summary>
internal sealed class TagVisual : IDisposable
{
    private readonly CompositionHost _host;
    private readonly Compositor _c;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _thread, _tag, _shadowNear, _shadowFar;
    private SwapSurface? _tagSurface, _threadSurface;
    private string? _tagKey, _threadKey;
    private float _dpi;
    private int _generation;
    private RectF _threadBox;

    public TagVisual(CompositionHost host)
    {
        _host = host;
        _c = host.Compositor;
        _root = _c.CreateContainerVisual();
        _root.IsVisible = false;
        _thread = _c.CreateSpriteVisual();
        _shadowFar = _c.CreateSpriteVisual();
        _shadowNear = _c.CreateSpriteVisual();
        _tag = _c.CreateSpriteVisual();
        foreach (var v in new[] { _shadowFar, _shadowNear, _tag, _thread }) _root.Children.InsertAtTop(v);   // the thread ties on over the tag (ActTag: z-index 1)
        host.Root.Children.InsertAtTop(_root);
    }

    public bool Shown { get; private set; }
    public float Width { get; private set; }
    /// <summary>Drawings so far (test diagnostics: a move draws nothing).</summary>
    public int Drawn { get; private set; }

    /// <summary>Draws (when the content changed) and shows the tag at <paramref name="g"/>.</summary>
    public void Show(Activity a, int beads, bool knot, TagGeometry g, ThemeTokens t, bool dark, Pixels? cover, Rgba? coverColour, float dpi)
    {
        Ensure(dpi);
        float w = ActivityPainter.TagWidth(a);
        Width = w;
        var key = string.Join('|', a.Kind, a.Text, a.Meta, a.Progress?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), a.Asking, a.Icon, w,
            dark, cover is null ? "" : cover.Bgra.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + cover.Bgra[cover.Bgra.Length / 2], coverColour);
        if (key != _tagKey)
        {
            var scale = Matrix3x2.CreateScale(dpi);
            int i = _tagSurface!.Draw(dc => { dc.Clear(); dc.SetTransform(scale); ActivityPainter.Tag(dc, a, w, t, cover, coverColour, dark); });
            foreach (var v in new[] { _tag, _shadowNear, _shadowFar }) v.Brush = Brush(_tagSurface.Brushes[i]);
            _tagKey = key;
            Drawn++;
        }
        var size = new Vector2(MathF.Ceiling(w * dpi), MathF.Ceiling(TagPlace.HeightDip * dpi));
        foreach (var v in new[] { _tag, _shadowNear, _shadowFar }) v.Size = size;
        Shadow(_shadowNear, 2, 1, dark ? 0.5f : 0.08f, dpi);
        Shadow(_shadowFar, dark ? 18 : 14, dark ? 6 : 4, dark ? 0.42f : 0.10f, dpi);
        DrawThread(g, beads, knot, t, dpi);
        Move(g);
        if (!Shown) Enter();
    }

    /// <summary>Follows her: positions only, whole pixels.</summary>
    public void Move(TagGeometry g)
    {
        var at = new Vector3(MathF.Round(g.Tag.X), MathF.Round(g.Tag.Y), 0);
        foreach (var v in new[] { _tag, _shadowNear, _shadowFar }) v.Offset = at;
        var rel = g.To - g.From;
        var box = ThreadBox(g.From, rel, _dpi);
        _thread.Offset = new Vector3(MathF.Round(box.X), MathF.Round(box.Y), 0);
    }

    public void Hide()
    {
        if (!Shown) return;
        Shown = false;
        int gen = ++_generation;
        if (ExpressionPlayer.Reduced) { _root.IsVisible = false; return; }
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var fade = _c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = TimeSpan.FromMilliseconds(150);
        _root.StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) => { if (gen == _generation && !Shown) _root.IsVisible = false; };
    }

    private void Enter()
    {
        Shown = true;
        _generation++;
        _root.IsVisible = true;
        if (ExpressionPlayer.Reduced)
        {
            _root.StopAnimation("Opacity");
            _root.StopAnimation("Offset");
            _root.Opacity = 1;
            _root.Offset = Vector3.Zero;
            return;
        }
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));
        var fade = _c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1, ease);
        fade.Duration = TimeSpan.FromMilliseconds(220);
        var drop = _c.CreateVector3KeyFrameAnimation();
        drop.InsertKeyFrame(0, new Vector3(0, -MathF.Round(4 * _dpi), 0));
        drop.InsertKeyFrame(1, Vector3.Zero, ease);
        drop.Duration = TimeSpan.FromMilliseconds(220);
        _root.StartAnimation("Opacity", fade);
        _root.StartAnimation("Offset", drop);
    }

    private static RectF ThreadBox(Vector2 from, Vector2 rel, float dpi)
    {
        float m = 6 * dpi;
        float x0 = MathF.Min(0, rel.X) - m, y0 = MathF.Min(0, rel.Y) - m;
        return new RectF(from.X + x0, from.Y + y0, MathF.Abs(rel.X) + 2 * m, MathF.Abs(rel.Y) + 2 * m);
    }

    private void DrawThread(TagGeometry g, int beads, bool knot, ThemeTokens t, float dpi)
    {
        var rel = g.To - g.From;
        var box = ThreadBox(g.From, rel, dpi);
        var relDip = new Vector2(MathF.Round(rel.X), MathF.Round(rel.Y)) / dpi;
        var key = FormattableString.Invariant($"{relDip.X:0.#},{relDip.Y:0.#}|{beads}|{knot}|{t.Boundary}|{t.Menu}|{dpi}");
        _threadBox = box;
        var need = new SizeI((int)MathF.Ceiling(box.Width) + 2, (int)MathF.Ceiling(box.Height) + 2);
        if (_threadSurface is null) _threadSurface = new SwapSurface(_host, Max(need, new SizeI(64, 64)));
        else if (need.Width > _threadSurface.Size.Width || need.Height > _threadSurface.Size.Height)
        {
            _threadSurface.Resize(Max(need, _threadSurface.Size));
            _threadKey = null;
        }
        if (key != _threadKey)
        {
            var origin = (g.From - new Vector2(box.X, box.Y)) / dpi;
            var scale = Matrix3x2.CreateScale(dpi);
            int i = _threadSurface.Draw(dc => { dc.Clear(); dc.SetTransform(scale); ActivityPainter.Thread(dc, origin, origin + relDip, beads, knot, t); });
            _thread.Brush = Brush(_threadSurface.Brushes[i]);
            _threadKey = key;
        }
        _thread.Size = new Vector2(_threadSurface.Size.Width, _threadSurface.Size.Height);
    }

    private static SizeI Max(SizeI a, SizeI b) => new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));

    private static CompositionSurfaceBrush Brush(CompositionSurfaceBrush b)
    {
        b.Stretch = CompositionStretch.None;
        b.HorizontalAlignmentRatio = 0;
        b.VerticalAlignmentRatio = 0;
        return b;
    }

    private void Shadow(SpriteVisual v, float blur, float y, float alpha, float dpi)
    {
        var s = v.Shadow as DropShadow ?? _c.CreateDropShadow();
        s.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        s.BlurRadius = blur * dpi;
        s.Offset = new Vector3(0, y * dpi, 0);
        s.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        s.Opacity = alpha;
        v.Shadow = s;
    }

    private void Ensure(float dpi)
    {
        if (_tagSurface is not null && dpi == _dpi) return;
        _dpi = dpi;
        var size = new SizeI((int)MathF.Ceiling(TagPlace.MaxWidthDip * dpi) + 1, (int)MathF.Ceiling(TagPlace.HeightDip * dpi) + 1);
        if (_tagSurface is null) _tagSurface = new SwapSurface(_host, size);
        else _tagSurface.Resize(size);
        _tagKey = _threadKey = null;
    }

    /// <summary>After a device loss: drawn again next time.</summary>
    public void Redraw()
    {
        _tagKey = _threadKey = null;
        _tagSurface?.Redraw();
        _threadSurface?.Redraw();
    }

    /// <summary>"x,y,w,h" of the tag (px) and the thread's box, for test diagnostics.</summary>
    public string Describe(TagGeometry g) => FormattableString.Invariant(
        $"tag={g.Tag.X:0},{g.Tag.Y:0},{g.Tag.Width:0},{g.Tag.Height:0} thread={_threadBox.X:0},{_threadBox.Y:0},{_threadBox.Width:0},{_threadBox.Height:0}");

    public void Dispose()
    {
        _tagSurface?.Dispose();
        _threadSurface?.Dispose();
        _root.Dispose();
    }
}
