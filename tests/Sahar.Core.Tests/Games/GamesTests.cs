using System.Numerics;
using Sahar.Core.Games;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Games;

public class GamesTests
{
    private static readonly RectF Work = new(0, 0, 1920, 1040);
    private static readonly Vector2 Body = new(80, 77);

    [Fact]
    public void Menu_Play_OffersTheFourGamesAndARandomMove() =>
        Assert.Equal(["game-chase", "game-hide", "game-ball", "game-stars", "play-random"],
            MenuCatalog.Drill("play", new MenuState(false, false, false))!.Options.Select(i => i.Id));

    // ---- الحقني ----

    [Fact]
    public void Chase_FarPointer_StaysPut() =>
        Assert.Null(Chase.Flee(new Vector2(800, 500), new Vector2(200, 200), Body, Work, 1f, new Random(1)));

    [Fact]
    public void Chase_NearPointer_JumpsAwayInsideTheScreen()
    {
        var her = new Vector2(800, 500);
        var to = Chase.Flee(her, her + Body / 2 + new Vector2(-60, 0), Body, Work, 1f, new Random(1));
        Assert.NotNull(to);
        Assert.True(to!.Value.X > her.X + 100, $"fled to {to}");
        Assert.InRange(to.Value.X, Work.X, Work.Right - Body.X);
        Assert.InRange(to.Value.Y, Work.Y, Work.Bottom - Body.Y);
    }

    [Fact]
    public void Chase_Cornered_DashesAcross()
    {
        var her = new Vector2(Work.Right - Body.X, Work.Bottom - Body.Y);   // bottom-right corner
        var to = Chase.Flee(her, her + Body / 2 - new Vector2(60, 60), Body, Work, 1f, new Random(1));   // you come from the middle
        Assert.NotNull(to);
        Assert.True(Vector2.Distance(to!.Value, her) > 300, $"cornered, she only went to {to}");
    }

    [Fact]
    public void Chase_Hit_OnlyOnHerBody()
    {
        var her = new RectF(100, 100, 80, 77);
        Assert.True(Chase.Hit(her, new Vector2(140, 140)));
        Assert.False(Chase.Hit(her, new Vector2(300, 140)));
    }

    // ---- غمّضلي ----

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Hide_Spot_ShowsAQuarterOfHerAtAnEdge(int edge)
    {
        var p = HideSeek.Spot(Work, Body, edge, 0.5f);
        var visible = new RectF(p.X, p.Y, Body.X, Body.Y).Intersect(Work);
        float shown = visible.Width * visible.Height / (Body.X * Body.Y);
        Assert.InRange(shown, 0.2f, 0.35f);
    }

    [Fact]
    public void Hide_OnlyEdgesWithNoScreenBeyond_AndNotTheTaskbarEdge()
    {
        var left = new RectF(0, 0, 1920, 1080);
        var right = new RectF(1920, 0, 1920, 1080);
        // Left monitor: its right edge joins the other screen (her hidden part would show there); taskbar at the bottom.
        Assert.Equal([0, 1], HideSeek.OpenEdges(left, [left, right], taskbarEdge: 3));
        Assert.Equal([1, 2, 3], HideSeek.OpenEdges(right, [left, right], taskbarEdge: 0));
        Assert.Equal([0, 1, 2, 3], HideSeek.OpenEdges(left, [left], taskbarEdge: -1));
    }

    [Fact]
    public void Hide_HintAfter20s_GiveUpAfter60s()
    {
        Assert.False(HideSeek.Hint(19.9));
        Assert.True(HideSeek.Hint(20));
        Assert.False(HideSeek.GiveUp(59.9));
        Assert.True(HideSeek.GiveUp(60));
    }

    // ---- الطابة ----

    [Fact]
    public void Ball_Thrown_BouncesAndComesToRestOnTheFloor()
    {
        var b = new Sahar.Core.Motion.Body2D(new Vector2(300, 300), new Vector2(1500, -900));
        var size = new Vector2(28, 28);
        bool rest = false;
        float t = 0;
        while (!rest && t < 12)
        {
            (b, rest) = Ball.Step(b, 1 / 60f, Work, size);
            t += 1 / 60f;
            Assert.InRange(b.Position.X, Work.X, Work.Right - size.X);
            Assert.InRange(b.Position.Y, Work.Y, Work.Bottom - size.Y);
        }
        Assert.True(rest, "the ball never came to rest");
        Assert.Equal(Work.Bottom - size.Y, b.Position.Y, 1);
    }

    [Fact]
    public void Ball_Flick_VelocityFromTheLastMoment()
    {
        var v = Ball.Flick([(0.00, new Vector2(100, 100)), (0.05, new Vector2(120, 100)), (0.10, new Vector2(180, 90))]);
        Assert.Equal(new Vector2(800, -100), v);
        Assert.Equal(Vector2.Zero, Ball.Flick([(0.0, new Vector2(1, 1))]));
    }

    // ---- النجوم ----

    [Fact]
    public void Stars_FallAndAreCaughtByHer()
    {
        var g = new StarRain(Work, new Random(3));
        var her = new RectF(900, 900, 80, 77);
        int caught = 0;
        for (int i = 0; i < 60 * 40; i++)
        {
            g.Step(1 / 60f);
            foreach (var s in g.Stars) her = her with { X = s.Position.X - 40 };   // she always gets under one
            caught += g.Catch(her);
        }
        Assert.True(g.Spawned >= 40, $"spawned {g.Spawned}");
        Assert.True(caught > 0);
        Assert.Equal(caught, g.Caught);
        Assert.All(g.Stars, s => Assert.InRange(s.Position.Y, Work.Y - 40, Work.Bottom));
    }

    // ---- النتيجة ----

    [Theory]
    [InlineData(GameKind.Chase, 5, "مسكتني 5 مرات! 🏆")]
    [InlineData(GameKind.Chase, 1, "مسكتني مرة وحدة! 🏆")]
    [InlineData(GameKind.Chase, 0, "ولا مرة مسكتني 😜")]
    [InlineData(GameKind.Ball, 3, "جبتلك الطابة 3 مرات ⚽")]
    [InlineData(GameKind.Stars, 12, "لقطنا 12 نجمة ⭐")]
    [InlineData(GameKind.HideSeek, 14, "لقيتني بـ 14 ثانية بالمعدل 🏆")]
    public void Result_Lines(GameKind kind, int score, string line) => Assert.Equal(line, GameText.Result(kind, score));

    [Fact]
    public void Result_ToAWoman() => Assert.Equal("مسكتيني 5 مرات! 🏆", Address.For(Gender.Female, GameText.Result(GameKind.Chase, 5)));
}
