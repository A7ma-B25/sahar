using System.Numerics;
using Sahar.Core.Expressions;
using Sahar.Core.Motion;
using Sahar.Engine.Host;
using Windows.UI.Composition;

namespace Sahar.Engine.Motion;

/// <summary>Motions 26–27: what she does when you shake her while dragging (0.8).</summary>
internal sealed partial class MotionController
{
    private readonly ShakeDetector _shake = new();

    /// <summary>Decides the reaction to a shake of this strength (the app knows how recently you shook her).</summary>
    public Func<int, ShakeReaction>? ShakeReact { get; set; }

    /// <summary>A shake ended in a reaction: what and how hard.</summary>
    public event Action<ShakeReaction, int>? Shaken;

    /// <summary>The app's sulk after an angry shake (her angry face stays while it lasts).</summary>
    public Func<bool> Sulking { get; set; } = () => false;

    /// <summary>The last reaction played (test diagnostics).</summary>
    public ShakeReaction LastReaction { get; private set; }

    private void FeedShake(double seconds, Vector2 cursor) => _shake.Add(seconds, cursor.X / Monitor.DpiScale);

    /// <summary>On release: a shake becomes a reaction instead of a throw.</summary>
    private bool ReactToShake()
    {
        if (!_shake.Shaken) return false;
        int strength = _shake.Strength;
        _shake.Reset();
        var r = ShakeReact?.Invoke(strength) ?? ShakeMood.Pick(strength, 0, _rng.NextDouble());
        if (r == ShakeReaction.None) return false;
        React(r, strength);
        return true;
    }

    /// <summary>Plays a reaction (also test mode's "shake" command).</summary>
    public void React(ShakeReaction r, int strength)
    {
        LastReaction = r;
        Shaken?.Invoke(r, strength);
        switch (r)
        {
            case ShakeReaction.Dizzy: Reset(MotionKind.Dizzy); StartDizzy(sick: false); break;
            case ShakeReaction.Sick: Reset(MotionKind.Dizzy); StartDizzy(sick: true); break;
            case ShakeReaction.Angry: Reset(MotionKind.Angry); StartAngry(); break;
        }
        UpdateTicker();
    }

    // ---------- 26 Dizzy (and sick) ----------

    private void StartDizzy(bool sick)
    {
        _express("dizzy", false);
        var c = _v.Root.Compositor;
        _v.Root.CenterPoint = PivotBodyCentre;
        var wobble = c.CreateScalarKeyFrameAnimation();
        float[] swings = [12, -11, 9, -7, 5, -3, 1.5f, 0];
        for (int i = 0; i < swings.Length; i++) wobble.InsertKeyFrame((i + 1f) / swings.Length, swings[i]);
        wobble.Duration = TimeSpan.FromSeconds(sick ? 1.4 : 3.2);
        _v.Root.StartAnimation("RotationAngleInDegrees", wobble);
        if (!sick)
        {
            Wait(3.5f, () => { _express("", true); Play(MotionKind.Hover); });
            return;
        }
        Wait(1.2f, () =>
        {
            _express("sick", false);
            // She leans forward (a squash toward the ground) and a cartoon drop falls from her mouth.
            var lean = c.CreateVector3KeyFrameAnimation();
            lean.InsertKeyFrame(0.3f, new Vector3(1.06f, 0.9f, 1));
            lean.InsertKeyFrame(0.8f, new Vector3(1.06f, 0.9f, 1));
            lean.InsertKeyFrame(1f, Vector3.One);
            lean.Duration = TimeSpan.FromSeconds(1.3);
            _v.Root.CenterPoint = PivotBottom;
            _v.Root.StartAnimation("Scale", lean);
            Drop();
            Wait(1.6f, () =>
            {
                _express("", true);
                Wait(0.2f, () =>
                {
                    _face(Eyes.Happy, Mouth.Wobble, 1.5f);   // a little embarrassed
                    Wait(1.5f, () => Play(MotionKind.Hover));
                });
            });
        });
    }

    /// <summary>A green cartoon drop from her mouth: falls 90 DIP, splashes flat and fades (0.9 s).</summary>
    private void Drop()
    {
        var c = _v.Root.Compositor;
        float s = _v.Scale, dpi = Monitor.DpiScale;
        var mouth = new Vector2(BodyOffset.X + BodySize.X * 0.5f, BodyOffset.Y + BodySize.Y * 0.68f);
        var geometry = c.CreateEllipseGeometry();
        geometry.Radius = new Vector2(7 * s, 9 * s);
        var shape = c.CreateSpriteShape(geometry);
        shape.FillBrush = c.CreateColorBrush(Windows.UI.Color.FromArgb(235, 0x7F, 0xC8, 0x4E));
        var drop = c.CreateShapeVisual();
        drop.Size = new Vector2(40 * s, 40 * s);
        shape.Offset = new Vector2(20 * s, 20 * s);
        drop.Shapes.Add(shape);
        drop.Offset = new Vector3(mouth - new Vector2(20 * s, 20 * s), 0);
        drop.CenterPoint = new Vector3(20 * s, 20 * s, 0);
        _v.Root.Children.InsertAtTop(drop);
        _rings.Add(drop);   // removed with her other little shapes when the motion ends

        var fall = c.CreateVector3KeyFrameAnimation();
        fall.InsertKeyFrame(0.65f, drop.Offset + new Vector3(0, 90 * dpi, 0), c.CreateCubicBezierEasingFunction(new(0.5f, 0), new(1, 1)));
        fall.InsertKeyFrame(1f, drop.Offset + new Vector3(0, 90 * dpi, 0));
        fall.Duration = TimeSpan.FromSeconds(0.9);
        drop.StartAnimation("Offset", fall);
        var splash = c.CreateVector3KeyFrameAnimation();
        splash.InsertKeyFrame(0.65f, new Vector3(0.8f, 1.2f, 1));
        splash.InsertKeyFrame(0.75f, new Vector3(1.8f, 0.35f, 1));
        splash.InsertKeyFrame(1f, new Vector3(2.2f, 0.25f, 1));
        splash.Duration = TimeSpan.FromSeconds(0.9);
        drop.StartAnimation("Scale", splash);
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0.7f, 1f);
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = TimeSpan.FromSeconds(1.4);
        drop.StartAnimation("Opacity", fade);
    }

    // ---------- 27 Angry ----------

    private void StartAngry()
    {
        _express("angry", false);
        var c = _v.Root.Compositor;
        // A huff: two puffs.
        var puff = c.CreateVector3KeyFrameAnimation();
        puff.InsertKeyFrame(0.2f, new Vector3(1.12f, 0.9f, 1));
        puff.InsertKeyFrame(0.4f, Vector3.One);
        puff.InsertKeyFrame(0.6f, new Vector3(1.12f, 0.9f, 1));
        puff.InsertKeyFrame(1f, Vector3.One);
        puff.Duration = TimeSpan.FromSeconds(0.9);
        _v.Root.CenterPoint = PivotBottom;
        _v.Root.StartAnimation("Scale", puff);
        Wait(0.9f, () =>
        {
            // She turns a little away from you and moves off.
            var p = Native.CursorPos();
            var away = Centre - new Vector2(p.X, p.Y);
            var dir = away.Length() < 1 ? Vector2.UnitX : Vector2.Normalize(away);
            _v.Root.CenterPoint = PivotBodyCentre;
            float tilt = dir.X >= 0 ? TiltDegrees : -TiltDegrees;
            _step = Travel(Clamp(TopLeft + dir * 150 * Monitor.DpiScale), 0.7f, EaseOut,
                frame: t => _v.Root.RotationAngleInDegrees = tilt * t,
                done: () => Wait(1.5f, () =>
                {
                    if (!Sulking()) _express("", true);   // played on its own (the show): back to her face
                    Play(MotionKind.Hover);
                }));
        });
    }

    /// <summary>Test mode: a drag shaken with <paramref name="swings"/> quick 160-DIP swings, then released where she is.</summary>
    public void TestShake(int swings)
    {
        BeginDrag();
        _step = null;   // she stays where she is (a real drag follows the cursor)
        float x = Centre.X / Monitor.DpiScale;
        double t = 0;
        for (int s = 0; s < swings; s++)
        {
            float from = s % 2 == 0 ? x : x + 160, to = s % 2 == 0 ? x + 160 : x;
            for (int i = 0; i <= 9; i++) { _shake.Add(t, from + (to - from) * i / 9f); t += 0.016; }
        }
        _lastDragVelocity = Vector2.Zero;
        EndDrag();
    }

    /// <summary>While she sulks the pointer coming close makes her drift away (not too often).</summary>
    public void DriftAway()
    {
        if (Current != MotionKind.Hover) return;
        var p = Native.CursorPos();
        var away = Centre - new Vector2(p.X, p.Y);
        var dir = away.Length() < 1 ? Vector2.UnitX : Vector2.Normalize(away);
        Reset(MotionKind.Hover);
        _eyes = false;
        _step = Travel(Clamp(TopLeft + dir * 120 * Monitor.DpiScale), 0.6f, EaseOut, done: () => Play(MotionKind.Hover));
        UpdateTicker();
    }
}
