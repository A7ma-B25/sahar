using Sahar.Core.Menu;

namespace Sahar.Core.Tests.Menu;

public class MenuKeysTests
{
    private static readonly MenuView View = new("main", MenuCatalog.Visible(null, _ => true), false, false, false, null);
    private static KeyResult K(string? focus, MenuKey key, bool rtl = true, bool inChat = false, MenuView? v = null)
    {
        var view = v ?? View;
        return MenuKeys.Handle(MenuCardLayout.Layout(view, rtl), view, focus, key, rtl, inChat);
    }

    [Fact]
    public void Tab_WrapsThroughTheOrder()
    {
        Assert.Equal("chat", K(null, MenuKey.Tab).Focus);
        Assert.Equal("send", K("chat", MenuKey.Tab).Focus);
        Assert.Equal("chat", K("clipboard", MenuKey.Tab).Focus);
        Assert.Equal("clipboard", K("chat", MenuKey.ShiftTab).Focus);
    }

    [Fact]
    public void Arrows_AreMirroredInRtl()
    {
        Assert.Equal("dnd", K("play", MenuKey.Left).Focus);
        Assert.Equal("play", K("dnd", MenuKey.Right).Focus);
        Assert.Equal("play", K("dnd", MenuKey.Left, rtl: false).Focus);
        Assert.Equal("play", K("play", MenuKey.Right).Focus);   // the row's end: stays
    }

    [Fact]
    public void UpDown_GoToTheNearestInTheNextRow()
    {
        var l = MenuCardLayout.Layout(View);
        var down = K("dnd", MenuKey.Down).Focus!;
        float cx = l.Rects["dnd"].X + l.Rects["dnd"].Width / 2;
        Assert.Contains(down, new[] { "note", "listen" });
        Assert.True(MathF.Abs(l.Rects[down].X + l.Rects[down].Width / 2 - cx) < 40);
        Assert.Equal("dnd", K(down, MenuKey.Up).Focus);
    }

    [Fact]
    public void Digits_PickTheTiles_NotInChat()
    {
        Assert.Equal("play", K(null, MenuKey.Digit1).Activate);
        Assert.Equal("hide", K("chat", MenuKey.Digit3).Activate);
        Assert.Null(K("chat", MenuKey.Digit1, inChat: true).Activate);
    }

    [Fact]
    public void Enter_Activates_Escape_BacksThenCloses()
    {
        Assert.Equal("timer", K("timer", MenuKey.Enter).Activate);
        Assert.True(K("chat", MenuKey.Escape, inChat: true).Close);
        var drill = MenuCatalog.Drill("play", new MenuState(false, false, false));
        var dv = new MenuView("play", View.Visible, false, false, false, drill);
        var r = K("game-chase", MenuKey.Escape, v: dv);
        Assert.True(r.Back);
        Assert.False(r.Close);
    }
}
