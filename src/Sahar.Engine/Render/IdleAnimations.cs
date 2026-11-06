using System.Numerics;
using Windows.UI.Composition;

namespace Sahar.Engine.Render;

/// <summary>
/// Her idle life, all as compositor keyframe animations that loop forever (the engine sleeps meanwhile):
/// bob, hem flutter, blink, leaf sway, star spin and twinkle, moon orbit, shadow breathing, halo and bud glow.
/// Values come from the approved design (tools/art/design.py); distances are design units × scale.
/// </summary>
internal static class IdleAnimations
{
    public const float BobUnits = 10f;
    public const float OrbitRx = 112f, OrbitRy = 26f, OrbitTiltDegrees = -10f;
    private static readonly float[] TwinkleSeconds = [2.6f, 3.4f, 2.2f, 3f, 2.8f];

    public static void Start(SaharVisual v)
    {
        var c = v.Root.Compositor;
        float s = v.Scale;

        StartBob(v);

        // Hem flutter: the second body frame fades in and out over 2.2 s on top of the first.
        v.BodyB.Container.StartAnimation("Opacity", Loop(c, 2.2, (0f, 0f), (0.5f, 1f), (1f, 0f)));

        // Blink once every 6 s (92–100 % of the cycle).
        v.Face.Container.StartAnimation("Scale", Loop3(c, 6.0,
            (0f, Vector3.One), (0.92f, Vector3.One), (0.96f, new Vector3(1, 0.1f, 1)), (1f, Vector3.One)));

        // Leaf sways ±5° around the stem base over 5 s.
        v.Leaf.StartAnimation("RotationAngleInDegrees", Loop(c, 5.0, (0f, -5f), (0.5f, 5f), (1f, -5f)));

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
            v.StarVisuals[i].StartAnimation("Opacity", Loop(c, TwinkleSeconds[i], (0f, 0.3f), (0.5f, 1f), (1f, 0.3f)));

        StartOrbit(v, c, s);
    }

    /// <summary>Bob: the character floats up 10 units and back over 3.6 s; the shadow shrinks and fades in step.</summary>
    public static void StartBob(SaharVisual v)
    {
        var c = v.Root.Compositor;
        v.Character.StartAnimation("Offset.Y", Loop(c, 3.6, (0f, 0f), (0.5f, -BobUnits * v.Scale), (1f, 0f)));
        v.ShadowVisual.StartAnimation("Scale", Loop3(c, 3.6, (0f, Vector3.One), (0.5f, new Vector3(50f / 62f, 1, 1)), (1f, Vector3.One)));
        v.ShadowVisual.StartAnimation("Opacity", Loop(c, 3.6, (0f, 1f), (0.5f, 0.5f), (1f, 1f)));
    }

    /// <summary>
    /// The moon travels a tilted ellipse every 9 s, starting at the right and passing in front of her on the
    /// lower half (the front copy is visible for the first half of the cycle, the back copy for the second).
    /// </summary>
    private static void StartOrbit(SaharVisual v, Compositor c, float s)
    {
        var props = v.MoonProps;
        var t = c.CreateScalarKeyFrameAnimation();
        t.InsertKeyFrame(0f, 0f);
        t.InsertKeyFrame(1f, 1f, c.CreateLinearEasingFunction());
        t.Duration = TimeSpan.FromSeconds(9);
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
        props.StartAnimation("t", t);
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
