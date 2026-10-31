using System.Diagnostics;
using System.Numerics;

namespace Spike;

/// <summary>How a drawing approach shows the blob. Both modes implement it; the behaviour is shared.</summary>
internal interface IBlobView
{
    /// <summary>The window that takes the blob's clicks and drags.</summary>
    nint InputWindow { get; }
    /// <summary>The blob's centre (screen pixels) moved.</summary>
    void MoveTo(Vector2 centre);
    /// <summary>Where the pupils look: a unit-ish vector, (0,0) = straight ahead.</summary>
    void LookAt(Vector2 direction);
    /// <summary>It hit the floor at this speed (px/s): squash.</summary>
    void Land(float speed);
    /// <summary>It was poked: a happy wobble.</summary>
    void Giggle();
    void SetVisible(bool visible);
    /// <summary>Called on idle ticks (~30 Hz); returns true if the view drew something.</summary>
    bool IdleTick(double seconds);
    /// <summary>True while the view has its own frames to draw (a squash in progress).</summary>
    bool Animating { get; }
    void SetExcludedFromCapture(bool excluded);
}

/// <summary>
/// The shared behaviour: where the blob is, drag and throw, gravity and bounce, eyes on the cursor.
/// Pure state plus the input handling for the view's input window.
/// </summary>
internal sealed class Blob : IWindowHandler
{
    public readonly float Radius;
    public Vector2 Position;
    public Vector2 Velocity;
    public bool Dragging { get; private set; }
    public bool Flying { get; private set; }

    private IBlobView _view = null!;
    private Vector2 _grab;
    private Vector2 _downAt;
    private long _downTicks;
    private readonly Queue<(long ticks, Vector2 pos)> _trail = new();
    private RECT _area;

    private const float Gravity = 2600f;
    private const float Bounce = 0.38f;
    private const float MaxThrow = 4500f;

    public Blob(float radius)
    {
        Radius = radius;
        _area = Native.WorkArea();
        Position = new Vector2(_area.Right - radius * 5, Floor);
    }

    private float Floor => _area.Bottom - Radius;

    public void Attach(IBlobView view)
    {
        _view = view;
        _view.MoveTo(Position);
    }

    public bool NeedsFrames => Flying || _view.Animating;

    /// <summary>One physics step while flying.</summary>
    public void Step(float dt)
    {
        if (!Flying || Dragging) return;
        dt = MathF.Min(dt, 1f / 30f);
        Velocity.Y += Gravity * dt;
        Position += Velocity * dt;

        if (Position.Y >= Floor)
        {
            Position.Y = Floor;
            if (Velocity.Y > 260f) _view.Land(Velocity.Y);
            Velocity.Y = -Velocity.Y * Bounce;
            Velocity.X *= 0.82f;
            if (MathF.Abs(Velocity.Y) < 140f) Velocity.Y = 0;
        }
        if (Position.Y < _area.Top + Radius) { Position.Y = _area.Top + Radius; Velocity.Y = 0; }
        if (Position.X < _area.Left + Radius) { Position.X = _area.Left + Radius; Velocity.X = -Velocity.X * 0.5f; }
        if (Position.X > _area.Right - Radius) { Position.X = _area.Right - Radius; Velocity.X = -Velocity.X * 0.5f; }

        bool onFloor = Position.Y >= Floor - 0.5f;
        if (onFloor) Velocity.X *= MathF.Pow(0.08f, dt); // floor friction
        if (onFloor && Velocity.Y == 0 && MathF.Abs(Velocity.X) < 12f)
        {
            Velocity = Vector2.Zero;
            Flying = false;
        }
        _view.MoveTo(Position);
    }

    /// <summary>Eyes follow the cursor; returns true when the cursor is near (poll faster then).</summary>
    public bool TrackCursor()
    {
        var c = Native.CursorPos();
        var d = new Vector2(c.X, c.Y) - Position;
        float dist = d.Length();
        var dir = dist < 1 ? Vector2.Zero : d / dist * MathF.Min(1f, dist / (Radius * 6));
        _view.LookAt(dir);
        return dist < Radius * 10;
    }

    public void RefreshWorkArea() => _area = Native.WorkArea();

    public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_SETCURSOR:
                Native.SetCursor(Native.LoadCursor(0, Native.IDC_HAND));
                return 1;
            case Native.WM_LBUTTONDOWN:
            {
                var c = Native.CursorPos();
                var cv = new Vector2(c.X, c.Y);
                Native.SetCapture(hwnd);
                Dragging = true;
                Flying = false;
                Velocity = Vector2.Zero;
                _grab = cv - Position;
                _downAt = cv;
                _downTicks = Stopwatch.GetTimestamp();
                _trail.Clear();
                _trail.Enqueue((_downTicks, Position));
                return 0;
            }
            case Native.WM_MOUSEMOVE when Dragging:
            {
                var c = Native.CursorPos();
                Position = new Vector2(c.X, c.Y) - _grab;
                long now = Stopwatch.GetTimestamp();
                _trail.Enqueue((now, Position));
                while (_trail.Count > 2 && Stopwatch.GetElapsedTime(_trail.Peek().ticks, now).TotalMilliseconds > 100)
                    _trail.Dequeue();
                _view.MoveTo(Position);
                return 0;
            }
            case Native.WM_LBUTTONUP when Dragging:
            {
                Native.ReleaseCapture();
                Release();
                return 0;
            }
            case Native.WM_CAPTURECHANGED when Dragging:
                Release();
                return 0;
        }
        return null;
    }

    private void Release()
    {
        if (!Dragging) return;
        Dragging = false;
        long now = Stopwatch.GetTimestamp();
        var c = Native.CursorPos();
        bool click = Vector2.Distance(new Vector2(c.X, c.Y), _downAt) < 5 &&
                     Stopwatch.GetElapsedTime(_downTicks, now).TotalMilliseconds < 300;
        if (click)
        {
            _view.Giggle();
            Velocity = new Vector2(0, -900f);
        }
        else
        {
            var (t0, p0) = _trail.Peek();
            double secs = Stopwatch.GetElapsedTime(t0, now).TotalSeconds;
            Velocity = secs > 0.005 ? (Position - p0) / (float)secs : Vector2.Zero;
            if (Velocity.Length() > MaxThrow) Velocity = Vector2.Normalize(Velocity) * MaxThrow;
        }
        Flying = true;
    }
}
