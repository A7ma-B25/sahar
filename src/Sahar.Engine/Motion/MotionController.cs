using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.UI.Composition;

namespace Sahar.Engine.Motion;

/// <summary>
/// Motions 1–12 (board "حركة سَحَر"). CPU-driven motions (travel, drag, throw, loops) run on the 60 Hz
/// <see cref="Ticker"/>, which stops as soon as she rests; everything else is a compositor animation.
/// Positions are her body's top-left in screen pixels.
/// </summary>
internal sealed class MotionController
{
    public const float ThrowSpeed = 300f;       // px/s: release faster than this throws her
    public const float TiltDegrees = 8f;
    public const float LeanDegrees = 12f;
    public const float EyesUnits = 4f;
    public const float CelebrateRadiusDip = 50f;

    private readonly SaharVisual _v;
    private readonly ExpressionPlayer _player;
    private readonly Ticker _ticker;
    private readonly Action<Vector2> _moveCanvasTo;
    private readonly Action<string, bool> _express;   // key, transient (returns to the brain's face after a moment)
    private readonly Wander _wander = new(Environment.TickCount);
    private readonly Random _rng = new();

    private Func<float, bool>? _step;
    private bool _eyes;
    private Vector2 _lastCursor;
    private float _sinceCursorMoved;
    private float _eyeClock;

    // Drag state
    private readonly Pendulum _pendulum = new();
    private readonly List<(double T, Vector2 P)> _samples = [];
    private double _dragClock;
    private Vector2 _lastDragVelocity;

    public MotionKind Current { get; private set; } = MotionKind.Hover;
    /// <summary>Raised when FullscreenHide has shrunk her away (the overlay can then be hidden).</summary>
    public event Action? HiddenAway;
    /// <summary>The user grabbed her (mouse or pipe).</summary>
    public event Action? DragStarted;
    private bool _throwNext;
    private Vector2? _home;
    /// <summary>Sitting or sleeping: no idle bob.</summary>
    public bool Still { get; private set; }

    public MotionController(SaharVisual v, ExpressionPlayer player, Ticker ticker, Action<Vector2> moveCanvasTo, Action<string, bool> express)
    {
        _v = v;
        _player = player;
        _ticker = ticker;
        _moveCanvasTo = moveCanvasTo;
        _express = express;
        _ticker.Tick += OnTick;
        _player.Still = () => Still;
    }

    private Vector2 BodySize => new(SaharVisual.BodyUnits.Width * _v.Scale, SaharVisual.BodyUnits.Height * _v.Scale);
    private Vector2 BodyOffset => new(SaharVisual.BodyUnits.X * _v.Scale, SaharVisual.BodyUnits.Y * _v.Scale);

    public Vector2 TopLeft
    {
        get => _v.Position + BodyOffset;
        set => _moveCanvasTo(value - BodyOffset);
    }

    private Vector2 Centre => TopLeft + BodySize / 2;
    private MonitorInfo Monitor => Monitors.At(Centre);

    /// <summary>Canvas-pixel points the root pivots around.</summary>
    private Vector3 PivotBodyCentre => new(BodyOffset + BodySize / 2, 0);
    private Vector3 PivotBottom => new(BodyOffset.X + BodySize.X / 2, BodyOffset.Y + BodySize.Y, 0);
    private Vector3 PivotLeaf => new(150 * _v.Scale, 60 * _v.Scale, 0);

    public bool Play(MotionKind k, Vector2? velocity = null)
    {
        if (k is not (MotionKind.Wake or MotionKind.FullscreenHide)) _home = null;
        switch (k)
        {
            case MotionKind.Hover: Reset(k); break;
            case MotionKind.Wander: Reset(k); StartWander(); break;
            case MotionKind.PeekTaskbar: Reset(k); StartPeek(); break;
            case MotionKind.EyesFollow: Reset(k); _eyes = true; break;
            case MotionKind.Drag: BeginDrag(); _step = TimedRelease(_step!, 1.5f); break;
            case MotionKind.Throw: Reset(k); StartThrow(velocity ?? new Vector2(_rng.Next(-1400, 1400), -500)); break;
            case MotionKind.LandAndSit: Reset(k); StartLand(); break;
            case MotionKind.SleepInCorner: Reset(k); StartCorner(sleep: true); break;
            case MotionKind.Wake: StartWake(); break;
            case MotionKind.FullscreenHide: Reset(k); StartCorner(sleep: false); break;
            case MotionKind.PerchWindow: Reset(k); StartPerchWindow(); break;
            case MotionKind.CelebrateLoop: Reset(k); StartCelebrate(); break;
            default: return false;   // 13–25 arrive in Plan 2
        }
        UpdateTicker();
        return true;
    }

    // ---------- shared ----------

    private void Reset(MotionKind next)
    {
        Current = next;
        _step = null;
        // Her eyes follow the mouse whenever she is resting (spec §6 motion 4).
        _eyes = next is MotionKind.Hover or MotionKind.EyesFollow or MotionKind.LandAndSit or MotionKind.PerchWindow;
        Still = false;
        var root = _v.Root;
        root.StopAnimation("RotationAngleInDegrees");
        root.StopAnimation("Scale");
        root.RotationAngleInDegrees = 0;
        root.Scale = Vector3.One;
        root.Clip = null;
        _v.Face.Container.Offset = Vector3.Zero;
        RestoreBob();
    }

    private void RestoreBob()
    {
        if (!Still && !_player.Current.Extras.HasFlag(Sahar.Core.Expressions.Extras.Grounded)) IdleAnimations.StartBob(_v);
    }

    private void HoldStill()
    {
        Still = true;
        _v.Character.StopAnimation("Offset.Y");
        _v.Character.Offset = _v.Character.Offset with { Y = 0 };
    }

    private void UpdateTicker()
    {
        if (_step != null) _ticker.Start(16);
        else if (_eyes) _ticker.Start(_sinceCursorMoved < 0.5f ? 33 : 250);
        else _ticker.Stop();
    }

    private void OnTick(float dt)
    {
        if (_throwNext)
        {
            _throwNext = false;
            UpdateTicker();
            throw new InvalidOperationException("test: exception in a motion tick");
        }
        var s = _step;
        if (s != null && !s(dt) && _step == s) _step = null;
        if (_eyes) FollowEyes(dt);
        UpdateTicker();
    }

    /// <summary>Moves her body's top-left to <paramref name="to"/> over <paramref name="seconds"/>.</summary>
    private Func<float, bool> Travel(Vector2 to, float seconds, Func<float, float> ease, Action<float>? frame = null, Action? done = null)
    {
        var from = TopLeft;
        float t = 0;
        return dt =>
        {
            t = MathF.Min(1, t + dt / seconds);
            TopLeft = Vector2.Lerp(from, to, ease(t));
            frame?.Invoke(t);
            if (t < 1) return true;
            _step = null;
            if (Current != MotionKind.FullscreenHide) Reclamp();
            done?.Invoke();
            return _step != null;
        };
    }

    /// <summary>Puts her body back inside a real work area (monitor unplugged, resolution or taskbar changed).</summary>
    public void Reclamp()
    {
        var size = BodySize;
        var areas = Monitors.All().Select(m => m.WorkF).ToList();
        var r = Sahar.Core.Geometry.Placement.Reclamp(new RectF(TopLeft.X, TopLeft.Y, size.X, size.Y), areas);
        if (r.X != TopLeft.X || r.Y != TopLeft.Y) TopLeft = new Vector2(r.X, r.Y);
    }

    /// <summary>The display layout changed: stop any trip planned on the old layout and get back on screen.</summary>
    public void DisplayChanged()
    {
        if (_step != null && Current is not (MotionKind.Drag or MotionKind.Throw)) Play(MotionKind.Hover);
        if (Current != MotionKind.FullscreenHide) Reclamp();
    }

    /// <summary>Test mode: the next tick throws, to prove the loop survives it.</summary>
    public void ArmThrow()
    {
        _throwNext = true;
        _ticker.Start(16);
    }

    private static float Smooth(float t) => t * t * (3 - 2 * t);
    private static float EaseIn(float t) => t * t;
    private static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

    private Vector2 Clamp(Vector2 topLeft) =>
        Physics.Clamp(new Body2D(topLeft, Vector2.Zero), Monitor.WorkF, BodySize).Position;

    private void Squash(bool horizontal)
    {
        var c = _v.Root.Compositor;
        var squash = horizontal ? new Vector3(0.86f, 1.14f, 1) : new Vector3(1.14f, 0.86f, 1);
        _v.Character.StartAnimation("Scale", IdleAnimations.Loop3(c, 0.35,
            (0f, Vector3.One), (0.35f, squash), (0.7f, new Vector3(0.97f, 1.03f, 1)), (1f, Vector3.One)) .Once());
    }

    // ---------- 1 Wander ----------

    private void StartWander()
    {
        var m = Monitor;
        var cursor = Native.CursorPos();
        var avoid = new[] { new RectF(cursor.X - 80, cursor.Y - 80, 160, 160) };
        var target = _wander.NextTarget(m.WorkF, BodySize, avoid);
        float dist = Vector2.Distance(target, TopLeft);
        float secs = Math.Clamp(dist / (90 * m.DpiScale), 4, 8);
        float dir = MathF.Sign(target.X - TopLeft.X);
        _v.Root.CenterPoint = PivotBodyCentre;
        _step = Travel(target, secs, Smooth,
            frame: t => _v.Root.RotationAngleInDegrees = TiltDegrees * dir * MathF.Sin(MathF.PI * t),
            done: () => Play(MotionKind.Hover));
    }

    // ---------- 3 PeekTaskbar ----------

    private void StartPeek()
    {
        var m = Monitor;
        var (bar, edge, _) = Taskbar.Get(m.Handle);
        var size = BodySize;
        float along = Math.Clamp((Centre.X - bar.X) / MathF.Max(1, bar.Width), 0.15f, 0.85f);
        float alongY = Math.Clamp((Centre.Y - bar.Y) / MathF.Max(1, bar.Height), 0.15f, 0.85f);
        // Hidden behind the bar, then out by 70 % of her height (width for side bars).
        (Vector2 hidden, Vector2 peek) = edge switch
        {
            Edge.Top => (new Vector2(bar.X + bar.Width * along - size.X / 2, bar.Y + bar.Height - size.Y),
                         new Vector2(bar.X + bar.Width * along - size.X / 2, bar.Y + bar.Height - size.Y * 0.3f)),
            Edge.Left => (new Vector2(bar.X + bar.Width - size.X, bar.Y + bar.Height * alongY - size.Y / 2),
                          new Vector2(bar.X + bar.Width - size.X * 0.3f, bar.Y + bar.Height * alongY - size.Y / 2)),
            Edge.Right => (new Vector2(bar.X, bar.Y + bar.Height * alongY - size.Y / 2),
                           new Vector2(bar.X - size.X * 0.7f, bar.Y + bar.Height * alongY - size.Y / 2)),
            _ => (new Vector2(bar.X + bar.Width * along - size.X / 2, bar.Y),
                  new Vector2(bar.X + bar.Width * along - size.X / 2, bar.Y - size.Y * 0.7f)),
        };
        HoldStill();
        var clip = _v.Root.Compositor.CreateInsetClip();
        _v.Root.Clip = clip;
        void Clip()
        {
            // Nothing of her is drawn on the far side of the bar's inner edge.
            var p = _v.Position;
            var canvas = new Vector2(_v.CanvasPx.Width, _v.CanvasPx.Height);
            clip.TopInset = clip.BottomInset = clip.LeftInset = clip.RightInset = 0;
            switch (edge)
            {
                case Edge.Top: clip.TopInset = MathF.Max(0, bar.Y + bar.Height - p.Y); break;
                case Edge.Left: clip.LeftInset = MathF.Max(0, bar.X + bar.Width - p.X); break;
                case Edge.Right: clip.RightInset = MathF.Max(0, p.X + canvas.X - bar.X); break;
                default: clip.BottomInset = MathF.Max(0, p.Y + canvas.Y - bar.Y); break;
            }
        }
        TopLeft = hidden;
        Clip();
        float look = 3 * _v.Scale;
        _step = Travel(peek, 0.9f, EaseOut, _ => Clip(), done: () =>
        {
            float t = 0;
            _step = dt =>
            {
                t += dt;
                // Look left, then right, then back (2 s).
                float x = t < 0.7f ? -look : t < 1.5f ? look : 0;
                _v.Face.Container.Offset = new Vector3(x, 0, 0);
                if (t < 2f) return true;
                _v.Face.Container.Offset = Vector3.Zero;
                _step = Travel(hidden, 0.9f, EaseIn, _ => Clip(), done: () => Play(MotionKind.LandAndSit));
                return true;
            };
        });
    }

    // ---------- 4 EyesFollow ----------

    private void FollowEyes(float dt)
    {
        var p = Native.CursorPos();
        var cursor = new Vector2(p.X, p.Y);
        if (cursor != _lastCursor) { _lastCursor = cursor; _sinceCursorMoved = 0; }
        else _sinceCursorMoved += dt;
        _eyeClock += dt;
        if (_sinceCursorMoved > 0.5f || _eyeClock < 0.033f) return;
        _eyeClock = 0;
        var eyes = _v.Position + new Vector2(150, 154) * _v.Scale;
        var d = cursor - eyes;
        float len = d.Length();
        var offset = len < 1 ? Vector2.Zero : d / len * MathF.Min(1, len / 200f) * EyesUnits * _v.Scale;
        _v.Face.Container.Offset = new Vector3(offset, 0);
    }

    // ---------- 5 Drag ----------

    /// <summary>Starts hanging from the leaf under the cursor.</summary>
    public void BeginDrag()
    {
        DragStarted?.Invoke();
        Reset(MotionKind.Drag);
        HoldStill();
        Still = false;
        var root = _v.Root;
        root.Scale = new Vector3(0.92f, 1.1f, 1);
        _samples.Clear();
        _dragClock = 0;
        var prevVelocity = Vector2.Zero;
        _express("dragged", false);
        _step = dt =>
        {
            _dragClock += dt;
            var c = Native.CursorPos();
            var cursor = new Vector2(c.X, c.Y);
            // Pivot from the current scale: crossing onto a monitor with another DPI rescales her mid-drag.
            root.CenterPoint = PivotLeaf;
            _moveCanvasTo(cursor - new Vector2(PivotLeaf.X, PivotLeaf.Y));
            _samples.Add((_dragClock, cursor));
            _samples.RemoveAll(s => _dragClock - s.T > 0.1);
            var velocity = Velocity();
            var accel = (velocity - prevVelocity) / dt;
            prevVelocity = velocity;
            _pendulum.Step(dt, accel.X);
            root.RotationAngleInDegrees = _pendulum.Angle;
            _lastDragVelocity = velocity;
            return true;
        };
        UpdateTicker();
    }

    private Vector2 Velocity()
    {
        if (_samples.Count < 2) return Vector2.Zero;
        var (t0, p0) = _samples[0];
        var (t1, p1) = _samples[^1];
        return t1 - t0 < 1e-3 ? Vector2.Zero : (p1 - p0) / (float)(t1 - t0);
    }

    /// <summary>Lets go: fast → thrown, near the taskbar → sits, otherwise floats where dropped.</summary>
    public void EndDrag()
    {
        if (Current != MotionKind.Drag) return;
        _express("", true);   // back to the brain's face
        var v = _lastDragVelocity;
        var root = _v.Root;
        root.Scale = Vector3.One;
        root.RotationAngleInDegrees = 0;
        TopLeft = Clamp(TopLeft);
        if (v.Length() > ThrowSpeed) Play(MotionKind.Throw, v);
        else if (Monitor.Work.Bottom - (TopLeft.Y + BodySize.Y) < 120 * Monitor.DpiScale) Play(MotionKind.LandAndSit);
        else Play(MotionKind.Hover);
    }

    private Func<float, bool> TimedRelease(Func<float, bool> inner, float seconds)
    {
        float t = 0;
        return dt =>
        {
            t += dt;
            inner(dt);
            if (t < seconds) return true;
            _step = null;
            EndDrag();
            return _step != null;
        };
    }

    // ---------- 6 Throw ----------

    private void StartThrow(Vector2 velocity)
    {
        var body = new Body2D(Clamp(TopLeft), velocity);
        _v.Root.CenterPoint = PivotBodyCentre;
        float spin = 0, dir = velocity.X < 0 ? -1 : 1;
        _step = dt =>
        {
            var before = body.Velocity;
            (body, bool resting) = Physics.Step(body, dt, Monitor.WorkF, BodySize);
            if (MathF.Sign(before.X) != MathF.Sign(body.Velocity.X) && body.Velocity.X != 0) Squash(horizontal: true);
            if (MathF.Sign(before.Y) != MathF.Sign(body.Velocity.Y) && body.Velocity.Y != 0) Squash(horizontal: false);
            TopLeft = body.Position;
            spin += 360 * dt * dir * MathF.Min(1, body.Velocity.Length() / 400f + 0.2f);
            _v.Root.RotationAngleInDegrees = spin % 360;
            if (!resting) return true;
            _step = null;
            float a = ((spin % 360) + 360) % 360;
            Play(MotionKind.Hover);
            if (a > 1)
            {
                _v.Root.CenterPoint = PivotBodyCentre;
                var settle = _v.Root.Compositor.CreateScalarKeyFrameAnimation();
                settle.InsertKeyFrame(0f, a);
                settle.InsertKeyFrame(1f, a > 180 ? 360 : 0);
                settle.Duration = TimeSpan.FromMilliseconds(400);
                _v.Root.StartAnimation("RotationAngleInDegrees", settle);
            }
            return _step != null;
        };
    }

    // ---------- 7 LandAndSit ----------

    private Vector2 PerchTopLeft(float along)
    {
        var m = Monitor;
        var (bar, edge, _) = Taskbar.Get(m.Handle);
        var size = BodySize;
        var p = Perch.OnTaskbar(bar, edge, edge is Edge.Left or Edge.Right ? size.Y : size.X, along);
        var topLeft = edge switch
        {
            Edge.Top => new Vector2(p.X - size.X / 2, p.Y),
            Edge.Left => new Vector2(p.X, p.Y - size.Y / 2),
            Edge.Right => new Vector2(p.X - size.X, p.Y - size.Y / 2),
            _ => new Vector2(p.X - size.X / 2, p.Y - size.Y),
        };
        return Clamp(topLeft);
    }

    private float AlongBar()
    {
        var (bar, edge, _) = Taskbar.Get(Monitor.Handle);
        return edge is Edge.Left or Edge.Right
            ? (Centre.Y - bar.Y) / MathF.Max(1, bar.Height)
            : (Centre.X - bar.X) / MathF.Max(1, bar.Width);
    }

    private void StartLand()
    {
        HoldStill();
        _step = Travel(PerchTopLeft(AlongBar()), 0.8f, EaseIn, done: () => Squash(horizontal: false));
    }

    // ---------- 8 SleepInCorner / 10 FullscreenHide ----------

    private void StartCorner(bool sleep)
    {
        if (!sleep) _home ??= TopLeft;
        HoldStill();
        var target = PerchTopLeft(1f);
        _step = Travel(target, sleep ? 2f : 0.5f, Smooth, done: () =>
        {
            var c = _v.Root.Compositor;
            _v.Root.CenterPoint = PivotBottom;
            float k = sleep ? 0.75f : 4f * Monitor.DpiScale / BodySize.X;
            var shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, new Vector3(k, k, 1));
            shrink.Duration = TimeSpan.FromMilliseconds(sleep ? 600 : 350);
            var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
            _v.Root.StartAnimation("Scale", shrink);
            batch.End();
            if (!sleep) batch.Completed += (_, _) => { if (Current == MotionKind.FullscreenHide) HiddenAway?.Invoke(); };
            if (sleep) _express("sleep", false);
        });
    }

    // ---------- 9 Wake ----------

    private void StartWake()
    {
        var from = _v.Root.Scale;
        Reset(MotionKind.Wake);
        if (_home is { } home)
        {
            // Back where she was before hiding.
            TopLeft = home;
            _home = null;
            Reclamp();
        }
        var c = _v.Root.Compositor;
        _v.Root.CenterPoint = PivotBottom;
        _v.Root.Scale = from;
        var stretch = c.CreateVector3KeyFrameAnimation();
        stretch.InsertKeyFrame(0f, from);
        stretch.InsertKeyFrame(0.45f, new Vector3(0.86f, 1.2f, 1));
        stretch.InsertKeyFrame(1f, Vector3.One);
        stretch.Duration = TimeSpan.FromMilliseconds(700);
        _v.Root.StartAnimation("Scale", stretch);
        _express("back", true);
        float t = 0;
        _step = dt =>
        {
            t += dt;
            if (t < 0.75f) return true;
            _step = null;
            Current = MotionKind.Hover;   // awake: hovering again (lean works)
            _eyes = true;
            return false;
        };
    }

    // ---------- 11 PerchWindow ----------

    private void StartPerchWindow()
    {
        var target = WindowPerch();
        if (target is null)
        {
            // Nowhere to sit on: the taskbar will do.
            HoldStill();
            _step = Travel(PerchTopLeft(AlongBar()), 0.8f, EaseIn, done: () => Squash(horizontal: false));
            return;
        }
        HoldStill();
        _step = Travel(target.Value, 1.2f, Smooth, done: () =>
        {
            Squash(horizontal: false);
            _v.Root.CenterPoint = PivotBottom;
            _v.Root.StartAnimation("RotationAngleInDegrees", IdleAnimations.Loop(_v.Root.Compositor, 2.0, (0f, -6f), (0.5f, 6f), (1f, -6f)));
        });
    }

    private unsafe Vector2? WindowPerch()
    {
        nint fg = Native.GetForegroundWindow();
        if (fg == 0 || fg == Native.GetShellWindow() || Win.Own.Contains(fg)) return null;
        if (Native.IsWindowVisible(fg) == 0 || Native.IsIconic(fg) != 0 || Native.IsZoomed(fg) != 0) return null;
        if (Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        RECT r;
        if (Native.GetWindowRect(fg, &r) == 0) return null;
        var rect = new RectF(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        var size = BodySize;
        var p = Perch.OnWindowTop(rect, size.X, 0.7f);
        var topLeft = new Vector2(p.X - size.X / 2, p.Y - size.Y);
        var work = Monitors.At(p).WorkF;
        if (topLeft.Y < work.Y || topLeft.X < work.X || topLeft.X + size.X > work.X + work.Width) return null;
        return topLeft;
    }

    // ---------- 12 CelebrateLoop ----------

    private void StartCelebrate()
    {
        var start = TopLeft;
        float r = CelebrateRadiusDip * Monitor.DpiScale;
        _v.Root.CenterPoint = PivotBodyCentre;
        float t = 0;
        _step = dt =>
        {
            t = MathF.Min(1, t + dt / 2.6f);
            float a = 2 * MathF.PI * Smooth(t);
            TopLeft = Clamp(start + new Vector2(r * MathF.Sin(a), -r * (1 - MathF.Cos(a))));
            _v.Root.RotationAngleInDegrees = 360 * Smooth(t);
            if (t < 1) return true;
            _step = null;
            Play(MotionKind.Hover);
            return _step != null;
        };
    }

    // ---------- hover lean ----------

    /// <summary>Leans 12° toward the cursor while it rests on her (Hover only).</summary>
    public void Lean(bool on)
    {
        if (Current is not (MotionKind.Hover or MotionKind.EyesFollow)) return;
        var c = _v.Root.Compositor;
        float to = 0;
        if (on)
        {
            var p = Native.CursorPos();
            to = p.X < Centre.X ? -LeanDegrees : LeanDegrees;
        }
        _v.Root.CenterPoint = PivotBottom;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, to);
        a.Duration = TimeSpan.FromMilliseconds(300);
        _v.Root.StartAnimation("RotationAngleInDegrees", a);
    }
}

internal static class AnimationExtensions
{
    /// <summary>Plays a keyframe animation once instead of looping.</summary>
    public static T Once<T>(this T a) where T : KeyFrameAnimation
    {
        a.IterationBehavior = AnimationIterationBehavior.Count;
        a.IterationCount = 1;
        return a;
    }
}
