using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Geometry;
using Sahar.Engine.Senses;
using Windows.System;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>Her speech bubble: a white rounded pill above her with one or two lines; it follows her and fades after 8 s.</summary>
internal sealed class SpeechBubble : IDisposable
{
    private static readonly Rgba Ink = Rgba.Parse("#1E2350"), White = Rgba.Parse("#FFFFFF"), Edge = Rgba.Parse("#E7E3F3");

    private readonly SaharVisual _v;
    private readonly Func<RectF> _body;
    private readonly DispatcherQueueTimer _hide;
    private SpriteVisual? _visual;
    private SwapSurface? _surface;

    public string? Text { get; private set; }
    public bool Showing => Text != null;
    /// <summary>It went away by itself (true) or was clicked away (false).</summary>
    public event Action<bool>? Ended;

    public SpeechBubble(DispatcherQueue ui, SaharVisual v, Func<RectF> body)
    {
        _v = v;
        _body = body;
        _hide = ui.CreateTimer();
        _hide.IsRepeating = false;
        _hide.Interval = TimeSpan.FromSeconds(8);
        _hide.Tick += (_, _) => End(timedOut: true);
    }

    public void Say(string text)
    {
        Remove();
        Text = text;
        var c = _v.Root.Compositor;
        float dpi = _v.DpiScale, w = 260 * dpi, h = 52 * dpi;
        _surface = new SwapSurface(_v.Host, new SizeI((int)MathF.Ceiling(w), (int)MathF.Ceiling(h)));
        _surface.DrawCurrent(dc =>
        {
            dc.Clear();
            dc.SetTransform(Matrix3x2.Identity);
            float r = 16 * dpi, bw = w - 2, bh = h - 10 * dpi;
            var shape = dc.Path(PathData.Parse(FormattableString.Invariant(
                $"M{1 + r} 1 L{bw - r} 1 A{r} {r} 0 0 1 {bw} {1 + r} L{bw} {bh - r} A{r} {r} 0 0 1 {bw - r} {bh} L{w / 2 + 8 * dpi} {bh} L{w / 2} {h - 1} L{w / 2 - 8 * dpi} {bh} L{1 + r} {bh} A{r} {r} 0 0 1 1 {bh - r} L1 {1 + r} A{r} {r} 0 0 1 {1 + r} 1 Z")));
            dc.FillGeometry(shape, dc.SolidBrush(new D2DColor(White, 0.96f)));
            dc.DrawGeometry(shape, dc.SolidBrush(new D2DColor(Edge)), 1.5f * dpi, dc.StrokeStyle("round"));
            dc.Text(text, "Segoe UI", 13 * dpi, 600, new D2DRect { Left = 12 * dpi, Top = 2, Right = w - 12 * dpi, Bottom = bh }, dc.SolidBrush(new D2DColor(Ink)), rightToLeft: true);
        });
        _visual = c.CreateSpriteVisual();
        _visual.Size = new Vector2(_surface.Size.Width, _surface.Size.Height);
        _visual.Brush = _surface.Brushes[_surface.Current];
        _v.Host.Root.Children.InsertAtTop(_visual);
        // Above her head, kept inside her screen's work area; it follows her.
        var units = SaharVisual.BodyUnits;
        var b = _body();
        var work = Monitors.At(new Vector2(b.X + b.Width / 2, b.Y + b.Height / 2)).WorkF;
        var offset = new Vector2((units.X + units.Width / 2) * _v.Scale - w / 2, units.Y * _v.Scale - h - 4 * dpi);
        float minX = work.X - _v.Origin.X, maxX = work.Right - w - _v.Origin.X, minY = work.Y - _v.Origin.Y;
        var follow = c.CreateExpressionAnimation("Vector3(Clamp(r.Offset.X + o.X, mnx, mxx), Max(r.Offset.Y + o.Y, mny), 0)");
        follow.SetReferenceParameter("r", _v.Root);
        follow.SetVector3Parameter("o", new Vector3(offset, 0));
        follow.SetScalarParameter("mnx", minX);
        follow.SetScalarParameter("mxx", MathF.Max(minX, maxX));
        follow.SetScalarParameter("mny", minY);
        _visual.StartAnimation("Offset", follow);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(250);
        _visual.StartAnimation("Opacity", fade);
        _hide.Start();
    }

    /// <summary>A click on her while it shows: away it goes.</summary>
    public void Dismiss()
    {
        if (Showing) End(timedOut: false);
    }

    private void End(bool timedOut)
    {
        _hide.Stop();
        Remove();
        Ended?.Invoke(timedOut);
    }

    private void Remove()
    {
        Text = null;
        if (_visual != null)
        {
            _visual.StopAnimation("Offset");
            _v.Host.Root.Children.Remove(_visual);
            _visual.Dispose();
            _visual = null;
        }
        _surface?.Dispose();
        _surface = null;
    }

    public void Dispose() => Remove();
}
