using System.Numerics;
using Sahar.Core.Games;
using Sahar.Core.Geometry;

namespace Sahar.Core.Tests.Games;

/// <summary>🏃 الحقني must be catchable: the owner's log (Nov 7–10) shows 0 catches in 5 of 6 games.</summary>
public class ChaseStaminaTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);
    private static readonly Vector2 Body = new(80, 77);

    [Fact]
    public void Rest_InRange_AndLongerAsSheTires()
    {
        var s = new ChaseStamina(new Random(3));
        var early = new List<float>();
        var late = new List<float>();
        for (int i = 0; i < 200; i++)
        {
            var e = s.Flee(1f, new Vector2(800, 500), new Vector2(840, 538), Body, Work, 1f);
            Assert.NotNull(e);
            Assert.InRange(e!.Rest, ChaseStamina.MinRest, ChaseStamina.MaxRest);
            early.Add(e.Rest);
            var l = s.Flee(ChaseStamina.TireSeconds, new Vector2(800, 500), new Vector2(840, 538), Body, Work, 1f);
            Assert.InRange(l!.Rest, ChaseStamina.MinRest, ChaseStamina.MaxRest);
            late.Add(l.Rest);
        }
        Assert.True(late.Average() > early.Average() + 0.4f, $"early {early.Average()} late {late.Average()}");
    }

    [Fact]
    public void Fatigue_ShrinksRadiusAndJump_AndSlowsHer()
    {
        var s = new ChaseStamina(new Random(1));
        Assert.Equal((1f, 1f, 0.28f), (s.NearScale(0), s.JumpScale(0), s.MoveSeconds(0)));
        Assert.Equal(0.55f, s.NearScale(ChaseStamina.TireSeconds), 3);
        Assert.Equal(0.5f, s.JumpScale(ChaseStamina.TireSeconds), 3);
        Assert.Equal(0.5f, s.MoveSeconds(ChaseStamina.TireSeconds), 3);
        Assert.Equal(0.5f, s.JumpScale(1000), 3);   // never past tired
    }

    [Fact]
    public void Resting_AfterAFlee_SheIgnoresAPointerThatIsNotOnHer()
    {
        var s = new ChaseStamina(new Random(1));
        var her = new Vector2(800, 500);
        var centre = her + Body / 2;
        var jump = s.Flee(10f, her, centre + new Vector2(-60, 0), Body, Work, 1f)!;
        float landed = 10f + jump.Seconds + 0.05f;
        Assert.True(s.Resting(landed));
        // Tired a little (10 s in), she lets the pointer come to 60 DIP while resting.
        Assert.Null(s.Flee(landed, jump.To, jump.To + Body / 2 + new Vector2(-60, 0), Body, Work, 1f));
        Assert.False(s.Resting(10f + jump.Seconds + jump.Rest + 0.01f));
    }

    [Fact]
    public void TypicalPlayer_CatchesHerWithin10To20Seconds()
    {
        var times = Enumerable.Range(0, 40).Select(seed => FirstCatch(seed, reaction: 0.45f, speed: 550f)).ToList();
        Assert.All(times, t => Assert.True(t is < 45f, "a typical player never caught her in 45 s"));
        var median = Median(times.Select(t => t!.Value));
        Assert.InRange(median, 10f, 20f);
    }

    [Fact]
    public void FastPlayer_DoesNotCatchHerAtOnce_SlowPlayerStillDoes()
    {
        Assert.True(Median(Enumerable.Range(0, 40).Select(s => FirstCatch(s, 0.25f, 1100f) ?? 45f)) >= 5f);
        Assert.All(Enumerable.Range(0, 40).Select(s => FirstCatch(s, 0.6f, 400f)), t => Assert.True(t is < 45f));
    }

    private static float Median(IEnumerable<float> xs)
    {
        var a = xs.OrderBy(x => x).ToArray();
        return a.Length % 2 == 1 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2;
    }

    /// <summary>The engine's chase tick (move → rest → flee) at 60 Hz against a simple human: the pointer heads for
    /// where she was <paramref name="reaction"/> s ago at <paramref name="speed"/> DIP/s; a click needs the pointer on
    /// her for 0.15 s. Returns the first catch time, or null.</summary>
    private static float? FirstCatch(int seed, float reaction, float speed)
    {
        const float dt = 1f / 60, clickNeeds = 0.15f;
        var stamina = new ChaseStamina(new Random(seed));
        var her = new Vector2(920, 480);
        var pointer = her + Body / 2 - new Vector2(300, 0);
        (Vector2 From, Vector2 To, float T, float Dur)? move = null;
        var seen = new List<(float At, Vector2 Centre)>();
        float clock = 0, onHer = 0;
        while (clock < 45f)
        {
            clock += dt;
            if (move is { } m)
            {
                float t = m.T + dt, k = MathF.Min(1, t / m.Dur);
                her = Vector2.Lerp(m.From, m.To, k);
                move = k >= 1 ? null : (m.From, m.To, t, m.Dur);
            }
            else if (stamina.Flee(clock, her, pointer, Body, Work, 1f) is { } jump)
                move = (her, jump.To, 0f, jump.Seconds);
            seen.Add((clock, her + Body / 2));
            var target = seen[0].Centre;
            for (int i = seen.Count - 1; i >= 0; i--)
                if (seen[i].At <= clock - reaction) { target = seen[i].Centre; break; }
            var d = target - pointer;
            float step = speed * dt;
            pointer = d.Length() <= step ? target : pointer + Vector2.Normalize(d) * step;
            bool on = Chase.Hit(new RectF(her.X, her.Y, Body.X, Body.Y), pointer);
            onHer = on ? onHer + dt : 0;
            if (onHer >= clickNeeds) return clock;
        }
        return null;
    }
}
