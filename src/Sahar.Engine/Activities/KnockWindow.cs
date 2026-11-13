using System.Numerics;
using Sahar.Core.Activities;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;

namespace Sahar.Engine.Activities;

/// <summary>
/// The knock (spec 0.13 §1.4): while she hides for a fullscreen app on her monitor, a critical thing drops a small tag
/// from that monitor's top edge for 4 s and goes back up. Its own click-through, never-focusable overlay window, sized
/// to the tag (never the monitor: <see cref="Knock.Rect"/>); no buttons — a click in a game is dangerous.
/// </summary>
internal sealed class KnockWindow : IDisposable
{
    private readonly OverlayWindow _window;
    private readonly DesktopWindowTarget _target;
    private readonly ContainerVisual _root;
    private readonly SpriteVisual _tag, _shadow;
    private readonly SwapSurface _surface;
    private readonly DispatcherQueueTimer _timer;
    private readonly Compositor _c;
    private float _dpi = 1;

    public KnockWindow(CompositionHost host, DispatcherQueue ui, bool capturable)
    {
        _c = host.Compositor;
        _window = new OverlayWindow(capturable, new RectI(0, 0, 1, 1), visible: false, title: "Sahar knock");
        _target = host.AddTarget(_window.Hwnd);
        _root = _c.CreateContainerVisual();
        _target.Root = _root;
        _shadow = _c.CreateSpriteVisual();
        _tag = _c.CreateSpriteVisual();
        _root.Children.InsertAtTop(_shadow);
        _root.Children.InsertAtTop(_tag);
        _surface = new SwapSurface(host, new SizeI(400, 80));
        _timer = ui.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Up();
    }

    public bool Showing { get; private set; }
    public RectI Rect => _window.Rect;
    public int Knocks { get; private set; }

    public void Show(Activity a, RectI monitor, float dpi, bool dark)
    {
        _dpi = dpi;
        var t = Tokens.For(dark);
        float w = ActivityPainter.TagWidth(a);
        var size = new SizeI((int)MathF.Ceiling(w * dpi), (int)MathF.Ceiling(TagPlace.HeightDip * dpi));
        if (size.Width + 2 > _surface.Size.Width || size.Height + 2 > _surface.Size.Height)
            _surface.Resize(new SizeI(Math.Max(size.Width + 2, _surface.Size.Width), Math.Max(size.Height + 2, _surface.Size.Height)));
        var scale = Matrix3x2.CreateScale(dpi);
        int i = _surface.Draw(dc => { dc.Clear(); dc.SetTransform(scale); ActivityPainter.Tag(dc, a with { Progress = null }, w, t, null, null, dark); });
        var brush = _surface.Brushes[i];
        brush.Stretch = CompositionStretch.None;
        brush.HorizontalAlignmentRatio = 0;
        brush.VerticalAlignmentRatio = 0;
        var rect = Knock.Rect(monitor, size, dpi);
        Win.Apply(_window.MoveTo(rect));
        float left = (rect.Width - size.Width) / 2f, top = MathF.Round(8 * dpi);
        foreach (var v in new[] { _tag, _shadow })
        {
            v.Brush = brush;
            v.Size = new Vector2(size.Width, size.Height);
            v.Offset = new Vector3(MathF.Round(left), top, 0);
        }
        var s = _c.CreateDropShadow();
        s.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        s.BlurRadius = (dark ? 18 : 14) * dpi;
        s.Offset = new Vector3(0, (dark ? 6 : 4) * dpi, 0);
        s.Opacity = dark ? 0.42f : 0.10f;
        _shadow.Shadow = s;
        Native.ShowWindow(_window.Hwnd, Native.SW_SHOWNOACTIVATE);
        Native.SetWindowPos(_window.Hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, ActNative.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        Showing = true;
        Knocks++;
        Slide(-rect.Height, 0, 250);
        _timer.Stop();
        _timer.Interval = Knock.Length;
        _timer.Start();
    }

    private void Up()
    {
        if (!Showing) return;
        Slide(0, -_window.Rect.Height, 200, () =>
        {
            Native.ShowWindow(_window.Hwnd, Native.SW_HIDE);
            Showing = false;
        });
    }

    private void Slide(float from, float to, double ms, Action? done = null)
    {
        if (ExpressionPlayer.Reduced)
        {
            _root.Offset = new Vector3(0, to, 0);
            done?.Invoke();
            return;
        }
        var a = _c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, new Vector3(0, from, 0));
        a.InsertKeyFrame(1, new Vector3(0, to, 0), _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f)));
        a.Duration = TimeSpan.FromMilliseconds(ms);
        var batch = _c.CreateScopedBatch(CompositionBatchTypes.Animation);
        _root.StartAnimation("Offset", a);
        batch.End();
        if (done is not null) batch.Completed += (_, _) => done();
    }

    public string Describe() =>
        $"knock={(Showing ? "true" : "false")} knocks={Knocks} knockRect={Rect.Left},{Rect.Top},{Rect.Width},{Rect.Height}";

    public void Dispose()
    {
        _surface.Dispose();
        Native.DestroyWindow(_window.Hwnd);
    }
}
