using System.Numerics;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Life;
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
internal sealed partial class MotionController
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
    private readonly Action<Eyes, Mouth, float> _face;   // a passing face (seconds), back to her expression after
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
    /// <summary>More places to keep away from when she wanders (e.g. where you are typing).</summary>
    public Func<IReadOnlyList<RectF>>? ExtraAvoid { get; set; }
    /// <summary>Her body in screen pixels.</summary>
    public RectF Body => new(TopLeft.X, TopLeft.Y, BodySize.X, BodySize.Y);
    public float DpiScale => _v.DpiScale;
    /// <summary>Raised when FullscreenHide has shrunk her away (the overlay can then be hidden).</summary>
    public event Action? HiddenAway;
    /// <summary>The user grabbed her (mouse or pipe).</summary>
    public event Action? DragStarted;
    /// <summary>Her goodbye (motion 19) has finished: she is gone.</summary>
    public event Action? GoodbyeDone;
    /// <summary>While she rests and the pointer moves: its distance to her body, in DIPs (for "shy", motion 17).</summary>
    public event Action<float>? PointerNear;
    private bool _throwNext;
    private Vector2? _home;
    /// <summary>Sitting or sleeping: no idle bob.</summary>
    public bool Still { get; private set; }

    public MotionController(SaharVisual v, ExpressionPlayer player, Ticker ticker, Action<Vector2> moveCanvasTo, Action<string, bool> express,
        Action<Eyes, Mouth, float> face)
    {
        _face = face;
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
            case MotionKind.PlayWithMoon: Reset(k); StartPlayWithMoon(); break;
            case MotionKind.Curious: Reset(k); StartCurious(); break;
            case MotionKind.Dance: Reset(k); StartDance(); break;
            case MotionKind.Breathe: Reset(k); StartBreathe(); break;
            case MotionKind.OrbitCursor: Reset(k); StartOrbitCursor(); break;
            case MotionKind.FollowCursor: Reset(k); StartFollowCursor(); break;
            case MotionKind.HideBehindWindow: Reset(k); StartHideBehindWindow(); break;
            case MotionKind.Shy: Reset(k); StartShy(); break;
            case MotionKind.StartupEntry: Reset(k); StartStartupEntry(); break;
            case MotionKind.Goodbye: Reset(k); StartGoodbye(); break;
            case MotionKind.HopIcons: Reset(k); StartHopIcons(); break;
            case MotionKind.Attention: Reset(k); StartAttention(); break;
            case MotionKind.NapOnIcon: Reset(k); StartCorner(sleep: true, along: 1f / 3); break;
            case MotionKind.Dizzy: Reset(k); StartDizzy(sick: false); break;
            case MotionKind.Angry: Reset(k); StartAngry(); break;
            default:
                if (!PlayLiving(k)) return false;
                break;
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
        _v.Character.StopAnimation("RotationAngleInDegrees");
        _v.Character.RotationAngleInDegrees = 0;
        _v.Face.Container.Offset = Vector3.Zero;
        RemoveRings();
        ResetLiving();
        if (BodyScaleBusy)
        {
            // An interrupted breath or shy shrink: stop it, and give the expression back its own body animations.
            BodyScaleBusy = false;
            _v.Character.StopAnimation("Scale");
            _v.Character.Scale = Vector3.One;
            _player.Reapply();
        }
        RestoreBob();
    }

    /// <summary>A motion owns her body's scale (Breathe, Shy) until it ends or is interrupted.</summary>
    public bool BodyScaleBusy { get; private set; }

    private readonly List<ShapeVisual> _rings = [];   // her little shapes (rings, a drop): removed when a motion ends

    private void RemoveRings()
    {
        foreach (var v in _rings) { _v.Root.Children.Remove(v); v.Dispose(); }
        _rings.Clear();
    }

    /// <summary>Flies smoothly to a spot (no jump), then hovers.</summary>
    public void FlyTo(Vector2 topLeft)
    {
        Reset(MotionKind.Wander);
        float secs = Math.Clamp(Vector2.Distance(topLeft, TopLeft) / (200 * Monitor.DpiScale), 0.4f, 1.5f);
        _step = Travel(Clamp(topLeft), secs, Smooth, done: () => Play(MotionKind.Hover));
        UpdateTicker();
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

    /// <summary>A quick giggle squish.</summary>
    public void Squish() => Squash(horizontal: false);

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
        RectF[] avoid = [new RectF(cursor.X - 80, cursor.Y - 80, 160, 160), .. ExtraAvoid?.Invoke() ?? []];
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
        if (PointerNear is { } near)
        {
            var b = Body;
            float dx = MathF.Max(0, MathF.Max(b.X - cursor.X, cursor.X - b.Right)), dy = MathF.Max(0, MathF.Max(b.Y - cursor.Y, cursor.Y - b.Bottom));
            near(MathF.Sqrt(dx * dx + dy * dy) / _v.DpiScale);
        }
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
        _shake.Reset();
        _shakeDpi = Monitor.DpiScale;
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
            FeedShake(_dragClock, cursor);
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
        if (ReactToShake()) return;   // shaken: dizzy, sick or angry instead of a throw
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

    private void StartCorner(bool sleep, float along = 1f)
    {
        if (!sleep) _home ??= TopLeft;
        HoldStill();
        var target = PerchTopLeft(along);
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

    private nint _perchHwnd;
    private RECT _perchRect;

    /// <summary>She sits on a window that has since moved, changed size or state, or closed.</summary>
    public unsafe bool PerchIsStale()
    {
        if (Current != MotionKind.PerchWindow || _perchHwnd == 0) return false;
        RECT r;
        if (Native.IsWindow(_perchHwnd) == 0 || Native.IsWindowVisible(_perchHwnd) == 0 || Native.IsIconic(_perchHwnd) != 0 ||
            Native.IsZoomed(_perchHwnd) != 0 || Native.GetWindowRect(_perchHwnd, &r) == 0) return true;
        return Math.Abs(r.Left - _perchRect.Left) > 4 || Math.Abs(r.Top - _perchRect.Top) > 4 || Math.Abs(r.Right - _perchRect.Right) > 4;
    }

    private unsafe Vector2? WindowPerch()
    {
        _perchHwnd = 0;
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
        (_perchHwnd, _perchRect) = (fg, r);
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

    // ---------- flights (motion 1 variations from the boards) ----------

    /// <summary>
    /// Flies one of the board patterns from where she is, mirrored (sideways and/or up-down) so the whole path fits
    /// on screen and she doesn't end under the pointer or where you type. False if it doesn't fit (unless forced:
    /// then the board's shape toward the side with more room, clamped).
    /// </summary>
    public bool PlayFlight(FlightPattern pattern, bool force = false)
    {
        var work = Monitor.WorkF;
        var cursor = Native.CursorPos();
        RectF[] avoid = [new RectF(cursor.X - 80, cursor.Y - 80, 160, 160), .. ExtraAvoid?.Invoke() ?? []];
        var plan = FlightPath.Plan(pattern, Body, work, Monitor.DpiScale, avoid);
        if (plan is null && !force) return false;
        var dir = plan ?? new Vector2(Centre.X > work.X + work.Width / 2 ? -1 : 1, 1);
        Reset(MotionKind.Wander);
        var start = TopLeft;
        float dip = Monitor.DpiScale, secs = FlightPath.Seconds(pattern), t = 0, tilt = 0;
        var last = start;
        _v.Root.CenterPoint = PivotBodyCentre;
        _step = dt =>
        {
            t = MathF.Min(1, t + dt / secs);
            var o = FlightPath.Offset(pattern, t, BodySize.X / dip) * dip;
            var next = Clamp(start + o * dir);
            float vx = (next.X - last.X) / MathF.Max(dt, 1e-3f);
            last = next;
            TopLeft = next;
            tilt += (Math.Clamp(vx / (300 * dip), -1, 1) * TiltDegrees - tilt) * MathF.Min(1, dt * 6);
            _v.Root.RotationAngleInDegrees = tilt;
            if (t < 1) return true;
            _step = null;
            Play(MotionKind.Hover);
            return _step != null;
        };
        UpdateTicker();
        return true;
    }

    // ---------- 15 PlayWithMoon, 16 Curious, 21 Dance, 22 Breathe, 24 OrbitCursor ----------

    /// <summary>She sways after her moon as it circles (follows the moon's own orbit phase, so nothing jumps).</summary>
    private void StartPlayWithMoon()
    {
        var c = _v.Root.Compositor;
        var follow = c.CreateExpressionAnimation("Cos(p.t * 6.2831853) * 12");
        follow.SetReferenceParameter("p", _v.MoonProps);
        _v.Character.StartAnimation("RotationAngleInDegrees", follow);
        _face(Eyes.Happy, Mouth.Grin, 4f);
        Wait(4f, () => Play(MotionKind.Hover));
    }

    /// <summary>Drifts a little toward the mouse, tilts her head, looks, then goes back.</summary>
    private void StartCurious()
    {
        var p = Native.CursorPos();
        var toward = new Vector2(p.X, p.Y) - Centre;
        var dir = toward.Length() < 1 ? Vector2.UnitX : Vector2.Normalize(toward);
        var home = TopLeft;
        var near = Clamp(home + dir * 30 * Monitor.DpiScale);
        _v.Root.CenterPoint = PivotBottom;
        var c = _v.Root.Compositor;
        var tilt = c.CreateScalarKeyFrameAnimation();
        tilt.InsertKeyFrame(0.15f, dir.X < 0 ? -10f : 10f);
        tilt.InsertKeyFrame(0.85f, dir.X < 0 ? -10f : 10f);
        tilt.InsertKeyFrame(1f, 0f);
        tilt.Duration = TimeSpan.FromSeconds(3.6);
        _v.Root.StartAnimation("RotationAngleInDegrees", tilt);
        _face(Eyes.Look, Mouth.O, 2.8f);
        _step = Travel(near, 0.6f, Smooth, done: () =>
            Wait(2.4f, () => _step = Travel(home, 0.6f, Smooth, done: () => Play(MotionKind.Hover))));
    }

    /// <summary>Sways and bounces to the beat while music plays (the life director stops her when it ends), at most 12 s.</summary>
    private void StartDance()
    {
        var c = _v.Root.Compositor;
        HoldStill();
        Still = false;
        float dip = Monitor.DpiScale;
        _v.Character.StartAnimation("RotationAngleInDegrees", IdleAnimations.Loop(c, 1.0, (0f, -8f), (0.5f, 8f), (1f, -8f)));
        _v.Character.StartAnimation("Offset.Y", IdleAnimations.Loop(c, 0.5, (0f, 0f), (0.5f, -6f * dip), (1f, 0f)));
        Wait(12f, () => Play(MotionKind.Hover));
    }

    /// <summary>Two slow breaths with her eyes half closed.</summary>
    private void StartBreathe()
    {
        var c = _v.Root.Compositor;
        _v.Character.StartAnimation("Scale", IdleAnimations.Loop3(c, 4.0,
            (0f, Vector3.One), (0.45f, new Vector3(1.06f, 1.06f, 1)), (1f, Vector3.One)));
        BodyScaleBusy = true;
        _face(Eyes.Half, Mouth.Smile, 8f);
        Wait(8f, () => Play(MotionKind.Hover));   // Reset ends the breath
    }

    /// <summary>One circle around the resting mouse pointer, then home.</summary>
    private void StartOrbitCursor()
    {
        var p = Native.CursorPos();
        var cursor = new Vector2(p.X, p.Y);
        var home = TopLeft;
        float r = 80 * Monitor.DpiScale;
        var half = BodySize / 2;
        var from = Centre - cursor;
        float a0 = from.Length() < 1 ? 0 : MathF.Atan2(from.Y, from.X);
        Vector2 At(float a) => cursor + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r - half;
        _step = Travel(Clamp(At(a0)), 0.5f, Smooth, done: () =>
        {
            float t = 0;
            _step = dt =>
            {
                t = MathF.Min(1, t + dt / 2f);
                TopLeft = Clamp(At(a0 + 2 * MathF.PI * Smooth(t)));
                if (t < 1) return true;
                _step = Travel(home, 0.5f, Smooth, done: () => Play(MotionKind.Hover));
                return true;
            };
        });
    }


    // ---------- 13 FollowCursor ----------

    /// <summary>Flies after the pointer, gently and a little behind it (never onto it), for 6 s.</summary>
    private void StartFollowCursor()
    {
        float dip = Monitor.DpiScale, t = 0, tilt = 0;
        _v.Root.CenterPoint = PivotBodyCentre;
        _face(Eyes.Happy, Mouth.Smile, 6f);
        _step = dt =>
        {
            t += dt;
            var p = Native.CursorPos();
            var cursor = new Vector2(p.X, p.Y);
            var away = Centre - cursor;
            var dir = away.Length() < 1 ? -Vector2.UnitX : Vector2.Normalize(away);
            var target = cursor + dir * 90 * dip - BodySize / 2;   // her centre stays 90 DIP behind the pointer
            var move = (target - TopLeft) * MathF.Min(1, dt * 2.5f);
            float max = 260 * dip * dt;
            if (move.Length() > max) move = Vector2.Normalize(move) * max;
            TopLeft = Clamp(TopLeft + move);
            tilt += (Math.Clamp(move.X / MathF.Max(dt, 1e-3f) / (300 * dip), -1, 1) * TiltDegrees - tilt) * MathF.Min(1, dt * 6);
            _v.Root.RotationAngleInDegrees = tilt;
            if (t < 6f) return true;
            _step = null;
            Play(MotionKind.Hover);
            return _step != null;
        };
    }

    // ---------- 14 HideBehindWindow ----------

    /// <summary>Slips behind the edge of the window in front (her far side clipped away) and peeks out twice.</summary>
    private unsafe void StartHideBehindWindow()
    {
        nint fg = Native.GetForegroundWindow();
        RECT r;
        if (fg == 0 || Win.Own.Contains(fg) || fg == Native.GetShellWindow() || Native.IsWindowVisible(fg) == 0 ||
            Native.IsIconic(fg) != 0 || Native.IsZoomed(fg) != 0 || Native.GetWindowRect(fg, &r) == 0 ||
            Native.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
        {
            Play(MotionKind.PeekTaskbar);   // no window to hide behind: the taskbar will do
            return;
        }
        var work = Monitor.WorkF;
        var size = BodySize;
        bool fromLeft = r.Left - work.X >= size.X || r.Right + size.X > work.Right;   // she stands left of the window's left edge
        float edgeX = fromLeft ? r.Left : r.Right;
        float y = Math.Clamp(r.Top + (r.Bottom - r.Top) * 0.4f - size.Y / 2, work.Y, work.Bottom - size.Y);
        float sign = fromLeft ? -1 : 1;   // outward
        Vector2 At(float outFraction) => new(fromLeft ? edgeX - size.X * outFraction : edgeX - size.X * (1 - outFraction), y);
        var outside = Clamp(At(1.15f));
        var hidden = At(0.15f);
        var peek = At(0.55f);
        var clip = _v.Root.Compositor.CreateInsetClip();
        void Clip()
        {
            var p = _v.Position;
            float canvasW = _v.CanvasPx.Width;
            clip.LeftInset = clip.RightInset = 0;
            if (fromLeft) clip.RightInset = MathF.Max(0, p.X + canvasW - edgeX);
            else clip.LeftInset = MathF.Max(0, edgeX - p.X);
        }
        _v.Root.Clip = clip;
        Clip();
        _v.Root.CenterPoint = PivotBodyCentre;
        _face(Eyes.Look, Mouth.Smile, 7f);
        _v.Face.Container.Offset = new Vector3(sign * 3 * _v.Scale, 0, 0);
        _step = Travel(hidden, 1.2f, Smooth, _ => Clip(), done: () =>
            _step = Travel(peek, 0.5f, EaseOut, _ => Clip(), done: () =>
                Wait(1f, () => _step = Travel(hidden, 0.5f, EaseIn, _ => Clip(), done: () =>
                    Wait(0.6f, () => _step = Travel(peek, 0.5f, EaseOut, _ => Clip(), done: () =>
                        Wait(1f, () => _step = Travel(outside, 1f, Smooth, _ => Clip(), done: () =>
                        {
                            _v.Root.Clip = null;
                            Play(MotionKind.Hover);
                        }))))))));
    }

    // ---------- 17 Shy ----------

    /// <summary>The pointer came too close too often: she blushes, shrinks a little and drifts away from it.</summary>
    private void StartShy()
    {
        var p = Native.CursorPos();
        var away = Centre - new Vector2(p.X, p.Y);
        var dir = away.Length() < 1 ? Vector2.UnitX : Vector2.Normalize(away);
        var c = _v.Root.Compositor;
        var shrink = c.CreateVector3KeyFrameAnimation();
        shrink.InsertKeyFrame(0.2f, new Vector3(0.94f, 0.94f, 1));
        shrink.InsertKeyFrame(0.8f, new Vector3(0.94f, 0.94f, 1));
        shrink.InsertKeyFrame(1f, Vector3.One);
        shrink.Duration = TimeSpan.FromSeconds(2.5);
        _v.Character.StartAnimation("Scale", shrink);
        BodyScaleBusy = true;
        _face(Eyes.Half, Mouth.Smile, 2.5f);
        _step = Travel(Clamp(TopLeft + dir * 120 * Monitor.DpiScale), 0.8f, EaseOut,
            done: () => Wait(1.7f, () => { BodyScaleBusy = false; Play(MotionKind.Hover); }));
    }

    // ---------- 18 StartupEntry ----------

    /// <summary>The PC just started: she flies in from the nearest side of the screen and waves hello.</summary>
    private void StartStartupEntry()
    {
        var home = TopLeft;
        var work = Monitor.WorkF;
        bool fromRight = Centre.X > work.X + work.Width / 2;
        TopLeft = new Vector2(fromRight ? work.Right + 10 : work.X - BodySize.X - 10, home.Y);
        _v.Root.CenterPoint = PivotBodyCentre;
        _v.Root.RotationAngleInDegrees = fromRight ? -TiltDegrees : TiltDegrees;
        _step = Travel(home, 1.6f, EaseOut, frame: t => _v.Root.RotationAngleInDegrees = (fromRight ? -TiltDegrees : TiltDegrees) * (1 - t),
            done: () =>
            {
                Wave(10f, 1.2f);
                _face(Eyes.Happy, Mouth.Grin, 2f);
                Wait(1.3f, () => Play(MotionKind.Hover));
            });
    }

    private void Wave(float degrees, float seconds)
    {
        var c = _v.Root.Compositor;
        var wave = c.CreateScalarKeyFrameAnimation();
        wave.InsertKeyFrame(0.25f, degrees);
        wave.InsertKeyFrame(0.5f, -degrees);
        wave.InsertKeyFrame(0.75f, degrees);
        wave.InsertKeyFrame(1f, 0f);
        wave.Duration = TimeSpan.FromSeconds(seconds);
        _v.Character.CenterPoint = new Vector3(BodyOffset.X + BodySize.X / 2, BodyOffset.Y + BodySize.Y, 0);
        _v.Character.StartAnimation("RotationAngleInDegrees", wave);
    }

    // ---------- 19 Goodbye ----------

    /// <summary>Waves with her leaf, then turns into small stars and is gone (raises <see cref="GoodbyeDone"/>).</summary>
    private void StartGoodbye()
    {
        HoldStill();
        Wave(12f, 1f);
        _face(Eyes.Happy, Mouth.Smile, 2f);
        Wait(1f, () =>
        {
            var c = _v.Root.Compositor;
            _v.Root.CenterPoint = PivotBodyCentre;
            var shrink = c.CreateVector3KeyFrameAnimation();
            shrink.InsertKeyFrame(1f, Vector3.Zero);
            shrink.Duration = TimeSpan.FromMilliseconds(800);
            var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
            _v.Root.StartAnimation("Scale", shrink);
            var sparkles = Sparkles(c);
            batch.End();
            batch.Completed += (_, _) =>
            {
                foreach (var v in sparkles) { _v.Host.Root.Children.Remove(v); v.Dispose(); }
                if (Current == MotionKind.Goodbye) GoodbyeDone?.Invoke();
            };
        });
    }

    /// <summary>Eight small stars flying out of her body and fading (overlay coordinates, so they don't shrink with her).</summary>
    private List<ShapeVisual> Sparkles(Compositor c)
    {
        var list = new List<ShapeVisual>();
        float dip = Monitor.DpiScale, r = 3 * dip;
        var origin = Centre - _v.Origin;
        for (int i = 0; i < 8; i++)
        {
            var g = c.CreateEllipseGeometry();
            g.Radius = new Vector2(r);
            g.Center = new Vector2(r);
            var shape = c.CreateSpriteShape(g);
            shape.FillBrush = c.CreateColorBrush(i % 2 == 0 ? Windows.UI.Color.FromArgb(255, 0xFF, 0xD9, 0x8A) : Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF));
            var v = c.CreateShapeVisual();
            v.Size = new Vector2(2 * r);
            v.Shapes.Add(shape);
            v.Offset = new Vector3(origin - new Vector2(r), 0);
            _v.Host.Root.Children.InsertAtTop(v);
            float a = i * MathF.PI / 4;
            var fly = c.CreateVector3KeyFrameAnimation();
            fly.InsertKeyFrame(1f, new Vector3(origin + new Vector2(MathF.Cos(a), MathF.Sin(a)) * 60 * dip - new Vector2(r), 0));
            fly.Duration = TimeSpan.FromMilliseconds(800);
            v.StartAnimation("Offset", fly);
            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, 0f);
            fade.Duration = TimeSpan.FromMilliseconds(800);
            v.StartAnimation("Opacity", fade);
            list.Add(v);
        }
        return list;
    }

    // ---------- 20 HopIcons ----------

    /// <summary>Four light hops along the taskbar toward the middle, squishing on each landing.</summary>
    private void StartHopIcons()
    {
        var m = Monitor;
        var (bar, edge, _) = Taskbar.Get(m.Handle);
        float dip = m.DpiScale, along = Math.Clamp(AlongBar(), 0.1f, 0.9f);
        var start = PerchTopLeft(along);
        bool horizontal = edge is not (Edge.Left or Edge.Right);
        var work = m.WorkF;
        float toward = horizontal
            ? (start.X + BodySize.X / 2 < work.X + work.Width / 2 ? 1 : -1)
            : (start.Y + BodySize.Y / 2 < work.Y + work.Height / 2 ? 1 : -1);
        var step = (horizontal ? new Vector2(48, 0) : new Vector2(0, 48)) * dip * toward;
        var up = edge switch { Edge.Top => Vector2.UnitY, Edge.Left => Vector2.UnitX, Edge.Right => -Vector2.UnitX, _ => -Vector2.UnitY } * 24 * dip;
        HoldStill();
        int hop = 0;
        void Next()
        {
            if (hop == 4) { Play(MotionKind.Hover); return; }
            var from = TopLeft;
            var to = Clamp(start + step * (hop + 1));
            hop++;
            float t = 0;
            _step = dt =>
            {
                t = MathF.Min(1, t + dt / 0.45f);
                TopLeft = Vector2.Lerp(from, to, t) + up * MathF.Sin(MathF.PI * t);
                if (t < 1) return true;
                Squash(horizontal: false);
                Wait(0.15f, Next);
                return true;
            };
        }
        _step = Travel(start, 0.8f, EaseIn, done: Next);
    }

    // ---------- 23 Attention ----------

    /// <summary>For something that needs you: she comes a little toward the middle and sends out three soft rings of light.</summary>
    private void StartAttention()
    {
        var home = TopLeft;
        var work = Monitor.WorkF;
        var middle = new Vector2(work.X + work.Width / 2, work.Y + work.Height / 2) - BodySize / 2;
        var near = Clamp(Vector2.Lerp(home, middle, 0.25f));
        var c = _v.Root.Compositor;
        var centre = BodyOffset + BodySize / 2;
        float radius = BodySize.X / 2;
        var rings = _rings;
        for (int i = 0; i < 3; i++)
        {
            var g = c.CreateEllipseGeometry();
            g.Center = centre;
            g.Radius = new Vector2(radius);
            var shape = c.CreateSpriteShape(g);
            shape.StrokeBrush = c.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0xB9, 0xB0, 0xFF));
            shape.StrokeThickness = 3 * Monitor.DpiScale;
            var v = c.CreateShapeVisual();
            v.Size = new Vector2(_v.CanvasPx.Width, _v.CanvasPx.Height);
            v.CenterPoint = new Vector3(centre, 0);
            v.Shapes.Add(shape);
            v.Opacity = 0;
            _v.Root.Children.InsertAtBottom(v);
            var grow = c.CreateVector3KeyFrameAnimation();
            grow.InsertKeyFrame(0f, Vector3.One);
            grow.InsertKeyFrame(1f, new Vector3(2.2f, 2.2f, 1));
            grow.Duration = TimeSpan.FromMilliseconds(900);
            grow.DelayTime = TimeSpan.FromMilliseconds(600 + 300 * i);
            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0.6f);
            fade.InsertKeyFrame(1f, 0f);
            fade.Duration = TimeSpan.FromMilliseconds(900);
            fade.DelayTime = grow.DelayTime;
            v.StartAnimation("Scale", grow);
            v.StartAnimation("Opacity", fade);
            rings.Add(v);
        }
        _face(Eyes.Open, Mouth.O, 3f);
        _step = Travel(near, 0.6f, Smooth, done: () =>
            Wait(2.2f, () => { RemoveRings(); _step = Travel(home, 0.6f, Smooth, done: () => Play(MotionKind.Hover)); }));
    }

    /// <summary>Runs <paramref name="then"/> after a pause (on the motion clock).</summary>
    private void Wait(float seconds, Action then)
    {
        float t = 0;
        _step = dt =>
        {
            t += dt;
            if (t < seconds) return true;
            _step = null;
            then();
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
