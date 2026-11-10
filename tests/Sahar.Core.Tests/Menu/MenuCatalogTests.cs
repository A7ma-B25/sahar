using Sahar.Core.Art;
using Sahar.Core.Menu;

namespace Sahar.Core.Tests.Menu;

public class MenuCatalogTests
{
    private static string Ids(IEnumerable<MenuEntry> e) => string.Join(',', e.Select(x => x.Id));

    [Fact]
    public void Tiles_Labels()
    {
        var t = MenuCatalog.Tiles(hushOn: false);
        Assert.Equal(new MenuEntry("play", "نلعب؟", "ball"), t[0]);
        Assert.Equal(new MenuEntry("dnd", "بسكت شوي", "bud"), t[1]);
        Assert.Equal(new MenuEntry("hide", "بغفى شوي", "moonZ"), t[2]);
        Assert.Equal("رجّعيني احكي", MenuCatalog.Tiles(hushOn: true)[1].Label);
    }

    [Fact]
    public void Tools_InOrder()
    {
        Assert.Equal(
            new MenuEntry[] { new("timer", "مؤقت", "clock"), new("note", "ملاحظة", "page"), new("listen", "استماع", "headphones"), new("clipboard", "الحافظة", "board") },
            MenuCatalog.Tools);
        Assert.Equal(new MenuEntry("settings", "إعداداتي", "gear"), MenuCatalog.Settings);
        Assert.Equal(new MenuEntry("back", "رجوع", "back"), MenuCatalog.Back);
    }

    [Fact]
    public void ItemIds_AreTheEightChoosable()
        => Assert.Equal("chat,play,dnd,hide,timer,note,listen,clipboard", string.Join(',', MenuCatalog.ItemIds));

    [Fact]
    public void Visible_NullMeansEveryAvailable() => Assert.Equal(8, MenuCatalog.Visible(null, _ => true).Count);

    [Fact]
    public void Visible_DropsUnavailable() => Assert.DoesNotContain("listen", MenuCatalog.Visible(null, id => id != "listen"));

    [Fact]
    public void Visible_DropsSwitchedOff()
        => Assert.Equal("play,timer", string.Join(',', MenuCatalog.Visible(["play", "timer"], _ => true).Order()));

    [Fact]
    public void Visible_IgnoresUnknownIds() => Assert.Equal("play", string.Join(',', MenuCatalog.Visible(["play", "radio"], _ => true)));

    private static readonly MenuState Idle = new(false, false, false);

    [Fact]
    public void Drill_Play()
    {
        var d = MenuCatalog.Drill("play", Idle)!;
        Assert.Equal(("شو بدك نلعب؟", DrillStyle.List), (d.Title, d.Style));
        Assert.Equal("game-chase,game-hide,game-ball,game-stars,play-random", Ids(d.Options));
        Assert.Equal("الحقني,غمّضلي,الطابة,لقط النجوم,حركة عشوائية", string.Join(',', d.Options.Select(o => o.Label)));
    }

    [Fact]
    public void Drill_Hush()
    {
        var d = MenuCatalog.Drill("dnd", Idle)!;
        Assert.Equal(("قديش بدك ياني اسكت؟", DrillStyle.Segmented), (d.Title, d.Style));
        Assert.Equal("dnd-30,dnd-60,dnd-120,dnd-on", Ids(d.Options));
        Assert.Equal("نص ساعة,ساعة,ساعتين,لحد ما تقلي", string.Join(',', d.Options.Select(o => o.Label)));
        Assert.Equal("رح وقّف تعليقاتي، بس بضل ذكّرك بالصلاة والمؤقت.", d.Note);
        Assert.Null(MenuCatalog.Drill("dnd", Idle with { HushOn = true }));
    }

    [Fact]
    public void Drill_Timer()
    {
        var d = MenuCatalog.Drill("timer", Idle)!;
        Assert.Equal(("قديش المؤقت؟", DrillStyle.Segmented), (d.Title, d.Style));
        Assert.Equal("timer-5,timer-15,timer-25,timer-45,timer-custom", Ids(d.Options));
        Assert.Equal("٥ د,١٥ د,٢٥ د,٤٥ د,وقت تاني", string.Join(',', d.Options.Select(o => o.Label)));
        var running = MenuCatalog.Drill("timer", Idle with { TimerRunning = true })!;
        Assert.Equal("المؤقت شغّال", running.Title);
        Assert.Equal(new[] { new MenuEntry("timer-cancel", "وقّف المؤقت", "clock") }, running.Options);
    }

    [Fact]
    public void Drill_Listen()
    {
        var d = MenuCatalog.Drill("listen", Idle)!;
        Assert.Equal("شو بدك نسمع؟", d.Title);
        Assert.Equal("music,quran", Ids(d.Options));
        Assert.Equal("موسيقى,قرآن", string.Join(',', d.Options.Select(o => o.Label)));
        var playing = MenuCatalog.Drill("listen", Idle with { QuranPlaying = true })!;
        Assert.Equal("quran-stop,music", Ids(playing.Options));
        Assert.Equal("وقّف القرآن", playing.Options[0].Label);
    }

    [Fact]
    public void Drill_Music()
    {
        var d = MenuCatalog.Drill("music", Idle)!;
        Assert.Equal("الموسيقى", d.Title);
        Assert.Equal("music-prev,music-toggle,music-next", Ids(d.Options));
        Assert.Equal("السابق,تشغيل/إيقاف,التالي", string.Join(',', d.Options.Select(o => o.Label)));
    }

    [Theory]
    [InlineData("hide"), InlineData("note"), InlineData("clipboard"), InlineData("chat"), InlineData("settings")]
    public void Drill_DirectItems_AreNull(string id) => Assert.Null(MenuCatalog.Drill(id, Idle));

    [Fact]
    public void MenuIcons_AllParse()
    {
        string[] keys = ["ball", "bud", "moonZ", "clock", "page", "headphones", "board", "gear", "chevronStart", "back", "mic", "send"];
        Assert.All(keys, k => Assert.True(MenuIcons.All.ContainsKey(k), k));
        foreach (var (key, icon) in MenuIcons.All)
        {
            Assert.True(icon.Paths.Length + icon.Circles.Length > 0, key);
            foreach (var p in icon.Paths) Assert.NotEmpty(PathData.Parse(p));
        }
    }

    [Fact]
    public void MenuIcons_CarryTheHandWobble_WithinTheGrid()
    {
        // The mockup's displacement (scale 1.1) baked in: the paths stay inside the 24-unit grid (± the wobble), and
        // a straight stroke is no longer perfectly straight.
        foreach (var (key, icon) in MenuIcons.All)
            foreach (var p in icon.Paths)
                foreach (var c in PathData.Parse(p))
                    Assert.InRange(c.P3.X, -1f, 25f);
        var page = PathData.Parse(MenuIcons.All["page"].Paths[1]);
        Assert.Contains(page, c => c.Op == PathOp.CubicTo);
    }
}

public class MenuOpenRuleTests
{
    [Theory]
    [InlineData(Sahar.Core.Settings.MenuOpen.Double, true, false, true)]
    [InlineData(Sahar.Core.Settings.MenuOpen.Right, false, true, true)]
    [InlineData(Sahar.Core.Settings.MenuOpen.Both, true, true, true)]
    public void MenuOpenRule_Allows(Sahar.Core.Settings.MenuOpen setting, bool dbl, bool right, bool shortcut)
    {
        Assert.Equal(dbl, MenuOpenRule.Allows(setting, MenuTrigger.DoubleClick));
        Assert.Equal(right, MenuOpenRule.Allows(setting, MenuTrigger.RightClick));
        Assert.Equal(shortcut, MenuOpenRule.Allows(setting, MenuTrigger.Shortcut));
    }
}
