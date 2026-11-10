using System.Numerics;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Her original-look loops, all as compositor keyframe animations that loop forever (the engine sleeps meanwhile):
/// hem flutter, star spin and twinkle, the inner stars and dots, moon orbit (16 s a lap), halo and bud glow. Float,
/// blink, leaf sway and the shadow are the soul's (<see cref="SoulLayer"/>, 0.10a Ruling 5).
/// Values come from the approved design (tools/art/design.py); distances are design units × scale.
/// </summary>
internal static class IdleAnimations
{
    public const float OrbitRx = 112f, OrbitRy = 26f, OrbitTiltDegrees = -10f;
    /// <param name="reduced">Windows "Animation effects" off: no loops at all; everything rests (stars lit, moon beside her).</param>
    public static void Start(SaharVisual v, bool reduced = false)
    {
        var c = v.Root.Compositor;
        float s = v.Scale;
        var sky = v.Rig.Sky;

        if (reduced)
        {
            foreach (var x in new Visual[] { v.BodyB.Container, v.HaloLayer.Container, v.BudGlow.Container, v.Stars })
                foreach (var p in new[] { "Opacity", "RotationAngleInDegrees" }) x.StopAnimation(p);
            v.BodyB.Container.Opacity = 0;
            v.HaloLayer.Container.Opacity = 0.6f;
            v.BudGlow.Container.Opacity = 0.5f;
            for (int i = 0; i < v.StarVisuals.Length; i++) { v.StarVisuals[i].StopAnimation("Opacity"); v.StarVisuals[i].Opacity = 1; }
            for (int i = 0; i < v.InnerStarVisuals.Length; i++)
            {
                var k = sky.InnerStars[i].Scale;
                v.InnerStarVisuals[i].StopAnimation("Opacity"); v.InnerStarVisuals[i].StopAnimation("Scale");
                v.InnerStarVisuals[i].Opacity = 1; v.InnerStarVisuals[i].Scale = new Vector3(k, k, 1);
            }
            for (int i = 0; i < v.InnerDotVisuals.Length; i++) { v.InnerDotVisuals[i].StopAnimation("Opacity"); v.InnerDotVisuals[i].Opacity = 0.85f; }
            v.MoonProps.StopAnimation("t");
            v.MoonProps.InsertScalar("t", 0f);   // the moon rests at her right
            StartOrbit(v, c, s, still: true);
            return;
        }

        // Hem flutter: the second body frame fades in and out over 2.2 s on top of the first.
        v.BodyB.Container.StartAnimation("Opacity", Loop(c, 2.2, (0f, 0f), (0.5f, 1f), (1f, 0f)));

        // Halo and bud glow breathe.
        v.HaloLayer.Container.StartAnimation("Opacity", Loop(c, 4.0, (0f, 0.45f), (0.5f, 0.7f), (1f, 0.45f)));
        v.BudGlow.Container.StartAnimation("Opacity", Loop(c, 2.4, (0f, 0.25f), (0.5f, 0.7f), (1f, 0.25f)));

        // Stars: the ring of stars turns once every 40 s; each star twinkles on its own rhythm.
        var spin = c.CreateScalarKeyFrameAnimation();
        spin.InsertKeyFrame(0f, 0f);
        spin.InsertKeyFrame(1f, 360f, c.CreateLinearEasingFunction());
        spin.Duration = TimeSpan.FromSeconds(40);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        v.Stars.StartAnimation("RotationAngleInDegrees", spin);
        for (int i = 0; i < v.StarVisuals.Length; i++)
            v.StarVisuals[i].StartAnimation("Opacity", Loop(c, sky.OuterStars[i].Period, (0f, 0.3f), (0.5f, 1f), (1f, 0.3f)));

        // The night inside her (spec L3): six stars twinkle and swell, five dots glimmer, each on its own rhythm.
        for (int i = 0; i < v.InnerStarVisuals.Length; i++)
        {
            var spot = sky.InnerStars[i];
            float k = spot.Scale;
            v.InnerStarVisuals[i].StartAnimation("Opacity", Delayed(Loop(c, spot.Period, (0f, 0.15f), (0.5f, 1f), (1f, 0.15f)), spot.Begin));
            v.InnerStarVisuals[i].StartAnimation("Scale", Delayed(Loop3(c, spot.Period,
                (0f, new Vector3(0.6f * k, 0.6f * k, 1)), (0.5f, new Vector3(1.1f * k, 1.1f * k, 1)), (1f, new Vector3(0.6f * k, 0.6f * k, 1))), spot.Begin));
        }
        for (int i = 0; i < v.InnerDotVisuals.Length; i++)
        {
            var dot = sky.InnerDots[i];
            v.InnerDotVisuals[i].StartAnimation("Opacity", Delayed(Loop(c, dot.Period, (0f, 0.2f), (0.5f, 0.85f), (1f, 0.2f)), dot.Begin));
        }

        StartOrbit(v, c, s);
    }

    private static T Delayed<T>(T a, float seconds) where T : KeyFrameAnimation
    {
        a.DelayTime = TimeSpan.FromSeconds(seconds);
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        return a;
    }

    /// <summary>
    /// The moon travels a tilted ellipse every 16 s (spec §4), starting at the right and passing in front of her on the
    /// lower half (the front copy is visible for the first half of the cycle, the back copy for the second).
    /// </summary>
    private static void StartOrbit(SaharVisual v, Compositor c, float s, bool still = false)
    {
        var props = v.MoonProps;
        var t = c.CreateScalarKeyFrameAnimation();
        t.InsertKeyFrame(0f, 0f);
        t.InsertKeyFrame(1f, 1f, c.CreateLinearEasingFunction());
        t.Duration = TimeSpan.FromSeconds(Sahar.Core.Motion.SoulMath.MoonLapSeconds);
        t.IterationBehavior = AnimationIterationBehavior.Forever;

        float tilt = OrbitTiltDegrees * MathF.PI / 180f;
        var offset = c.CreateExpressionAnimation(
            "Vector3(cx + rx * Cos(p.t * 6.2831853) * ct - ry * Sin(p.t * 6.2831853) * st," +
            "        cy + rx * Cos(p.t * 6.2831853) * st + ry * Sin(p.t * 6.2831853) * ct, 0)");
        offset.SetReferenceParameter("p", props);
        offset.SetScalarParameter("cx", 150 * s);
        offset.SetScalarParameter("cy", 150 * s);
        offset.SetScalarParameter("rx", OrbitRx * s);
        offset.SetScalarParameter("ry", OrbitRy * s);
        offset.SetScalarParameter("ct", MathF.Cos(tilt));
        offset.SetScalarParameter("st", MathF.Sin(tilt));

        var front = c.CreateExpressionAnimation("p.t < 0.5 ? p.moon : 0");
        front.SetReferenceParameter("p", props);
        var back = c.CreateExpressionAnimation("p.t < 0.5 ? 0 : p.moon");
        back.SetReferenceParameter("p", props);

        v.MoonFront.Container.StartAnimation("Offset", offset);
        v.MoonBack.Container.StartAnimation("Offset", offset);
        v.MoonFront.Container.StartAnimation("Opacity", front);
        v.MoonBack.Container.StartAnimation("Opacity", back);
        if (!still) props.StartAnimation("t", t);
    }

    internal static ScalarKeyFrameAnimation Loop(Compositor c, double seconds, params (float At, float Value)[] frames)
    {
        var a = c.CreateScalarKeyFrameAnimation();
        foreach (var (at, value) in frames) a.InsertKeyFrame(at, value);
        a.Duration = TimeSpan.FromSeconds(seconds);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        return a;
    }

    internal static Vector3KeyFrameAnimation Loop3(Compositor c, double seconds, params (float At, Vector3 Value)[] frames)
    {
        var a = c.CreateVector3KeyFrameAnimation();
        foreach (var (at, value) in frames) a.InsertKeyFrame(at, value);
        a.Duration = TimeSpan.FromSeconds(seconds);
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        return a;
    }
}
