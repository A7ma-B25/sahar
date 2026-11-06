using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Engine.Host;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Sahar's visual tree on the overlay. Back to front: halo, shadow, orbit, moon (back half), stars, then her
/// character (leaf, body A, body B, pleats/highlight/blush, face, headphones), moon (front half), orb, extras.
/// The character bobs; moon, stars and shadow stay put, as in the design.
/// </summary>
internal sealed class SaharVisual
{
    /// <summary>Her body inside the 300-unit canvas (rig body bounds plus the rig offset).</summary>
    public static readonly RectF BodyUnits = new(70, 88, 160, 154);

    private static readonly (float X, float Y, float Scale)[] StarSpots =
        [(150, 22, 0.9f), (272, 120, 0.6f), (46, 70, 0.7f), (240, 236, 0.5f), (36, 214, 0.55f)];

    private readonly CompositionHost _host;
    private readonly Compositor _c;
    private readonly Rig _rig;
    private readonly List<(SwapSurface Surface, FadeLayer[] Layers)> _swaps = [];
    private readonly SwapSurface _halo, _shadow, _orbit, _moon, _star, _bodyA, _bodyB, _decor, _face, _leaf, _budGlow, _headphones;

    private Vector2 _position;
    private Vector2 _origin;
    private float _dpiScale;
    private Mood _mood = Mood.Calm;
    private Eyes _eyes = Eyes.Open;
    private Mouth _mouth = Mouth.Smile;

    public ContainerVisual Root { get; }
    public ContainerVisual Character { get; }
    public ContainerVisual Stars { get; }
    public ContainerVisual Leaf { get; }
    public ContainerVisual Orb { get; }
    public ContainerVisual Extras { get; }
    public FadeLayer HaloLayer { get; }
    public SpriteVisual ShadowVisual { get; }
    public FadeLayer MoonBack { get; }
    public FadeLayer MoonFront { get; }
    public FadeLayer BodyB { get; }
    public FadeLayer Face { get; }
    public FadeLayer BudGlow { get; }
    public FadeLayer Headphones { get; }
    public SpriteVisual[] StarVisuals { get; } = new SpriteVisual[StarSpots.Length];
    /// <summary>Drives the moon: "t" (orbit phase 0–1) and "moon" (1 shown, 0 hidden while the orb is out).</summary>
    public CompositionPropertySet MoonProps { get; }
    public CompositionHost Host => _host;

    /// <summary>Body width in DIPs (Small 48, Medium 80, Large 120).</summary>
    public float BodyDip { get; private set; }
    /// <summary>Pixels per design unit at the current size and DPI.</summary>
    public float Scale { get; private set; }
    public SizeI CanvasPx { get; private set; }

    /// <summary>Raised after every size or DPI change, so animations that use pixel values restart.</summary>
    public event Action? Rescaled;

    public SaharVisual(CompositionHost host, Rig rig, float bodyDip, float dpiScale)
    {
        _host = host;
        _c = host.Compositor;
        _rig = rig;
        BodyDip = bodyDip;
        _dpiScale = dpiScale;
        CanvasPx = RigScale.RasterSize(bodyDip, dpiScale);
        Scale = CanvasPx.Width / RigScale.CanvasUnits;

        Root = _c.CreateContainerVisual();
        Root.Size = new Vector2(CanvasPx.Width, CanvasPx.Height);
        host.Root.Children.InsertAtTop(Root);

        _halo = Canvas(); _shadow = Canvas(); _orbit = Canvas();
        _moon = Small(PartRenderer.MoonUnits); _star = Small(PartRenderer.StarUnits);
        _bodyA = Canvas(); _bodyB = Canvas(); _decor = Canvas(); _face = Canvas();
        _leaf = Canvas(); _budGlow = Canvas(); _headphones = Canvas();

        HaloLayer = Layer(_halo, Root);
        ShadowVisual = _c.CreateSpriteVisual();
        ShadowVisual.Brush = _shadow.Brushes[0];
        Root.Children.InsertAtTop(ShadowVisual);
        Layer(_orbit, Root);
        MoonBack = Layer(_moon, Root);

        Stars = _c.CreateContainerVisual();
        Root.Children.InsertAtTop(Stars);
        for (int i = 0; i < StarVisuals.Length; i++)
        {
            StarVisuals[i] = _c.CreateSpriteVisual();
            StarVisuals[i].Brush = _star.Brushes[0];
            StarVisuals[i].AnchorPoint = new Vector2(0.5f);
            Stars.Children.InsertAtTop(StarVisuals[i]);
        }

        Character = _c.CreateContainerVisual();
        Root.Children.InsertAtTop(Character);
        Leaf = _c.CreateContainerVisual();
        Character.Children.InsertAtTop(Leaf);
        Layer(_leaf, Leaf);
        BudGlow = Layer(_budGlow, Leaf);
        Layer(_bodyA, Character);
        BodyB = Layer(_bodyB, Character);
        Layer(_decor, Character);
        Face = Layer(_face, Character);
        Headphones = Layer(_headphones, Character);
        Headphones.Container.IsVisible = false;

        // The front half of the moon shares the moon surfaces with the back half.
        MoonFront = new FadeLayer(_c, _moon);
        Root.Children.InsertAtTop(MoonFront.Container);
        AddLayer(_moon, MoonFront);

        Orb = _c.CreateContainerVisual();
        Root.Children.InsertAtTop(Orb);
        Extras = _c.CreateContainerVisual();
        Root.Children.InsertAtTop(Extras);

        foreach (var m in new[] { MoonBack, MoonFront }) m.Container.AnchorPoint = new Vector2(0.5f);
        MoonProps = _c.CreatePropertySet();
        MoonProps.InsertScalar("t", 0f);
        MoonProps.InsertScalar("moon", 1f);
        Layout();
        DrawStatic();
        DrawMood(TimeSpan.Zero);
        DrawFace(TimeSpan.Zero);
    }

    /// <summary>Top-left of the overlay window in screen pixels (the virtual screen origin).</summary>
    public Vector2 Origin
    {
        get => _origin;
        set { _origin = value; Root.Offset = new Vector3(_position - _origin, 0); }
    }

    /// <summary>Top-left of her 300-unit canvas, in screen pixels.</summary>
    public Vector2 Position
    {
        get => _position;
        set { _position = value; Root.Offset = new Vector3(_position - _origin, 0); }
    }

    /// <summary>Her body rect in screen pixels (for the input window and the pipe's state).</summary>
    public RectI BodyRect => new(
        (int)MathF.Round(_position.X + BodyUnits.X * Scale), (int)MathF.Round(_position.Y + BodyUnits.Y * Scale),
        (int)MathF.Round(_position.X + (BodyUnits.X + BodyUnits.Width) * Scale),
        (int)MathF.Round(_position.Y + (BodyUnits.Y + BodyUnits.Height) * Scale));

    /// <summary>What the mouse can grab: body plus leaf, in screen pixels.</summary>
    public RectI HitRect => new(
        (int)MathF.Round(_position.X + BodyUnits.X * Scale), (int)MathF.Round(_position.Y + 30 * Scale),
        (int)MathF.Round(_position.X + (BodyUnits.X + BodyUnits.Width) * Scale),
        (int)MathF.Round(_position.Y + (BodyUnits.Y + BodyUnits.Height) * Scale));

    public float DpiScale => _dpiScale;

    public void SetMood(Mood m, TimeSpan fade)
    {
        if (m == _mood) return;
        _mood = m;
        DrawMood(fade);
    }

    /// <summary>The eyes and mouth showing now (for test checks).</summary>
    public (Eyes Eyes, Mouth Mouth) FaceShown => (_eyes, _mouth);

    public void SetFace(Eyes e, Mouth m, TimeSpan fade)
    {
        if (e == _eyes && m == _mouth) return;
        (_eyes, _mouth) = (e, m);
        DrawFace(fade);
    }

    /// <summary>Changes size or DPI: resizes every surface, redraws, and restarts pixel-based animations.</summary>
    public void SetSize(float bodyDip, float dpiScale)
    {
        if (bodyDip == BodyDip && dpiScale == _dpiScale) return;
        BodyDip = bodyDip;
        _dpiScale = dpiScale;
        CanvasPx = RigScale.RasterSize(bodyDip, dpiScale);
        Scale = CanvasPx.Width / RigScale.CanvasUnits;
        Root.Size = new Vector2(CanvasPx.Width, CanvasPx.Height);
        foreach (var (surface, _) in _swaps)
            surface.Resize(surface == _moon ? SmallPx(PartRenderer.MoonUnits) : surface == _star ? SmallPx(PartRenderer.StarUnits) : CanvasPx);
        Layout();
        Rescaled?.Invoke();
    }

    /// <summary>Redraws everything (after the GPU device was lost or replaced).</summary>
    public void Redraw()
    {
        foreach (var (surface, _) in _swaps) surface.Redraw();
    }

    private void Layout()
    {
        var canvas = new Vector2(CanvasPx.Width, CanvasPx.Height);
        foreach (var (surface, layers) in _swaps)
            foreach (var l in layers) l.SetSize(surface.Size);
        ShadowVisual.Size = canvas;
        ShadowVisual.CenterPoint = new Vector3(150 * Scale, 268 * Scale, 0);
        Stars.Size = canvas;
        Stars.CenterPoint = new Vector3(150 * Scale, 140 * Scale, 0);
        var starPx = SmallPx(PartRenderer.StarUnits);
        for (int i = 0; i < StarVisuals.Length; i++)
        {
            var (x, y, k) = StarSpots[i];
            StarVisuals[i].Size = new Vector2(starPx.Width, starPx.Height);
            StarVisuals[i].Offset = new Vector3(x * Scale, y * Scale, 0);
            StarVisuals[i].Scale = new Vector3(k, k, 1);
            StarVisuals[i].CenterPoint = new Vector3(starPx.Width / 2f, starPx.Height / 2f, 0);
        }
        Character.Size = canvas;
        Character.CenterPoint = new Vector3(150 * Scale, (BodyUnits.Y + BodyUnits.Height) * Scale, 0);  // bottom-centre
        Orb.Size = canvas;
        Extras.Size = canvas;
        Leaf.Size = canvas;
        Leaf.CenterPoint = new Vector3((120 + _rig.OffsetX) * Scale, (60 + _rig.OffsetY) * Scale, 0);   // stem base
        Face.Container.CenterPoint = new Vector3(150 * Scale, (124 + _rig.OffsetY) * Scale, 0);         // eye line
    }

    private void DrawStatic()
    {
        _shadow.DrawCurrent(dc => PartRenderer.Shadow(dc, Scale));
        _star.DrawCurrent(dc => PartRenderer.StarShape(dc, Scale));
    }

    private void DrawMood(TimeSpan fade)
    {
        var mood = _mood;
        var colors = Palette.Get(mood);
        Swap(_halo, fade, dc => PartRenderer.Halo(dc, Scale, colors));
        Swap(_orbit, fade, dc => PartRenderer.Orbit(dc, Scale, colors));
        Swap(_moon, fade, dc => PartRenderer.Moon(dc, Scale, mood, colors));
        Swap(_bodyA, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors, _rig.Parts["body"]));
        Swap(_bodyB, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors, _rig.Parts["bodyHem2"]));
        Swap(_decor, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors,
            [.. _rig.Parts["pleats"], .. _rig.Parts["highlight"], .. _rig.Parts["blushL"], .. _rig.Parts["blushR"]]));
        Swap(_leaf, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors,
            [.. _rig.Parts["leafStem"], .. _rig.Parts["leafA"], .. _rig.Parts["leafVein"], .. _rig.Parts["leafB"], .. _rig.Parts["bud"]]));
        Swap(_budGlow, fade, dc => PartRenderer.BudGlow(dc, Scale, _rig, colors));
        Swap(_headphones, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors, _rig.Parts["headphones"]));
        // The face does not change colour with the mood; it is redrawn only for new expressions.
    }

    private void DrawFace(TimeSpan fade)
    {
        var colors = Palette.Get(_mood);
        var shapes = _rig.EyesFor(_eyes).Concat(_rig.MouthFor(_mouth)).ToArray();
        Swap(_face, fade, dc => PartRenderer.Shapes(dc, Scale, _rig, colors, shapes));
    }

    private void Swap(SwapSurface surface, TimeSpan fade, Action<D2DContext> draw)
    {
        int index = surface.Draw(draw);
        foreach (var l in _swaps.Single(s => s.Surface == surface).Layers) l.Show(index, fade);
    }

    private SwapSurface Canvas() => Track(new SwapSurface(_host, CanvasPx));

    private SwapSurface Small(float units) => Track(new SwapSurface(_host, SmallPx(units)));

    public SizeI SmallPx(float units)
    {
        int px = (int)MathF.Ceiling(units * Scale);
        return new SizeI(px, px);
    }

    private SwapSurface Track(SwapSurface s)
    {
        _swaps.Add((s, []));
        return s;
    }

    private FadeLayer Layer(SwapSurface surface, ContainerVisual parent)
    {
        var layer = new FadeLayer(_c, surface);
        parent.Children.InsertAtTop(layer.Container);
        AddLayer(surface, layer);
        return layer;
    }

    private void AddLayer(SwapSurface surface, FadeLayer layer)
    {
        int i = _swaps.FindIndex(s => s.Surface == surface);
        _swaps[i] = (surface, [.. _swaps[i].Layers, layer]);
    }
}
