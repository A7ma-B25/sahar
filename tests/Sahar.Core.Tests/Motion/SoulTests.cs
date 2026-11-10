using System.Numerics;
using Sahar.Core.Motion;

namespace Sahar.Core.Tests.Motion;

/// <summary>The soul's maths (spec §4; values checked against the approved SaharOriginal mockup).</summary>
public class SoulTests
{
    private const float Tol = 1e-3f;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1.05, -6)]
    [InlineData(3.15, 6)]
    public void FloatY_Values(double t, float y) => Assert.Equal(y, SoulMath.FloatY(t), Tol);

    [Theory]
    [InlineData(0, 1.012)]
    [InlineData(1.05, 1.01159)]
    [InlineData(2.1, 0.982)]
    public void ScaleY_Values(double t, float sy) => Assert.Equal(sy, SoulMath.ScaleY(t), Tol);

    [Theory]
    [InlineData(2.1, 1.6)]
    [InlineData(1.05, 1.1314)]
    public void Sway_Values(double t, float deg) => Assert.Equal(deg, SoulMath.Sway(t), Tol);

    [Theory]
    [InlineData(0, -4.70)]
    [InlineData(1.05, 3.7297)]
    public void Leaf_Values(double t, float deg) => Assert.Equal(deg, SoulMath.Leaf(t), 2e-3f);

    [Fact]
    public void Loop_IsSeamless()
    {
        double L = SoulMath.LoopSeconds;
        Assert.Equal(SoulMath.FloatY(0), SoulMath.FloatY(L), Tol);
        Assert.Equal(SoulMath.ScaleY(0), SoulMath.ScaleY(L), Tol);
        Assert.Equal(SoulMath.Sway(0), SoulMath.Sway(L), Tol);
        Assert.Equal(SoulMath.Leaf(0), SoulMath.Leaf(L), Tol);
    }

    [Theory]
    [InlineData(0.045, 1, 0, 0.92, 0)]
    [InlineData(0.29, 1, -38, 1.14, 3)]
    [InlineData(0.49, 1, 0, 0.80, 0)]
    [InlineData(1.2, 1, 0, 1, 0)]
    [InlineData(3.0, 1, 0, 1, 0)]
    [InlineData(0.29, 0.6, -22.8, 1.084, 3)]
    public void Hop_Phases(double j, float power, float rise, float sy, float tilt)
    {
        var p = SoulMath.Hop(j, power);
        Assert.Equal(rise, p.RiseUnits, Tol);
        Assert.Equal(sy, p.ScaleY, Tol);
        Assert.Equal(tilt, p.TiltDeg, Tol);
    }

    [Fact]
    public void Hop_LandingSettles()
    {
        Assert.Equal(1.0492f, SoulMath.Hop(0.69, 1).ScaleY, Tol);
        Assert.Equal(0.9997f, SoulMath.Hop(1.19, 1).ScaleY, Tol);
    }

    [Theory]
    [InlineData(0.1309, -5.9238)]
    [InlineData(0.5, 0.3781)]
    public void LeafKick_Values(double j, float deg) => Assert.Equal(deg, SoulMath.LeafKick(j, 1), Tol);

    [Fact]
    public void LeafKick_IsOverByTheEnd() => Assert.True(MathF.Abs(SoulMath.LeafKick(1.2, 1)) < 0.1f);

    [Fact]
    public void HopKeys_CoverTheWholeHop()
    {
        var keys = SoulMath.HopKeys(1f);
        Assert.Equal(73, keys.Count);
        Assert.Equal(0f, keys[0].Progress);
        Assert.Equal(1f, keys[^1].Progress);
        for (int i = 1; i < keys.Count; i++) Assert.True(keys[i].Progress > keys[i - 1].Progress);
        foreach (var k in new[] { keys[0], keys[^1] })
            Assert.Equal((0f, 1f, 0f, 0f), (k.Pose.RiseUnits, k.Pose.ScaleY, k.Pose.TiltDeg, k.Kick));
        var peak = keys.MinBy(k => MathF.Abs(k.Progress - 0.29f / 1.2f))!;
        Assert.Equal(-38f, peak.Pose.RiseUnits, 0.5f);
        foreach (var k in keys)
        {
            double j = k.Progress * SoulMath.HopSeconds;
            Assert.Equal(SoulMath.Hop(j, 1f), k.Pose);
            Assert.Equal(SoulMath.LeafKick(j, 1f), k.Kick);
        }
    }

    [Theory]
    [InlineData(0, 62, 0.16)]
    [InlineData(38, 43, 0.084)]
    [InlineData(200, 0, 0)]
    public void Shadow_Values(float h, float rx, float op)
    {
        Assert.Equal(rx, SoulMath.ShadowRx(h), Tol);
        Assert.Equal(op, SoulMath.ShadowOpacity(h), Tol);
    }

    [Fact]
    public void Look_Within400_FollowsTheCursor()
    {
        var (u, lean) = SoulMath.Look(new Vector2(600, 500), new Vector2(500, 500), 150, 50);
        Assert.Equal(2.133f, u.X, Tol);
        Assert.Equal(0f, u.Y, Tol);
        Assert.Equal(0.667f, lean, Tol);
    }

    [Fact]
    public void Look_ClampsAtTheEdge()
    {
        var (u, lean) = SoulMath.Look(new Vector2(1500, 500), new Vector2(500, 500), 150, 300);
        Assert.Equal((3.2f, 0f, 1f), (u.X, u.Y, lean));
    }

    [Fact]
    public void Look_FarAway_IsZero() =>
        Assert.Equal((Vector2.Zero, 0f), SoulMath.Look(new Vector2(600, 500), new Vector2(500, 500), 150, 401));

    [Fact]
    public void Look_AtTheEyes_IsZero()
    {
        var (u, lean) = SoulMath.Look(new Vector2(500, 500), new Vector2(500, 500), 150, 0);
        Assert.Equal((Vector2.Zero, 0f), (u, lean));
        Assert.False(float.IsNaN(u.X) || float.IsNaN(lean));
    }

    [Fact]
    public void Blink_GapsInRange_AndDeterministic()
    {
        var a = BlinkPattern.Gaps(1);
        Assert.Equal(8, a.Count);
        Assert.All(a, g => Assert.InRange(g, BlinkPattern.MinGap, BlinkPattern.MaxGap));
        Assert.Equal(a, BlinkPattern.Gaps(1));
        Assert.NotEqual(a, BlinkPattern.Gaps(2));
    }

    [Fact]
    public void SoulIdle_Next_InRange_AndBalanced()
    {
        var rng = new Random(7);
        var counts = new Dictionary<SoulIdleKind, int>();
        for (int i = 0; i < 10_000; i++)
        {
            var (wait, kind) = SoulIdle.Next(rng);
            Assert.InRange(wait.TotalSeconds, 6, 12);
            counts[kind] = counts.GetValueOrDefault(kind) + 1;
        }
        Assert.InRange(counts[SoulIdleKind.Hop] / 10_000.0, 0.45, 0.55);
        Assert.InRange(counts[SoulIdleKind.LookLeft] / 10_000.0, 0.2, 0.3);
        Assert.InRange(counts[SoulIdleKind.LookRight] / 10_000.0, 0.2, 0.3);
    }

    [Theory]
    [InlineData(MotionKind.Hover, true)]
    [InlineData(MotionKind.EyesFollow, true)]
    [InlineData(MotionKind.Drag, false)]
    [InlineData(MotionKind.Throw, false)]
    [InlineData(MotionKind.LandAndSit, false)]
    [InlineData(MotionKind.Game, false)]
    [InlineData(MotionKind.Dance, false)]
    public void SoulRules_CanHop_OnlyWhileResting(MotionKind kind, bool can) =>
        Assert.Equal(can, SoulRules.CanHop(kind, still: false, grounded: false, reduced: false, busy: false, secondsSinceLastHop: 10));

    [Fact]
    public void SoulRules_CanHop_NotWhenStillGroundedReducedOrBusy()
    {
        Assert.False(SoulRules.CanHop(MotionKind.Hover, still: true, grounded: false, reduced: false, busy: false, 10));
        Assert.False(SoulRules.CanHop(MotionKind.Hover, still: false, grounded: true, reduced: false, busy: false, 10));
        Assert.False(SoulRules.CanHop(MotionKind.Hover, still: false, grounded: false, reduced: true, busy: false, 10));
        Assert.False(SoulRules.CanHop(MotionKind.Hover, still: false, grounded: false, reduced: false, busy: true, 10));
    }

    [Fact]
    public void SoulRules_CanHop_AfterTheCooldown()
    {
        Assert.False(SoulRules.CanHop(MotionKind.Hover, false, false, false, false, 0.5));
        Assert.True(SoulRules.CanHop(MotionKind.Hover, false, false, false, false, 0.75));
    }
    // ---- Idle acts only when nothing else is going on (fresh review, 2026-11-10) ----

    [Fact]
    public void SoulIdle_MayAct_OnlyWhenRestingAndUndisturbed()
    {
        Assert.True(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.Hop, busy: false, quiet: false, typing: false, reduced: false));
        Assert.True(SoulIdle.MayAct(MotionKind.EyesFollow, SoulIdleKind.LookLeft, false, false, false, false));
        Assert.False(SoulIdle.MayAct(MotionKind.Wander, SoulIdleKind.Hop, false, false, false, false));
        Assert.False(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.Hop, busy: true, false, false, false));
        Assert.False(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.LookRight, false, quiet: true, false, false));
        Assert.False(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.Hop, false, false, typing: true, false));
        Assert.False(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.LookLeft, false, false, false, reduced: true));
        Assert.False(SoulIdle.MayAct(MotionKind.Hover, SoulIdleKind.Hop, false, false, false, reduced: true));
    }

    [Fact]
    public void KeyedCache_BuildsOncePerKey_AndDisposesOnClear()
    {
        int built = 0;
        var made = new List<Probe>();
        var cache = new KeyedCache<(float, float), Probe>(k => { built++; var p = new Probe(); made.Add(p); return p; });
        var a = cache.Get((1f, 0.5f));
        Assert.Same(a, cache.Get((1f, 0.5f)));
        cache.Get((0.6f, 0.5f));
        Assert.Equal((2, 2), (built, cache.Count));
        cache.Clear();
        Assert.All(made, p => Assert.True(p.Disposed));
        Assert.Equal(0, cache.Count);
        Assert.NotSame(a, cache.Get((1f, 0.5f)));
    }

    private sealed class Probe : IDisposable { public bool Disposed; public void Dispose() => Disposed = true; }
}
