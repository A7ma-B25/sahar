using System.Globalization;
using System.Numerics;
using Sahar.Core.Motion;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Her "soul" (spec §4), all on the compositor: one 25.2 s clock drives the float, follow-through, sway, leaf lag and
/// the ground shadow through expressions; a hop is one baked 1.2 s keyframe set (0.10a Ruling 9); the look is an
/// implicit animation on the eyes and on the lean container (Ruling 7); blinks are one random keyframe loop (Ruling 6).
/// The CPU only sets targets: pointer updates (≤ 30 Hz, only on change) and the odd hop.
/// </summary>
internal sealed class SoulLayer
{
    private const float Ease = 0.05f;   // look targets closer than this are not news
    private readonly SaharVisual _v;
    private readonly Compositor _c;
    private long _hopStart = long.MinValue / 2;
    private float _hopPower = 1f;
    private AnimationController? _floatClock, _hopClock;
    private double? _frozenFloat, _frozenHop;

    public SoulLayer(SaharVisual v)
    {
        _v = v;
        _c = v.Root.Compositor;
        Props = _c.CreatePropertySet();
        Props.InsertScalar("t", 0f);
        Props.InsertScalar("kick", 0f);
    }

    /// <summary>"t" (the 25.2 s clock) and "kick" (the leaf's kick after a hop).</summary>
    public CompositionPropertySet Props { get; }
    public bool Floating { get; private set; } = true;
    public bool Reduced { get; private set; }
    public int Hops { get; private set; }
    public bool Hopping => Environment.TickCount64 - _hopStart < SoulMath.HopSeconds * 1000;
    public Vector2 LookTarget { get; private set; }
    public float LeanTarget { get; private set; }

    /// <summary>Context for <see cref="SoulRules.CanHop"/>, set by the app.</summary>
    public Func<MotionKind> Motion { get; set; } = () => MotionKind.Hover;
    public Func<bool> Busy { get; set; } = () => false;
    public Func<bool> Still { get; set; } = () => false;
    public Func<bool> Grounded { get; set; } = () => false;

    /// <summary>Raised when a hop starts (the player stops a running squish: the hop is the squish).</summary>
    public event Action? HopStarted;

    /// <summary>(Re)builds every animation from the current pixel scale: after construction, a size or DPI change,
    /// and device loss. A hop in flight is cut to rest.</summary>
    public void Start()
    {
        float s = _v.Scale;
        double w = 2 * Math.PI / SoulMath.FloatPeriod, b = 2 * Math.PI / SoulMath.BreathPeriod;
        StopHop();

        // The clock.
        var clock = _c.CreateScalarKeyFrameAnimation();
        clock.InsertKeyFrame(0f, 0f);
        clock.InsertKeyFrame(1f, (float)SoulMath.LoopSeconds, _c.CreateLinearEasingFunction());
        clock.Duration = TimeSpan.FromSeconds(SoulMath.LoopSeconds);
        clock.IterationBehavior = AnimationIterationBehavior.Forever;
        _floatClock = _c.CreateAnimationController();
        Props.StartAnimation("t", clock, _floatClock);
        if (_frozenFloat is { } ft) { _floatClock.Pause(); _floatClock.Progress = (float)(ft / SoulMath.LoopSeconds); }

        // The leaf: lag behind the float, the hop's kick, and a little counter-lean (it reads the lean container).
        var leaf = Expr($"{F(SoulMath.LeafDeg)} * Sin(p.t * {F(w)} - {F(SoulMath.LeafLag)}) + p.kick - {F(SoulMath.LeafLeanDeg / SoulMath.LeanDeg)} * ln.RotationAngleInDegrees");
        leaf.SetReferenceParameter("ln", _v.Lean);
        _v.Leaf.StartAnimation("RotationAngleInDegrees", leaf);

        // The ground shadow follows her height (float + hop), in rig units.
        var h = $"Max(0, -(fl.Offset.Y + hp.Offset.Y) / {F(s)})";
        var shadowScale = Expr($"Vector3(Max(0, 62 - 0.5 * {h}) / 62, 1, 1)");
        var shadowOpacity = Expr($"Max(0, 0.16 - 0.002 * {h}) / 0.16");
        foreach (var e in new[] { shadowScale, shadowOpacity })
        {
            e.SetReferenceParameter("fl", _v.Float);
            e.SetReferenceParameter("hp", _v.Hop);
        }
        _v.ShadowVisual.StartAnimation("Scale", shadowScale);
        _v.ShadowVisual.StartAnimation("Opacity", shadowOpacity);

        if (Reduced) { RestFloat(); _v.EyesLayer.Container.StopAnimation("Scale"); _v.EyesLayer.Container.Scale = Vector3.One; }
        else
        {
            if (Floating) StartFloat(s, w, b); else RestFloat();
            StartBlink();
        }

        // The look: implicit, so a new target eases there from wherever she is, and nothing restarts per update.
        ApplyLookAnimations();
        _v.EyesLayer.Container.Offset = new Vector3(LookTarget * s, 0);
        _v.Lean.RotationAngleInDegrees = SoulMath.LeanDeg * LeanTarget;
    }

    private void StartFloat(float s, double w, double b)
    {
        _v.Float.StartAnimation("Offset.Y", Expr($"{F(-SoulMath.FloatUnits * s)} * Sin(p.t * {F(w)})"));
        var sy = $"(1 + {F(SoulMath.FollowAmp)} * Cos(p.t * {F(w)}) + {F(SoulMath.BreathAmp)} * Sin(p.t * {F(b)}))";
        _v.Float.StartAnimation("Scale", Expr($"Vector3(1 / {sy}, {sy}, 1)"));
        _v.Float.StartAnimation("RotationAngleInDegrees", Expr($"{F(SoulMath.SwayDeg)} * Sin(p.t * {F(w / 2)})"));
    }

    private void RestFloat()
    {
        foreach (var p in new[] { "Offset.Y", "Scale", "RotationAngleInDegrees" }) _v.Float.StopAnimation(p);
        _v.Float.Offset = Vector3.Zero;
        _v.Float.Scale = Vector3.One;
        _v.Float.RotationAngleInDegrees = 0;
    }

    /// <summary>Floating stops while a motion holds her still or she is grounded; she rests at her pose.</summary>
    public void SetFloating(bool on)
    {
        if (on == Floating) return;
        Floating = on;
        if (on && !Reduced)
        {
            double w = 2 * Math.PI / SoulMath.FloatPeriod, b = 2 * Math.PI / SoulMath.BreathPeriod;
            StartFloat(_v.Scale, w, b);
        }
        else RestFloat();
    }

    /// <summary>A hop, when she may (<see cref="SoulRules.CanHop"/>). Returns whether she hopped.</summary>
    public bool Hop(float power)
    {
        double since = (Environment.TickCount64 - _hopStart) / 1000.0;
        if (!SoulRules.CanHop(Motion(), Still(), Grounded(), Reduced, Busy(), since)) return false;
        _hopStart = Environment.TickCount64;
        _hopPower = power;
        Hops++;
        PlayHop(power, null);
        HopStarted?.Invoke();
        return true;
    }

    private void PlayHop(float power, double? pausedAt)
    {
        float s = _v.Scale;
        var keys = SoulMath.HopKeys(power);
        var linear = _c.CreateLinearEasingFunction();
        var rise = _c.CreateScalarKeyFrameAnimation();
        var scale = _c.CreateVector3KeyFrameAnimation();
        var tilt = _c.CreateScalarKeyFrameAnimation();
        var kick = _c.CreateScalarKeyFrameAnimation();
        foreach (var k in keys)
        {
            rise.InsertKeyFrame(k.Progress, k.Pose.RiseUnits * s, linear);
            scale.InsertKeyFrame(k.Progress, new Vector3(1 / k.Pose.ScaleY, k.Pose.ScaleY, 1), linear);
            tilt.InsertKeyFrame(k.Progress, k.Pose.TiltDeg, linear);
            kick.InsertKeyFrame(k.Progress, k.Kick, linear);
        }
        var d = TimeSpan.FromSeconds(SoulMath.HopSeconds);
        foreach (var a in new KeyFrameAnimation[] { rise, scale, tilt, kick }) a.Duration = d;
        _hopClock = _c.CreateAnimationController();
        _v.Hop.StartAnimation("Offset.Y", rise, _hopClock);
        _v.Hop.StartAnimation("Scale", scale, _hopClock);
        _v.Hop.StartAnimation("RotationAngleInDegrees", tilt, _hopClock);
        Props.StartAnimation("kick", kick, _hopClock);
        if (pausedAt is { } t) { _hopClock.Pause(); _hopClock.Progress = (float)(t / SoulMath.HopSeconds); }
    }

    private void StopHop()
    {
        foreach (var p in new[] { "Offset.Y", "Scale", "RotationAngleInDegrees" }) _v.Hop.StopAnimation(p);
        Props.StopAnimation("kick");
        _v.Hop.Offset = Vector3.Zero;
        _v.Hop.Scale = Vector3.One;
        _v.Hop.RotationAngleInDegrees = 0;
        Props.InsertScalar("kick", 0f);
        _hopStart = long.MinValue / 2;
        if (_frozenHop is { } t) PlayHop(_hopPower, t);
    }

    /// <summary>Where her eyes look (rig units) and how far she leans (−1…1). Only a real change sets anything.</summary>
    public void Look(Vector2 units, float lean)
    {
        if (Vector2.Distance(units, LookTarget) < Ease && MathF.Abs(lean - LeanTarget) < Ease / SoulMath.LeanDeg) return;
        LookTarget = units;
        LeanTarget = lean;
        _v.EyesLayer.Container.Offset = new Vector3(units * _v.Scale, 0);
        _v.Lean.RotationAngleInDegrees = SoulMath.LeanDeg * lean;
    }

    public void Look((Vector2 Units, float Lean) look) => Look(look.Units, look.Lean);

    /// <summary>Windows "Animation effects" off: no float, no blink, a running hop cut to rest, look instant, no hops.</summary>
    public void SetReducedMotion(bool on)
    {
        if (on == Reduced) return;
        Reduced = on;
        Start();
    }

    /// <summary>Test mode: pause the float clock at <paramref name="floatT"/> s and/or show the baked hop paused at
    /// <paramref name="hopT"/> s, so a screenshot shows the real composited pose; nulls resume.</summary>
    public void Freeze(double? floatT, double? hopT, float power = 1f)
    {
        _frozenFloat = floatT;
        _frozenHop = hopT;
        _hopPower = power;
        if (_floatClock != null)
        {
            if (floatT is { } ft) { _floatClock.Pause(); _floatClock.Progress = (float)(ft / SoulMath.LoopSeconds); }
            else _floatClock.Resume();
        }
        if (hopT is { } ht) PlayHop(power, ht);
        else StopHop();
    }

    public string Describe() => FormattableString.Invariant(
        $"floating={B(Floating)} hopping={B(Hopping)} reduced={B(Reduced)} hops={Hops} lookX={LookTarget.X:0.00} lookY={LookTarget.Y:0.00} lean={LeanTarget:0.00}");

    private void StartBlink()
    {
        var gaps = BlinkPattern.Gaps(Environment.TickCount);
        float length = gaps.Sum();
        var a = _c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0f, Vector3.One);
        float t = 0;
        foreach (var g in gaps)
        {
            t += g;
            float start = t - BlinkPattern.BlinkSeconds - 0.01f;   // the last blink ends just before the loop does
            a.InsertKeyFrame((start - 0.001f) / length, Vector3.One);
            a.InsertKeyFrame((start + BlinkPattern.BlinkSeconds / 2) / length, new Vector3(1, BlinkPattern.ClosedScale, 1));
            a.InsertKeyFrame((start + BlinkPattern.BlinkSeconds) / length, Vector3.One);
        }
        a.InsertKeyFrame(1f, Vector3.One);
        a.Duration = TimeSpan.FromSeconds(length);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        _v.EyesLayer.Container.StartAnimation("Scale", a);
    }

    private void ApplyLookAnimations()
    {
        if (Reduced)
        {
            _v.EyesLayer.Container.ImplicitAnimations = null;
            _v.Lean.ImplicitAnimations = null;
            return;
        }
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f));
        var d = TimeSpan.FromSeconds(SoulMath.LookEaseSeconds);
        var offset = _c.CreateVector3KeyFrameAnimation();
        offset.InsertExpressionKeyFrame(0f, "this.StartingValue");
        offset.InsertExpressionKeyFrame(1f, "this.FinalValue", ease);
        offset.Duration = d;
        offset.Target = "Offset";
        var eyes = _c.CreateImplicitAnimationCollection();
        eyes["Offset"] = offset;
        _v.EyesLayer.Container.ImplicitAnimations = eyes;
        var rot = _c.CreateScalarKeyFrameAnimation();
        rot.InsertExpressionKeyFrame(0f, "this.StartingValue");
        rot.InsertExpressionKeyFrame(1f, "this.FinalValue", ease);
        rot.Duration = d;
        rot.Target = "RotationAngleInDegrees";
        var lean = _c.CreateImplicitAnimationCollection();
        lean["RotationAngleInDegrees"] = rot;
        _v.Lean.ImplicitAnimations = lean;
    }

    private ExpressionAnimation Expr(string text)
    {
        var e = _c.CreateExpressionAnimation(text);
        e.SetReferenceParameter("p", Props);
        return e;
    }

    private static string F(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);
    private static string B(bool b) => b ? "true" : "false";
}
