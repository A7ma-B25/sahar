using System.Numerics;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Menu;

public class MenuCardLayoutTests
{
    private static readonly IReadOnlySet<string> All = MenuCatalog.Visible(null, _ => true);
    private static MenuView Main(IReadOnlySet<string>? visible = null, bool mic = true, bool settings = true, bool fact = true) =>
        new("main", visible ?? All, mic, settings, fact, null);
    private static float Cx(RectF r) => r.X + r.Width / 2;

    [Fact]
    public void Layout_Everything()
    {
        var l = MenuCardLayout.Layout(Main());
        Assert.Equal(296, l.Width);
        Assert.Equal(new RectF(8, 8, 280, 84), l.Rects["header"]);
        Assert.Equal(new RectF(8, 100, 280, 44), l.Rects["chatRow"]);
        var (chat, mic, send) = (l.Rects["chat"], l.Rects["mic"], l.Rects["send"]);
        Assert.Equal((36f, 36f), (mic.Width, mic.Height));
        Assert.Equal((36f, 36f), (send.Width, send.Height));
        Assert.True(chat.X > mic.X && mic.X > send.X);   // RTL: the field at the start (right), then mic, then send
        string[] tiles = ["play", "dnd", "hide"];
        Assert.All(tiles, t => Assert.Equal(86, l.Rects[t].Height));
        Assert.Equal(l.Rects["play"].Width, l.Rects["hide"].Width);
        Assert.Equal(8, l.Rects["play"].X - l.Rects["dnd"].Right, 3);
        Assert.True(Cx(l.Rects["play"]) > Cx(l.Rects["dnd"]) && Cx(l.Rects["dnd"]) > Cx(l.Rects["hide"]));
        string[] tools = ["timer", "note", "listen", "clipboard"];
        Assert.All(tools, t => Assert.Equal(58, l.Rects[t].Height));
        Assert.Equal(6, l.Rects["timer"].X - l.Rects["note"].Right, 3);
        Assert.True(Cx(l.Rects["timer"]) > Cx(l.Rects["clipboard"]));
        Assert.Equal(36, l.Rects["settings"].Height);
        Assert.True(Cx(l.Rects["settings"]) > 148);   // at the start
        Assert.Equal("fact,chat,mic,send,play,dnd,hide,timer,note,listen,clipboard,settings", string.Join(',', l.FocusOrder));
        Assert.Equal(5, l.Rows.Count);
        Assert.Equal("play,dnd,hide", string.Join(',', l.Rows[2]));
        Assert.True(l.Rects["header"].Bottom <= l.Rects["chatRow"].Y && l.Rects["chatRow"].Bottom <= l.Rects["play"].Y
            && l.Rects["play"].Bottom <= l.Rects["timer"].Y && l.Rects["timer"].Bottom <= l.Rects["settings"].Y);
        Assert.Equal(l.Height, l.Rects["footer"].Bottom + 8);
    }

    [Fact]
    public void Layout_LTR_Mirrors()
    {
        var l = MenuCardLayout.Layout(Main(), rtl: false);
        Assert.True(Cx(l.Rects["play"]) < Cx(l.Rects["hide"]));
        Assert.True(l.Rects["chat"].X < l.Rects["send"].X);
    }

    [Fact]
    public void Layout_NoMic_TheFieldTakesItsPlace()
    {
        var l = MenuCardLayout.Layout(Main(mic: false));
        Assert.False(l.Rects.ContainsKey("mic"));
        Assert.DoesNotContain("mic", l.FocusOrder);
        Assert.Equal(4, l.Rects["chat"].X - l.Rects["send"].Right, 3);
    }

    [Fact]
    public void Layout_TwoTiles_ShareTheRow()
    {
        var l = MenuCardLayout.Layout(Main(new HashSet<string> { "chat", "play", "hide", "timer" }));
        Assert.Equal((280 - 8) / 2f, l.Rects["play"].Width, 3);
        Assert.False(l.Rects.ContainsKey("dnd"));
    }

    [Fact]
    public void Layout_NoTiles_NoRow()
    {
        var full = MenuCardLayout.Layout(Main());
        var l = MenuCardLayout.Layout(Main(new HashSet<string> { "chat", "timer", "note", "listen", "clipboard" }));
        Assert.Equal(full.Height - 100, l.Height, 3);
    }

    [Fact]
    public void Layout_NothingVisible_HeaderAndHintOnly()
    {
        var l = MenuCardLayout.Layout(Main(new HashSet<string>(), mic: false, settings: false, fact: false));
        Assert.Empty(l.FocusOrder);
        Assert.Equal(8 + 84 + 8 + 36 + 8, l.Height);
        Assert.True(l.Rects.ContainsKey("hint"));
    }

    [Fact]
    public void Layout_Drill_HasBackAndOptions()
    {
        var drill = MenuCatalog.Drill("dnd", new MenuState(false, false, false));
        var l = MenuCardLayout.Layout(new MenuView("dnd", All, true, true, true, drill));
        var back = l.Rects["back"];
        Assert.Equal((32f, 32f), (back.Width, back.Height));
        Assert.True(Cx(back) > 148);   // at the start (right in RTL)
        string[] ids = ["dnd-30", "dnd-60", "dnd-120", "dnd-on"];
        Assert.All(ids, id => Assert.Equal(l.Rects["dnd-30"].Width, l.Rects[id].Width, 3));
        Assert.Equal(36, l.Rects["track"].Height);
        Assert.True(Cx(l.Rects["dnd-30"]) > Cx(l.Rects["dnd-on"]));
        Assert.Equal("back,dnd-30,dnd-60,dnd-120,dnd-on", string.Join(',', l.FocusOrder));
        Assert.True(l.Rects.ContainsKey("note-text"));
        Assert.False(l.Rects.ContainsKey("play"));
        Assert.False(l.Rects.ContainsKey("footer"));
    }

    [Fact]
    public void Layout_TimerDrill_TheFifthOptionIsARow()
    {
        var drill = MenuCatalog.Drill("timer", new MenuState(false, false, false));
        var l = MenuCardLayout.Layout(new MenuView("timer", All, false, false, false, drill));
        var custom = l.Rects["timer-custom"];
        Assert.Equal(44, custom.Height);
        Assert.True(custom.Y > l.Rects["timer-5"].Bottom);
        Assert.Equal(l.Rects["track"].Width, custom.Width, 3);
    }

    // ---- Placement (px) ----
    private static readonly RectF Work = new(0, 0, 1920, 1040);
    private static readonly Vector2 Card = new(296, 354);

    [Fact]
    public void Place_Beside_PicksTheRoomierSide()
    {
        var body = new RectF(1700, 500, 80, 77);
        var p = MenuCardLayout.Place(body, Work, Card, MenuPlace.Beside, 1);
        Assert.Equal(body.X - 12, p.Card.Right, 3);
        Assert.Equal(Corner.TopRight, p.Near);
        var q = MenuCardLayout.Place(new RectF(100, 500, 80, 77), Work, Card, MenuPlace.Beside, 1);
        Assert.Equal(180 + 12, q.Card.X, 3);
        Assert.Equal(Corner.TopLeft, q.Near);
    }

    [Fact]
    public void Place_TopAlignedWithHerHead()
        => Assert.Equal(400, MenuCardLayout.Place(new RectF(900, 400, 80, 77), Work, Card, MenuPlace.Beside, 1).Card.Y);

    [Theory]
    [InlineData(0, 0, 1f), InlineData(1840, 0, 1f), InlineData(0, 963, 1f), InlineData(1840, 963, 1f)]
    [InlineData(0, 0, 1.5f), InlineData(1800, 0, 1.5f), InlineData(0, 924, 1.5f), InlineData(1800, 924, 1.5f)]
    public void Place_EveryCornerInsideWork(float x, float y, float dpi)
    {
        var body = new RectF(x, y, 80 * dpi, 77 * dpi);
        var p = MenuCardLayout.Place(body, Work, Card * dpi, MenuPlace.Beside, dpi);
        Assert.True(Work.Contains(p.Card), $"{p.Card}");
        Assert.False(p.Card.Intersects(body), $"{p.Card} over {body}");
    }

    [Fact]
    public void Place_NeverOverHerBody_WhenThereIsRoom()
    {
        for (float x = 0; x < 1840; x += 37)
        {
            var body = new RectF(x, 300, 80, 77);
            Assert.False(MenuCardLayout.Place(body, Work, Card, MenuPlace.Beside, 1).Card.Intersects(body), $"x {x}");
        }
    }

    [Fact]
    public void Place_Center()
    {
        var p = MenuCardLayout.Place(new RectF(1700, 900, 80, 77), Work, Card, MenuPlace.Center, 1);
        Assert.Equal((960f, 520f), (p.Card.X + p.Card.Width / 2, p.Card.Y + p.Card.Height / 2));
    }

    [Fact]
    public void Place_MoonRestsOnTheNearCorner()
    {
        var p = MenuCardLayout.Place(new RectF(1700, 500, 80, 77), Work, Card * 1.5f, MenuPlace.Beside, 1.5f);
        Assert.Equal(new Vector2(p.Card.Right - 3, p.Card.Y), p.MoonCentre);
        var q = MenuCardLayout.Place(new RectF(100, 500, 80, 77), Work, Card, MenuPlace.Beside, 1);
        Assert.Equal(new Vector2(q.Card.X + 2, q.Card.Y), q.MoonCentre);
    }
}
