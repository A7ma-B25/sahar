using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Motion;

namespace Sahar.Core.Tests.Motion;

public class PhysicsTests
{
    private static readonly RectF Screen = new(0, 0, 1920, 1080);
    private static readonly Vector2 Size = new(80, 90);

    [Fact]
    public void Physics_Friction_SlowsToRest()
    {
        var b = new Body2D(new Vector2(900, 500), new Vector2(300, 0));
        bool resting = false;
        int steps = 0;
        while (!resting && steps < 1000) { (b, resting) = Physics.Step(b, 1 / 60f, Screen, Size); steps++; }
        Assert.True(resting);
        Assert.InRange(steps, 30, 300);
        Assert.True(b.Velocity.Length() < 20f);
    }

    [Fact]
    public void Physics_BouncesOffRightEdge()
    {
        var b = new Body2D(new Vector2(1920 - 80 - 1, 500), new Vector2(600, 0));
        var (next, _) = Physics.Step(b, 1 / 60f, Screen, Size);
        Assert.True(next.Velocity.X < 0);
        var friction = MathF.Exp(-2.2f / 60f);
        Assert.Equal(600 * friction * 0.55f, -next.Velocity.X, 0.5f);
        Assert.True(next.Position.X + Size.X <= 1920);
    }

    [Fact]
    public void Physics_ReclampsWhenBoundsShrink()
    {
        var b = new Body2D(new Vector2(1800, 500), Vector2.Zero);
        var (next, _) = Physics.Step(b, 1 / 60f, new RectF(0, 0, 1280, 720), Size);
        Assert.True(next.Position.X + Size.X <= 1280);
        Assert.True(next.Position.X >= 0);
        var c = Physics.Clamp(new Body2D(new Vector2(-50, 2000), Vector2.Zero), Screen, Size);
        Assert.Equal(new Vector2(0, 1080 - 90), c.Position);
    }
}

public class PendulumTests
{
    [Fact]
    public void Pendulum_SwingsOppositeToAcceleration_AndDamps()
    {
        var p = new Pendulum();
        for (int i = 0; i < 6; i++) p.Step(1 / 60f, pivotAccelX: 3000f);
        Assert.True(p.Angle < 0);
        Assert.True(p.Angle >= -25f);
        for (int i = 0; i < 600; i++) p.Step(1 / 60f, 0f);
        Assert.InRange(p.Angle, -0.5f, 0.5f);
    }

    [Fact]
    public void Pendulum_ClampsAt25Degrees()
    {
        var p = new Pendulum();
        for (int i = 0; i < 60; i++) p.Step(1 / 60f, -1_000_000f);
        Assert.Equal(25f, p.Angle, 0.001f);
    }
}

public class PerchTests
{
    [Fact]
    public void Perch_UsesTaskbarEdge_ForEachSide()
    {
        Assert.Equal(new Vector2(960, 1032), Perch.OnTaskbar(new RectF(0, 1032, 1920, 48), Edge.Bottom, 80, 0.5f));
        Assert.Equal(new Vector2(960, 48), Perch.OnTaskbar(new RectF(0, 0, 1920, 48), Edge.Top, 80, 0.5f));
        Assert.Equal(new Vector2(60, 540), Perch.OnTaskbar(new RectF(0, 0, 60, 1080), Edge.Left, 80, 0.5f));
        Assert.Equal(new Vector2(1860, 540), Perch.OnTaskbar(new RectF(1860, 0, 60, 1080), Edge.Right, 80, 0.5f));
        // fractions are clamped so the body stays on the bar
        Assert.Equal(new Vector2(40, 1032), Perch.OnTaskbar(new RectF(0, 1032, 1920, 48), Edge.Bottom, 80, 0f));
        Assert.Equal(new Vector2(1880, 1032), Perch.OnTaskbar(new RectF(0, 1032, 1920, 48), Edge.Bottom, 80, 2f));
    }

    [Fact]
    public void Perch_OnWindowTop_SitsOnTitleEdge() =>
        Assert.Equal(new Vector2(348, 100), Perch.OnWindowTop(new RectF(100, 100, 600, 400), 80, 0.4f));
}

public class WanderTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1032);
    private static readonly Vector2 Size = new(80, 90);

    [Fact]
    public void Wander_AvoidsRects()
    {
        var avoid = new[] { new RectF(0, 0, 1920, 600), new RectF(0, 600, 900, 432) };
        var w = new Wander(7);
        for (int i = 0; i < 200; i++)
        {
            var p = w.NextTarget(Work, Size, avoid);
            var r = new RectF(p.X, p.Y, Size.X, Size.Y);
            Assert.DoesNotContain(avoid, a => a.Intersects(r));
            Assert.True(p.X >= 0 && p.Y >= 0 && p.X + Size.X <= 1920 && p.Y + Size.Y <= 1032);
        }
    }

    [Fact]
    public void Wander_IsDeterministicForSeed()
    {
        var a = new Wander(42); var b = new Wander(42);
        for (int i = 0; i < 20; i++)
            Assert.Equal(a.NextTarget(Work, Size, []), b.NextTarget(Work, Size, []));
    }
}
