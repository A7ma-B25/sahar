using System.Globalization;
using System.Numerics;
using Sahar.Core.Expressions;
using Sahar.Core.Motion;
using Windows.System;
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
    private double? _frozenFloat, _frozenHop;
    // One animation set per (power, pixel scale), built once and reused; cleared (and disposed) on Start (a rescale or
    // a device change), so hopping all day allocates nothing new.
    private readonly KeyedCache<(float Power, float Scale), HopSet> _hops;

    private sealed class HopSet(ScalarKeyFrameAnimation rise, Vector3KeyFrameAnimation scale, ScalarKeyFrameAnimation tilt,
        ScalarKeyFrameAnimation kick) : IDisposable
    {
        public ScalarKeyFrameAnimation Rise => rise;
        public Vector3KeyFrameAnimation Scale => scale;
        public ScalarKeyFrameAnimation Tilt => tilt;
        public ScalarKeyFrameAnimation Kick => kick;
        public void Dispose() { rise.Dispose(); scale.Dispose(); tilt.Dispose(); kick.Dispose(); }
    }

    /// <summary>Animation sets built so far (test diagnostics: stays small however often she hops).</summary>
    public int HopSetsBuilt { get; private set; }
    private readonly DispatcherQueueTimer? _happyTimer;

    public SoulLayer(SaharVisual v, DispatcherQueue? ui = null)
    {
        _v = v;
        _c = v.Root.Compositor;
        _hops = new KeyedCache<(float Power, float Scale), HopSet>(k => BuildHop(k.Power, k.Scale));
        if (ui != null)
        {
            _happyTimer = ui.CreateTimer();
            _happyTimer.IsRepeating = false;
            _happyTimer.Interval = TimeSpan.FromSeconds(SoulMath.HappySeconds);
            _happyTimer.Tick += (_, _) =>
            {
                try { EndHappy(); }
                catch (Exception e) { Sahar.Engine.Diagnostics.Log.Write($"soul error: {e.GetType().Name}"); }
            };
        }
        Props = _c.CreatePropertySet();
        Props.InsertScalar("t", 0f);
        Props.InsertScalar("kick", 0f);
        Props.InsertScalar("droop", 0f);   // 0.13: rain coming — her leaf droops a little, like an umbrella
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

    /// <summary>The face her expression wants now (read when the joy face ends, so the newest one wins).</summary>
    public Func<(Eyes Eyes, Mouth Mouth)> CurrentFace { get; set; } = () => (Eyes.Open, Mouth.Smile);
    public bool Happy { get; private set; }

    /// <summary>Her joy face (spec L4) for 1.1 s: up-arc eyes, a big smile, warmer blush; then her own face again.</summary>
    public void HappyFace()
    {
        Happy = true;
        _v.SetFace(Eyes.Joy, Mouth.Joy, ExpressionPlayer.Fade);
        _v.BlushLayer.Container.Opacity = 1f;
        _happyTimer?.Stop();
        _happyTimer?.Start();
    }

    /// <summary>Ends the joy face now (its time is up, or a new expression arrived: the newest face wins).</summary>
    public void EndHappy()
    {
        _happyTimer?.Stop();
        if (!Happy) return;
        Happy = false;
        _v.BlushLayer.Container.Opacity = SaharVisual.BlushRest;
        var (e, m) = CurrentFace();
        _v.SetFace(e, m, ExpressionPlayer.Fade);
    }

    /// <summary>A hop in flight eases to rest in this long when her menu opens (0.10.1: the owner's double-click).</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(120);

    /// <summary>Her menu opened: the joy face goes, and a hop in flight eases to rest (<see cref="SettleTime"/>, not a
    /// snap), so the buttons pop around her settled body.</summary>
    public void Settle()
    {
        EndHappy();
        if (!Hopping || _frozenHop is not null) return;
        _hopStart = long.MinValue / 2;
        if (Reduced) { StopHop(); return; }
        var ease = _c.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0f), new Vector2(0.2f, 1f));
        ScalarKeyFrameAnimation To(float v)
        {
            var a = _c.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(1f, v, ease);
            a.Duration = SettleTime;
            return a;
        }
        // Stop first: each property keeps the value it had reached, and the ease starts from there.
        foreach (var p in new[] { "Offset.Y", "Scale", "RotationAngleInDegrees" }) _v.Hop.StopAnimation(p);
        Props.StopAnimation("kick");
        var scale = _c.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, Vector3.One, ease);
        scale.Duration = SettleTime;
        _v.Hop.StartAnimation("Offset.Y", To(0f));
        _v.Hop.StartAnimation("Scale", scale);
        _v.Hop.StartAnimation("RotationAngleInDegrees", To(0f));
        Props.StartAnimation("kick", To(0f));
    }

    /// <summary>(Re)builds every animation from the current pixel scale: after construction, a size or DPI change,
    /// and device loss. A hop in flight is cut to rest.</summary>
    public void Start()
    {
        float s = _v.Scale;
        double w = 2 * Math.PI / SoulMath.FloatPeriod, b = 2 * Math.PI / SoulMath.BreathPeriod;
        StopHop();
        _hops.Clear();   // pixel values changed (or the device did): build hop animations afresh, dispose the old

        // The clock.
        StartClock();

        // The leaf: lag behind the float, the hop's kick, and a little counter-lean (it reads the lean container).
        var leaf = Expr($"{F(SoulMath.LeafDeg)} * Sin(p.t * {F(w)} - {F(SoulMath.LeafLag)}) + p.kick + p.droop - {F(SoulMath.LeafLeanDeg / SoulMath.LeanDeg)} * ln.RotationAngleInDegrees");
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

    /// <summary>The 25.2 s clock runs, or rests at a fixed time (reduced motion: 0; a test freeze: its time). The
    /// expressions read it either way, so a frozen frame is the real composited pose at that time.</summary>
    private void StartClock()
    {
        if (_frozenFloat is { } ft || Reduced)
        {
            Props.StopAnimation("t");
            Props.InsertScalar("t", (float)(_frozenFloat ?? 0));
            return;
        }
        var clock = _c.CreateScalarKeyFrameAnimation();
        clock.InsertKeyFrame(0f, 0f);
        clock.InsertKeyFrame(1f, (float)SoulMath.LoopSeconds, _c.CreateLinearEasingFunction());
        clock.Duration = TimeSpan.FromSeconds(SoulMath.LoopSeconds);
        clock.IterationBehavior = AnimationIterationBehavior.Forever;
        Props.StartAnimation("t", clock);
    }

    private void StartFloat(float s, double w, double b)
    {
        _v.Float.StartAnimation("Offset.Y", Expr($"{F(-SoulMath.FloatUnits * s)} * Sin(p.t * {F(w)})"));
        var sy = $"(1 + {F(SoulMath.FollowAmp)} * Cos(p.t * {F(w)}) + {F(SoulMath.BreathAmp)} * Sin(p.t * {F(b)}))";
        _v.Float.StartAnimation("Scale", Expr($"Vector3(1 / {sy}, {sy}, 1)"));
        _v.Float.StartAnimation("RotationAngleInDegrees", MusicSway
            ? Expr($"{F(MusicSwayDeg)} * Sin(p.t * {F(2 * Math.PI / MusicSwayPeriod)})")
            : Expr($"{F(SoulMath.SwayDeg)} * Sin(p.t * {F(w / 2)})"));
    }

    /// <summary>0.13 §1.5: with music she sways gently at a calm fixed rhythm (GSMTC gives no tempo): 2.1 s, so the
    /// 25.2 s clock holds 12 whole sways and never jumps; ±3°. Reduced motion: none.</summary>
    public const double MusicSwayPeriod = 2.1, MusicSwayDeg = 3;
    public bool MusicSway { get; private set; }

    public void SetMusicSway(bool on)
    {
        if (on == MusicSway) return;
        MusicSway = on;
        if (!Reduced && Floating) StartFloat(_v.Scale, 2 * Math.PI / SoulMath.FloatPeriod, 2 * Math.PI / SoulMath.BreathPeriod);
    }

    /// <summary>Rain coming: the leaf droops (degrees, eased); 0 lifts it again.</summary>
    public float Droop { get; private set; }

    public void SetDroop(float degrees)
    {
        if (degrees == Droop) return;
        Droop = degrees;
        if (Reduced) { Props.InsertScalar("droop", degrees); return; }
        var a = _c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(1f, degrees);
        a.Duration = TimeSpan.FromMilliseconds(900);
        Props.StartAnimation("droop", a);
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
        HappyFace();
        return true;
    }

    private void PlayHop(float power, double? pausedAt)
    {
        float s = _v.Scale;
        if (pausedAt is { } at)
        {
            // A frozen frame (test mode): the pose the baked keys hold at that time, set still.
            foreach (var p in new[] { "Offset.Y", "Scale", "RotationAngleInDegrees" }) _v.Hop.StopAnimation(p);
            Props.StopAnimation("kick");
            var pose = SoulMath.Hop(at, power);
            _v.Hop.Offset = new Vector3(0, pose.RiseUnits * s, 0);
            _v.Hop.Scale = new Vector3(1 / pose.ScaleY, pose.ScaleY, 1);
            _v.Hop.RotationAngleInDegrees = pose.TiltDeg;
            Props.InsertScalar("kick", SoulMath.LeafKick(at, power));
            return;
        }
        var set = _hops.Get((power, s));
        _v.Hop.StartAnimation("Offset.Y", set.Rise);
        _v.Hop.StartAnimation("Scale", set.Scale);
        _v.Hop.StartAnimation("RotationAngleInDegrees", set.Tilt);
        Props.StartAnimation("kick", set.Kick);
    }

    private HopSet BuildHop(float power, float s)
    {
        HopSetsBuilt++;
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
        return new HopSet(rise, scale, tilt, kick);
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

    /// <summary>Test mode: hold the float clock at <paramref name="floatT"/> s (the expressions still compute the
    /// pose) and/or hold the hop's pose at <paramref name="hopT"/> s, so a screenshot shows that instant; nulls resume.</summary>
    public void Freeze(double? floatT, double? hopT, float power = 1f)
    {
        _frozenFloat = floatT;
        _frozenHop = hopT;
        _hopPower = power;
        StartClock();
        if (hopT is { } ht) PlayHop(power, ht);
        else StopHop();
    }

    public string Describe() => FormattableString.Invariant(
        $"floating={B(Floating && !Reduced)} hopping={B(Hopping)} happy={B(Happy)} reduced={B(Reduced)} hops={Hops} hopSets={HopSetsBuilt} lookX={LookTarget.X:0.00} lookY={LookTarget.Y:0.00} lean={LeanTarget:0.00} sway={(MusicSway ? "music" : "soft")} droop={Droop:0}");

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
