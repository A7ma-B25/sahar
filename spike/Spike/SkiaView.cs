using System.Diagnostics;
using System.Numerics;
using SkiaSharp;

namespace Spike;

/// <summary>
/// Mode B: a small window just around the blob, with per-pixel alpha (UpdateLayeredWindow).
/// SkiaSharp draws every frame on the CPU into a DIB; breathing and blinking mean redrawing ~30 times
/// a second even at rest. Transparent pixels pass clicks through on their own, so this one window
/// is also the input window.
/// </summary>
internal sealed unsafe class SkiaView : IBlobView
{
    private readonly nint _hwnd;
    private readonly nint _memDc;
    private readonly nint _bitmap;
    private readonly SKSurface _surface;
    private readonly int _size;
    private readonly float _r;
    private readonly long _t0 = Stopwatch.GetTimestamp();

    private Vector2 _centre;
    private Vector2 _look;
    private double _landAt = -10;
    private float _landK;
    private double _giggleAt = -10;
    private bool _visible = true;

    private readonly SKPaint _bodyPaint = new() { IsAntialias = true, Color = new SKColor(155, 140, 255) };
    private readonly SKPaint _shadowPaint = new() { IsAntialias = true, Color = new SKColor(20, 16, 40, 46) };
    private readonly SKPaint _shinePaint = new() { IsAntialias = true, Color = new SKColor(255, 255, 255, 90) };
    private readonly SKPaint _eyePaint = new() { IsAntialias = true, Color = SKColors.White };
    private readonly SKPaint _pupilPaint = new() { IsAntialias = true, Color = new SKColor(34, 28, 58) };

    public nint InputWindow => _hwnd;

    public bool Animating => Now - _landAt < 0.38 || Now - _giggleAt < 0.48;

    private double Now => Stopwatch.GetElapsedTime(_t0).TotalSeconds;

    public SkiaView(Blob blob)
    {
        _r = blob.Radius;
        _size = (int)MathF.Ceiling(_r * 3.2f);

        _hwnd = Win.Create(blob,
            Native.WS_EX_LAYERED | Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE,
            Native.WS_POPUP | Native.WS_VISIBLE, 0, 0, _size, _size, "spike skia");

        var header = new BITMAPINFOHEADER
        {
            BiSize = (uint)sizeof(BITMAPINFOHEADER),
            BiWidth = _size,
            BiHeight = -_size, // top-down
            BiPlanes = 1,
            BiBitCount = 32,
        };
        nint screen = Native.GetDC(0);
        _memDc = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(0, screen);
        void* bits;
        _bitmap = Native.CreateDIBSection(_memDc, &header, 0, &bits, 0, 0);
        Native.SelectObject(_memDc, _bitmap);
        _surface = SKSurface.Create(new SKImageInfo(_size, _size, SKColorType.Bgra8888, SKAlphaType.Premul), (nint)bits, _size * 4)
                   ?? throw new InvalidOperationException("SKSurface.Create failed");
    }

    public void MoveTo(Vector2 centre)
    {
        _centre = centre;
        Render();
    }

    public void LookAt(Vector2 direction) => _look = direction; // drawn on the next idle tick

    public void Land(float speed)
    {
        _landAt = Now;
        _landK = Math.Clamp(speed / 2400f, 0.15f, 0.45f);
    }

    public void Giggle() => _giggleAt = Now;

    public void SetVisible(bool visible)
    {
        _visible = visible;
        Native.ShowWindow(_hwnd, visible ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
    }

    public bool IdleTick(double seconds)
    {
        if (!_visible) return false;
        Render();
        return true;
    }

    public void SetExcludedFromCapture(bool excluded)
    {
        if (Native.SetWindowDisplayAffinity(_hwnd, excluded ? Native.WDA_EXCLUDEFROMCAPTURE : Native.WDA_NONE) == 0)
            Log.Write("SetWindowDisplayAffinity failed (Windows older than 2004?)");
    }

    private static float Keyframes(double t, double duration, ReadOnlySpan<(float at, float value)> frames)
    {
        float p = (float)Math.Clamp(t / duration, 0, 1);
        for (int i = 1; i < frames.Length; i++)
        {
            if (p <= frames[i].at)
            {
                var (a0, v0) = frames[i - 1];
                var (a1, v1) = frames[i];
                float u = (p - a0) / MathF.Max(a1 - a0, 1e-5f);
                u = u * u * (3 - 2 * u); // smoothstep
                return v0 + (v1 - v0) * u;
            }
        }
        return frames[^1].value;
    }

    private void Render()
    {
        double now = Now;
        var canvas = _surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        float mid = _size / 2f;

        // Squash (visual level) and wobble, pivoting on the bottom of the body.
        float sx = 1, sy = 1, angle = 0;
        double sinceLand = now - _landAt;
        if (sinceLand < 0.38)
        {
            float k = _landK;
            sx = Keyframes(sinceLand, 0.38, [(0f, 1f), (0.25f, 1 + k * 0.6f), (0.6f, 1 - k * 0.2f), (1f, 1f)]);
            sy = Keyframes(sinceLand, 0.38, [(0f, 1f), (0.25f, 1 - k), (0.6f, 1 + k * 0.25f), (1f, 1f)]);
        }
        double sinceGiggle = now - _giggleAt;
        if (sinceGiggle < 0.48)
            angle = Keyframes(sinceGiggle, 0.48, [(0f, 0f), (0.2f, -9f), (0.45f, 8f), (0.7f, -4f), (1f, 0f)]);

        canvas.Save();
        canvas.Translate(mid, mid);
        canvas.DrawOval(0, _r * 1.02f, _r * 0.8f, _r * 0.18f, _shadowPaint);
        canvas.Translate(0, _r);
        canvas.RotateDegrees(angle);
        canvas.Scale(sx, sy);
        // Breathing, inside the squash.
        double phase = now % 3.2 / 3.2;
        float bx = 1 + 0.035f * (float)Math.Sin(phase * Math.PI), by = 1 - 0.035f * (float)Math.Sin(phase * Math.PI);
        canvas.Scale(bx, by);
        canvas.Translate(0, -_r);

        canvas.DrawOval(0, 0, _r, _r * 0.94f, _bodyPaint);
        canvas.DrawOval(-_r * 0.42f, -_r * 0.52f, _r * 0.32f, _r * 0.2f, _shinePaint);

        double blinkP = now % 4.3 / 4.3;
        float blink = blinkP < 0.92 ? 1 : Keyframes(blinkP - 0.92, 0.06, [(0f, 1f), (0.5f, 0.1f), (1f, 1f)]);
        for (int i = 0; i < 2; i++)
        {
            float ex = (i == 0 ? -1 : 1) * _r * 0.36f, ey = -_r * 0.12f;
            canvas.DrawOval(ex, ey, _r * 0.24f, _r * 0.3f * blink, _eyePaint);
            canvas.DrawOval(ex + _look.X * _r * 0.1f, ey + _look.Y * _r * 0.1f, _r * 0.13f, _r * 0.15f * blink, _pupilPaint);
        }
        canvas.Restore();
        canvas.Flush();

        var dst = new POINT { X = (int)(_centre.X - mid), Y = (int)(_centre.Y - mid) };
        var size = new SIZE { Cx = _size, Cy = _size };
        var src = new POINT();
        var blend = new BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
        Native.UpdateLayeredWindow(_hwnd, 0, &dst, &size, _memDc, &src, 0, &blend, Native.ULW_ALPHA);
    }
}
