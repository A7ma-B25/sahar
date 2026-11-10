using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Windows.UI.Composition;

namespace Sahar.Engine.Menu;

/// <summary>
/// The card on the overlay (spec §5, §7): two drop shadows (elevation e1), the background layer, the hover / press /
/// focus shapes, the foreground layer (text and icons) and the moon clasp. Opening 180 ms opacity + scale 0.96 → 1 from
/// the corner nearest her; closing 120 ms; a drill cross-fades 160 ms. Reduced motion: opening opacity only (100 ms),
/// everything else instant. Surfaces are drawn once per change, at a fixed size per DPI, and reused.
/// </summary>
internal sealed class CardVisual : IDisposable
{
    public const float MaxHeightDip = 540;

    private readonly CompositionHost _host;
    private readonly Compositor _c;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _shadowNear, _shadowFar;
    private SwapSurface? _bg, _fg;
    private FadeLayer? _bgLayer, _fgLayer;
    private readonly ContainerVisual _bgHost, _fgHost;
    private readonly ShapeVisual _hover, _focus, _clasp;
    private readonly CompositionRoundedRectangleGeometry _hoverGeo, _focusGeo;
    private readonly CompositionSpriteShape _hoverShape, _focusShape;
    private readonly CompositionColorBrush _hoverBrush, _focusBrush;
    private readonly CompositionEllipseGeometry _claspGeo;
    private float _dpi;
    private int _generation;
    // Animations are built once and reused (each open used to make new ones: the menu stage saw memory creep).
    private readonly Dictionary<string, CompositionAnimation> _anims = [];

    public CardVisual(CompositionHost host)
    {
        _host = host;
        _c = host.Compositor;
        _root = _c.CreateContainerVisual();
        _root.IsVisible = false;
        host.Root.Children.InsertAtTop(_root);
        _shadowFar = _c.CreateSpriteVisual();
        _shadowNear = _c.CreateSpriteVisual();
        _root.Children.InsertAtTop(_shadowFar);
        _root.Children.InsertAtTop(_shadowNear);
        _bgHost = _c.CreateContainerVisual();
        _root.Children.InsertAtTop(_bgHost);

        _hoverGeo = _c.CreateRoundedRectangleGeometry();
        _hoverBrush = _c.CreateColorBrush();
        _hoverShape = _c.CreateSpriteShape(_hoverGeo);
        _hoverShape.FillBrush = _hoverBrush;
        _hover = _c.CreateShapeVisual();
        _hover.Shapes.Add(_hoverShape);
        _hover.Opacity = 0;
        _root.Children.InsertAtTop(_hover);

        _fgHost = _c.CreateContainerVisual();
        _root.Children.InsertAtTop(_fgHost);

        _focusGeo = _c.CreateRoundedRectangleGeometry();
        _focusBrush = _c.CreateColorBrush();
        _focusShape = _c.CreateSpriteShape(_focusGeo);
        _focusShape.StrokeBrush = _focusBrush;
        _focus = _c.CreateShapeVisual();
        _focus.Shapes.Add(_focusShape);
        _focus.IsVisible = false;
        _root.Children.InsertAtTop(_focus);

        _claspGeo = _c.CreateEllipseGeometry();
        var claspShape = _c.CreateSpriteShape(_claspGeo);
        claspShape.FillBrush = _c.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0xF2, 0xC8, 0x6A));
        _clasp = _c.CreateShapeVisual();
        _clasp.Shapes.Add(claspShape);
        _root.Children.InsertAtTop(_clasp);
    }

    public bool Shown { get; private set; }
    /// <summary>The open animation finished (or the card showed at once).</summary>
    public event Action? Opened;
    /// <summary>The card's place on screen (px) while shown.</summary>
    public RectF Card { get; private set; }

    /// <summary>Shows a view: drawn into the spare surfaces, or — when one of the two already holds exactly this
    /// content (<paramref name="key"/>) — just shown again (opening the menu, main ↔ a drill, costs no drawing);
    /// <paramref name="fade"/> cross-fades from the one showing (a drill).</summary>
    public void Draw(CardContent content, float dpi, bool fade, string key)
    {
        Ensure(dpi);
        var size = new SizeI((int)MathF.Ceiling(MenuCardLayout.Width * dpi), (int)MathF.Ceiling(MaxHeightDip * dpi));
        var fadeTime = fade && !ExpressionPlayer.Reduced ? TimeSpan.FromMilliseconds(160) : TimeSpan.Zero;
        int index;
        if (_keys[_bg!.Current] == key) index = _bg.Current;
        else if (_keys[1 - _bg.Current] == key) index = 1 - _bg.Current;
        else
        {
            var scale = Matrix3x2.CreateScale(dpi);
            index = _bg.Draw(dc => { dc.Clear(); dc.SetTransform(scale); CardPainter.Background(dc, content); });
            _fg!.DrawAt(index, dc => { dc.Clear(); dc.SetTransform(scale); CardPainter.Foreground(dc, content); });
            _keys[index] = key;
            Drawn++;
        }
        _bg.DrawnIndex(index);
        _bgLayer!.Show(index, fadeTime);
        _fgLayer!.Show(index, fadeTime);
        foreach (var s in new[] { _shadowNear, _shadowFar }) { s.Brush = _bg.Brushes[index]; s.Size = new Vector2(size.Width, size.Height); }
        Theme(content.T, content.Layout, dpi);
    }

    private readonly string?[] _keys = new string?[2];
    /// <summary>Card drawings so far (test diagnostics: opening again draws nothing new).</summary>
    public int Drawn { get; private set; }

    /// <summary>Shows the card at <paramref name="card"/> (px): opens from <paramref name="near"/>; the clasp rests at <paramref name="moon"/>.</summary>
    public void Open(RectF card, Corner near, Vector2 moon)
    {
        Card = card;
        Shown = true;
        _generation++;
        _root.Offset = new Vector3(card.X, card.Y, 0);
        _root.CenterPoint = new Vector3(near == Corner.TopLeft ? 0 : card.Width, 0, 0);
        float r = MenuCardLayout.MoonDip / 2 * _dpi;
        _clasp.Size = new Vector2(2 * r);
        _claspGeo.Radius = new Vector2(r);
        _claspGeo.Center = new Vector2(r);
        _clasp.Offset = new Vector3(moon.X - card.X - r, moon.Y - card.Y - r, 0);
        _root.StopAnimation("Opacity");
        _root.StopAnimation("Scale");
        _root.IsVisible = true;
        int gen = _generation;
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        batch.Completed += (_, _) => { if (_generation == gen && Shown) Opened?.Invoke(); };
        if (ExpressionPlayer.Reduced)
        {
            _root.Scale = Vector3.One;
            _root.StartAnimation("Opacity", Anim("open-reduced", () => Scalar(0, 1, 100, null)));
            batch.End();
            return;
        }
        _root.StartAnimation("Opacity", Anim("open-opacity", () => Scalar(0, 1, 180, OpenEase)));
        _root.StartAnimation("Scale", Anim("open-scale", () =>
        {
            var scale = _c.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(0, new Vector3(0.96f, 0.96f, 1));
            scale.InsertKeyFrame(1, Vector3.One, OpenEase);
            scale.Duration = TimeSpan.FromMilliseconds(180);
            return scale;
        }));
        batch.End();
    }

    private CompositionEasingFunction? _openEase, _closeEase;
    private CompositionEasingFunction OpenEase => _openEase ??= _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));
    private CompositionEasingFunction CloseEase => _closeEase ??= _c.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(1f, 1f));

    private CompositionAnimation Anim(string key, Func<CompositionAnimation> make)
    {
        if (!_anims.TryGetValue(key, out var a)) _anims[key] = a = make();
        return a;
    }

    /// <param name="closed">Runs once the card has faded (at once under reduced motion, or when it never showed).</param>
    public void Close(Action? closed = null)
    {
        if (!Shown) { _generation++; closed?.Invoke(); return; }
        Shown = false;
        SetHover(null, false, null);
        SetFocus(null, 0);
        int gen = ++_generation;
        if (ExpressionPlayer.Reduced) { _root.IsVisible = false; closed?.Invoke(); return; }
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        _root.StartAnimation("Opacity", Anim("close-opacity", () => Scalar(1, 0, 120, CloseEase)));
        _root.StartAnimation("Scale", Anim("close-scale", () =>
        {
            var scale = _c.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, new Vector3(0.98f, 0.98f, 1), CloseEase);
            scale.Duration = TimeSpan.FromMilliseconds(120);
            return scale;
        }));
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (_generation != gen) return;
            _root.IsVisible = false;
            closed?.Invoke();
        };
    }

    /// <summary>The hover (or press) highlight on a card-relative rect (DIP), or none.</summary>
    public void SetHover(RectF? rect, bool pressed, float? radius)
    {
        if (rect is not { } r) { Fade(_hover, 0, 120); return; }
        Place(_hover, _hoverGeo, r, radius ?? 10, 0);
        _hoverBrush.Color = Color(pressed ? _pressedColor : _hoverColor);
        _hover.CenterPoint = new Vector3(_hover.Size / 2, 0);
        _hover.Scale = pressed && !ExpressionPlayer.Reduced ? new Vector3(0.97f, 0.97f, 1) : Vector3.One;
        Fade(_hover, 1, ExpressionPlayer.Reduced ? 0 : 120);
    }

    /// <summary>The keyboard focus ring (2 DIP accent, 2 DIP outside the element) or none.</summary>
    public void SetFocus(RectF? rect, float radius)
    {
        if (rect is not { } r) { _focus.IsVisible = false; return; }
        Place(_focus, _focusGeo, r, radius + 2, 2);
        _focusShape.StrokeThickness = 2 * _dpi;
        _focus.IsVisible = true;
    }

    private Rgba _hoverColor, _pressedColor;

    private void Theme(ThemeTokens t, CardLayout l, float dpi)
    {
        _hoverColor = t.Hover;
        _pressedColor = t.Pressed;
        _focusBrush.Color = Color(t.Accent);
        // Elevation e1: light 0 1px 2px .08 + 0 8px 24px .12; dark 0 1px 2px .45 + 0 12px 32px .5.
        bool dark = t == Tokens.Dark;
        Shadow(_shadowNear, 2, 1, dark ? 0.45f : 0.08f, dpi);
        Shadow(_shadowFar, dark ? 32 : 24, dark ? 12 : 8, dark ? 0.5f : 0.12f, dpi);
        _ = l;
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

    private void Place(ShapeVisual v, CompositionRoundedRectangleGeometry g, RectF r, float radius, float outset)
    {
        float k = _dpi;
        v.Offset = new Vector3((r.X - outset) * k, (r.Y - outset) * k, 0);
        v.Size = new Vector2((r.Width + 2 * outset) * k, (r.Height + 2 * outset) * k);
        g.Size = v.Size - (outset > 0 ? new Vector2(2 * k) : Vector2.Zero);
        g.Offset = outset > 0 ? new Vector2(k) : Vector2.Zero;
        g.CornerRadius = new Vector2(radius * k);
    }

    private void Fade(Visual v, float to, double ms)
    {
        if (ms <= 0) { v.StopAnimation("Opacity"); v.Opacity = to; return; }
        v.StartAnimation("Opacity", Anim($"fade-{to}-{ms}", () => Scalar(null, to, ms, null)));
    }

    private ScalarKeyFrameAnimation Scalar(float? from, float to, double ms, CompositionEasingFunction? ease)
    {
        var a = _c.CreateScalarKeyFrameAnimation();
        if (from is { } f) a.InsertKeyFrame(0, f);
        if (ease is null) a.InsertKeyFrame(1, to); else a.InsertKeyFrame(1, to, ease);
        a.Duration = TimeSpan.FromMilliseconds(ms);
        return a;
    }

    private static Windows.UI.Color Color(Rgba c) => Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B);

    private void Ensure(float dpi)
    {
        if (_bg is not null && dpi == _dpi) return;
        _dpi = dpi;
        _keys[0] = _keys[1] = null;
        var size = new SizeI((int)MathF.Ceiling(MenuCardLayout.Width * dpi), (int)MathF.Ceiling(MaxHeightDip * dpi));
        if (_bg is null)
        {
            _bg = new SwapSurface(_host, size);
            _fg = new SwapSurface(_host, size);
            _bgLayer = new FadeLayer(_c, _bg);
            _fgLayer = new FadeLayer(_c, _fg);
            _bgHost.Children.InsertAtTop(_bgLayer.Container);
            _fgHost.Children.InsertAtTop(_fgLayer.Container);
        }
        else
        {
            _bg.Resize(size);
            _fg!.Resize(size);
            _bgLayer!.SetSize(size);
            _fgLayer!.SetSize(size);
        }
    }

    /// <summary>After a device loss: surfaces are redrawn from what they last drew.</summary>
    public void Redraw()
    {
        _keys[0] = _keys[1] = null;
        _bg?.Redraw();
        _fg?.Redraw();
    }

    public void Dispose()
    {
        _bg?.Dispose();
        _fg?.Dispose();
        _root.Dispose();
    }
}
