using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace Spike;

/// <summary>
/// Mode A: one transparent, click-through window over the whole primary screen, whose content is a
/// Windows.UI.Composition visual tree. Breathing, blinking, squash and wobble are keyframe animations
/// the compositor runs on its own; the app only moves the blob while it flies and turns the pupils.
/// A second, almost invisible layered window follows the blob to take its clicks.
/// </summary>
internal sealed unsafe class CompositionView : IBlobView, IWindowHandler
{
    private static nint _dispatcherQueueController; // kept for the life of the process

    private readonly nint _overlay;
    private readonly nint _input;
    private readonly Compositor _compositor;
    private readonly DesktopWindowTarget _target;
    private readonly ShapeVisual _visual;
    private readonly CompositionSpriteShape[] _pupils = new CompositionSpriteShape[2];
    private readonly Vector2[] _eyeCentres = new Vector2[2];
    private readonly float _r;
    private readonly float _size;
    private Vector2 _lastLook = new(float.NaN);

    public nint InputWindow => _input;
    public bool Animating => false; // the compositor animates on its own

    public CompositionView(Blob blob)
    {
        _r = blob.Radius;
        _size = MathF.Ceiling(_r * 3.2f);

        EnsureDispatcherQueue();
        _compositor = new Compositor();

        int sw = Native.GetSystemMetrics(Native.SM_CXSCREEN);
        int sh = Native.GetSystemMetrics(Native.SM_CYSCREEN);
        _overlay = Win.Create(this,
            Native.WS_EX_NOREDIRECTIONBITMAP | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
            Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE, 0, 0, sw, sh, "spike overlay");
        Native.SetLayeredWindowAttributes(_overlay, 0, 255, Native.LWA_ALPHA);

        _target = CreateTarget(_compositor, _overlay);
        var root = _compositor.CreateContainerVisual();
        root.Size = new Vector2(sw, sh);
        _target.Root = root;

        _visual = _compositor.CreateShapeVisual();
        _visual.Size = new Vector2(_size, _size);
        // Squash and wobble pivot on the bottom of the body.
        _visual.CenterPoint = new Vector3(_size / 2, _size / 2 + _r, 0);
        root.Children.InsertAtTop(_visual);
        BuildBlob();

        // The input window: layered at alpha 1, so it is invisible but still takes the mouse.
        int d = (int)(_r * 2);
        _input = Win.Create(blob,
            Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE, 0, 0, d, d, "spike input");
        Native.SetLayeredWindowAttributes(_input, 0, 1, Native.LWA_ALPHA);
    }

    private static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueController != 0) return;
        var options = new DispatcherQueueOptions
        {
            DwSize = sizeof(DispatcherQueueOptions),
            ThreadType = 2,    // DQTYPE_THREAD_CURRENT
            ApartmentType = 0, // DQTAT_COM_NONE: Main is [STAThread]
        };
        nint controller;
        Marshal.ThrowExceptionForHR(Native.CreateDispatcherQueueController(options, &controller));
        _dispatcherQueueController = controller;
    }

    /// <summary>ICompositorDesktopInterop::CreateDesktopWindowTarget, called through the vtable (AOT-safe).</summary>
    private static DesktopWindowTarget CreateTarget(Compositor compositor, nint hwnd)
    {
        nint unknown = ((IWinRTObject)compositor).NativeObject.ThisPtr;
        var iid = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out nint interop));
        try
        {
            nint target;
            var create = (delegate* unmanaged[Stdcall]<nint, nint, int, nint*, int>)(*(*(void***)interop + 3));
            Marshal.ThrowExceptionForHR(create(interop, hwnd, 1, &target));
            return DesktopWindowTarget.FromAbi(target);
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    private void BuildBlob()
    {
        var c = _compositor;
        var centre = new Vector2(_size / 2, _size / 2);

        // Everything that breathes together lives in one container shape.
        var body = c.CreateContainerShape();
        body.Offset = centre;
        body.CenterPoint = new Vector2(0, _r);
        _visual.Shapes.Add(body);

        var shadow = Ellipse(_r * 0.8f, _r * 0.18f, Color.FromArgb(46, 20, 16, 40));
        shadow.Offset = centre + new Vector2(0, _r * 1.02f);
        _visual.Shapes.Add(shadow);

        body.Shapes.Add(Ellipse(_r, _r * 0.94f, Color.FromArgb(255, 155, 140, 255)));
        var shine = Ellipse(_r * 0.32f, _r * 0.2f, Color.FromArgb(90, 255, 255, 255));
        shine.Offset = new Vector2(-_r * 0.42f, -_r * 0.52f);
        body.Shapes.Add(shine);

        var blink = c.CreateVector2KeyFrameAnimation();
        blink.InsertKeyFrame(0f, Vector2.One);
        blink.InsertKeyFrame(0.92f, Vector2.One);
        blink.InsertKeyFrame(0.95f, new Vector2(1f, 0.1f));
        blink.InsertKeyFrame(0.98f, Vector2.One);
        blink.Duration = TimeSpan.FromSeconds(4.3);
        blink.IterationBehavior = AnimationIterationBehavior.Forever;

        for (int i = 0; i < 2; i++)
        {
            _eyeCentres[i] = new Vector2((i == 0 ? -1 : 1) * _r * 0.36f, -_r * 0.12f);
            var eye = Ellipse(_r * 0.24f, _r * 0.3f, Color.FromArgb(255, 255, 255, 255));
            eye.Offset = _eyeCentres[i];
            eye.StartAnimation("Scale", blink);
            body.Shapes.Add(eye);

            var pupil = Ellipse(_r * 0.13f, _r * 0.15f, Color.FromArgb(255, 34, 28, 58));
            pupil.Offset = _eyeCentres[i];
            pupil.StartAnimation("Scale", blink);
            body.Shapes.Add(pupil);
            _pupils[i] = pupil;
        }

        var breathe = c.CreateVector2KeyFrameAnimation();
        breathe.InsertKeyFrame(0f, Vector2.One);
        breathe.InsertKeyFrame(0.5f, new Vector2(1.035f, 0.965f));
        breathe.InsertKeyFrame(1f, Vector2.One);
        breathe.Duration = TimeSpan.FromSeconds(3.2);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        body.StartAnimation("Scale", breathe);
    }

    private CompositionSpriteShape Ellipse(float rx, float ry, Color color)
    {
        var g = _compositor.CreateEllipseGeometry();
        g.Radius = new Vector2(rx, ry);
        var s = _compositor.CreateSpriteShape(g);
        s.FillBrush = _compositor.CreateColorBrush(color);
        return s;
    }

    public void MoveTo(Vector2 centre)
    {
        _visual.Offset = new Vector3(centre.X - _size / 2, centre.Y - _size / 2, 0);
        Native.SetWindowPos(_input, Native.HWND_TOPMOST, (int)(centre.X - _r), (int)(centre.Y - _r), 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    public void LookAt(Vector2 direction)
    {
        // Skip tiny changes: no compositor traffic while the cursor rests.
        if (!float.IsNaN(_lastLook.X) && Vector2.DistanceSquared(direction, _lastLook) < 0.0004f) return;
        _lastLook = direction;
        for (int i = 0; i < 2; i++)
            _pupils[i].Offset = _eyeCentres[i] + direction * (_r * 0.1f);
    }

    public void Land(float speed)
    {
        float k = Math.Clamp(speed / 2400f, 0.15f, 0.45f);
        var squash = _compositor.CreateVector3KeyFrameAnimation();
        squash.InsertKeyFrame(0f, Vector3.One);
        squash.InsertKeyFrame(0.25f, new Vector3(1 + k * 0.6f, 1 - k, 1));
        squash.InsertKeyFrame(0.6f, new Vector3(1 - k * 0.2f, 1 + k * 0.25f, 1));
        squash.InsertKeyFrame(1f, Vector3.One);
        squash.Duration = TimeSpan.FromMilliseconds(380);
        _visual.StartAnimation("Scale", squash);
    }

    public void Giggle()
    {
        var wobble = _compositor.CreateScalarKeyFrameAnimation();
        wobble.InsertKeyFrame(0f, 0f);
        wobble.InsertKeyFrame(0.2f, -9f);
        wobble.InsertKeyFrame(0.45f, 8f);
        wobble.InsertKeyFrame(0.7f, -4f);
        wobble.InsertKeyFrame(1f, 0f);
        wobble.Duration = TimeSpan.FromMilliseconds(480);
        _visual.StartAnimation("RotationAngleInDegrees", wobble);
    }

    public void SetVisible(bool visible)
    {
        _visual.IsVisible = visible;
        Native.ShowWindow(_input, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
    }

    public bool IdleTick(double seconds) => false;

    public void SetExcludedFromCapture(bool excluded)
    {
        uint a = excluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE;
        if (Native.SetWindowDisplayAffinity(_overlay, a) == 0 || Native.SetWindowDisplayAffinity(_input, a) == 0)
            Log.Write("SetWindowDisplayAffinity failed (Windows older than 2004?)");
    }

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam) =>
        msg == Native.WM_MOUSEACTIVATE ? Native.MA_NOACTIVATE : null;
}
