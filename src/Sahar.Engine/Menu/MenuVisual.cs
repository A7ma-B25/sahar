using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Windows.UI.Composition;

namespace Sahar.Engine.Menu;

/// <summary>
/// The menu's buttons on the overlay (board "القائمة الدائرية"): white discs with an emoji, a soft lavender
/// glow and a label pill; they pop in one after another with a spring (all on the compositor).
/// </summary>
internal sealed class MenuVisual : IDisposable
{
    public const float LabelWidthDip = 116f, PadDip = 6f, LabelHeightDip = 22f;

    private static readonly Rgba White = Rgba.Parse("#FFFFFF"), Ring = Rgba.Parse("#E7E3F3"), Glow = Rgba.Parse("#B9B0FF"),
        Ink = Rgba.Parse("#1E2350"), Hover = Rgba.Parse("#FFD98A");

    private readonly CompositionHost _host;
    private readonly ContainerVisual _root;
    private readonly List<SpriteVisual> _buttons = [];
    // Each button is drawn once per look and DPI, then reused (the menu opens often).
    private readonly Dictionary<(string Id, string Label, float Dpi), SwapSurface> _drawn = [];
    private readonly ShapeVisual _hover;
    private readonly CompositionEllipseGeometry _hoverGeometry;
    private readonly CompositionSpriteShape _hoverShape;

    public MenuVisual(CompositionHost host)
    {
        _host = host;
        var c = host.Compositor;
        _root = c.CreateContainerVisual();
        host.Root.Children.InsertAtTop(_root);
        _hoverGeometry = c.CreateEllipseGeometry();
        _hoverShape = c.CreateSpriteShape(_hoverGeometry);
        _hoverShape.StrokeBrush = c.CreateColorBrush(Windows.UI.Color.FromArgb(255, Hover.R, Hover.G, Hover.B));
        _hover = c.CreateShapeVisual();
        _hover.Shapes.Add(_hoverShape);
        _hover.IsVisible = false;
    }

    /// <summary>Shows these buttons (replacing any shown), popping in one after another.</summary>
    public void Show(IReadOnlyList<MenuItem> items, IReadOnlyList<ButtonSlot> slots, float dpi, Vector2 origin)
    {
        Clear();
        var c = _host.Compositor;
        float w = LabelWidthDip * dpi, r = slots.Count > 0 ? slots[0].Radius : RadialLayout.ButtonRadiusDip * dpi;
        float pad = PadDip * dpi, h = pad + r + (RadialLayout.LabelTopDip + RadialLayout.LabelHeightDip + 2) * dpi;
        var size = new SizeI((int)MathF.Ceiling(w), (int)MathF.Ceiling(h));
        for (int i = 0; i < slots.Count; i++)
        {
            var item = items[i];
            if (!_drawn.TryGetValue((item.Id, item.Label, dpi), out var surface))
            {
                surface = new SwapSurface(_host, size);
                surface.DrawCurrent(dc => Draw(dc, item, dpi, w, r, pad));
                _drawn[(item.Id, item.Label, dpi)] = surface;
            }
            var v = c.CreateSpriteVisual();
            v.Size = new Vector2(size.Width, size.Height);
            v.Brush = surface.Brushes[surface.Current];
            var anchor = new Vector2(w / 2, pad + r);
            v.Offset = new Vector3(slots[i].Centre - origin - anchor, 0);
            v.CenterPoint = new Vector3(anchor, 0);
            v.Scale = Vector3.Zero;
            var pop = c.CreateVector3KeyFrameAnimation();
            pop.InsertKeyFrame(0f, Vector3.Zero);
            pop.InsertKeyFrame(0.6f, new Vector3(1.15f, 1.15f, 1));
            pop.InsertKeyFrame(1f, Vector3.One);
            pop.Duration = TimeSpan.FromMilliseconds(240);
            pop.DelayTime = TimeSpan.FromMilliseconds(60 * i);
            pop.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            _root.Children.InsertAtTop(v);
            v.StartAnimation("Scale", pop);
            _buttons.Add(v);
        }
        _hover.IsVisible = false;
        _hoverShape.StrokeThickness = 3 * dpi;
        _hoverGeometry.Radius = new Vector2(r + 4 * dpi);
        _hover.Size = new Vector2(2 * (r + 8 * dpi));
        _hoverGeometry.Center = new Vector2(r + 8 * dpi);
        _root.Children.InsertAtTop(_hover);
    }

    /// <summary>The gold ring around the button under the pointer (or none).</summary>
    public void SetHover(ButtonSlot? slot, Vector2 origin)
    {
        if (slot is null) { _hover.IsVisible = false; return; }
        var half = _hover.Size / 2;
        _hover.Offset = new Vector3(slot.Centre - origin - half, 0);
        _hover.IsVisible = true;
    }

    public void Clear()
    {
        foreach (var v in _buttons)
        {
            _root.Children.Remove(v);
            v.Dispose();
        }
        _buttons.Clear();
        if (_hover.Parent != null) _root.Children.Remove(_hover);
    }

    private static void Draw(D2DContext dc, MenuItem item, float dpi, float w, float r, float pad)
    {
        dc.Clear();
        dc.SetTransform(System.Numerics.Matrix3x2.Identity);
        var centre = new Vector2(w / 2, pad + r);
        var glow = dc.RadialBrush(
            [new() { Position = 0.7f, Color = new D2DColor(Glow, 0.35f) }, new() { Position = 1, Color = new D2DColor(Glow, 0) }],
            centre, new Vector2(r + pad));
        dc.FillEllipse(centre, new Vector2(r + pad), glow);
        dc.FillEllipse(centre, new Vector2(r), dc.SolidBrush(new D2DColor(White)));
        dc.DrawEllipse(centre, new Vector2(r), dc.SolidBrush(new D2DColor(Ring)), 1.5f * dpi, dc.StrokeStyle("butt"));
        dc.Text(item.Emoji, "Segoe UI Emoji", r * 0.9f, 400,
            new D2DRect { Left = centre.X - r, Top = centre.Y - r, Right = centre.X + r, Bottom = centre.Y + r }, dc.SolidBrush(new D2DColor(Ink)));

        // Label pill under the disc.
        // Same size and place as RadialLayout's label rectangle (layout and hit-testing use it).
        float textW = RadialLayout.LabelWidthDip(item.Label) * dpi, pillH = RadialLayout.LabelHeightDip * dpi;
        float left = (w - textW) / 2, top = pad + r + RadialLayout.LabelTopDip * dpi, rad = pillH / 2;
        var pill = dc.Path(PathData.Parse(FormattableString.Invariant(
            $"M{left + rad} {top} L{left + textW - rad} {top} A{rad} {rad} 0 0 1 {left + textW - rad} {top + pillH} L{left + rad} {top + pillH} A{rad} {rad} 0 0 1 {left + rad} {top} Z")));
        dc.FillGeometry(pill, dc.SolidBrush(new D2DColor(White, 0.92f)));
        dc.Text(item.Label, "Segoe UI", 12 * dpi, 600,
            new D2DRect { Left = left, Top = top, Right = left + textW, Bottom = top + pillH }, dc.SolidBrush(new D2DColor(Ink)));
    }

    /// <summary>After a device loss: drawn buttons are gone, draw them again next time.</summary>
    public void Forget()
    {
        Clear();
        foreach (var s in _drawn.Values) s.Dispose();
        _drawn.Clear();
    }

    public void Dispose()
    {
        Forget();
        _hover.Dispose();
        _root.Dispose();
    }
}
