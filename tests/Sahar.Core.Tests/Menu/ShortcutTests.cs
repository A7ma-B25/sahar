using Sahar.Core.Menu;

namespace Sahar.Core.Tests.Menu;

public class ShortcutTests
{
    [Theory]
    [InlineData("Alt+S", "Alt+S")]
    [InlineData("ctrl+alt+k", "Ctrl+Alt+K")]
    [InlineData("Ctrl+Shift+F5", "Ctrl+Shift+F5")]
    [InlineData("alt+ctrl+s", "Ctrl+Alt+S")]
    [InlineData("Ctrl+Alt+Space", "Ctrl+Alt+Space")]
    public void Shortcut_Ok(string text, string shown)
    {
        var c = Shortcuts.Check(text);
        Assert.True(c.Ok, c.Reason);
        Assert.Equal(shown, c.Value.ToString());
    }

    [Theory]
    [InlineData(""), InlineData("Alt+"), InlineData("S"), InlineData("Shift+S"), InlineData("Ctrl+Alt"), InlineData("Ctrl+Q+W"), InlineData("Ctrl+Alt+Ä")]
    public void Shortcut_NeedsCtrlOrAlt(string text) => Assert.Equal("لازم يكون فيه Ctrl أو Alt.", Shortcuts.Check(text).Reason);

    [Theory]
    [InlineData("Win+L"), InlineData("Alt+F4"), InlineData("Alt+Tab"), InlineData("Alt+Esc"), InlineData("Ctrl+Esc"), InlineData("Alt+Space")]
    public void Shortcut_WindowsOwnIsRefused(string text) => Assert.Equal("هالاختصار للويندوز، اختار غيره.", Shortcuts.Check(text).Reason);

    [Theory]
    [InlineData("Ctrl+C"), InlineData("Ctrl+V"), InlineData("Ctrl+X"), InlineData("Ctrl+Z"), InlineData("Ctrl+A"), InlineData("Ctrl+S"), InlineData("Ctrl+F")]
    [InlineData("Ctrl+P"), InlineData("Ctrl+W"), InlineData("Ctrl+T"), InlineData("Ctrl+N"), InlineData("Ctrl+O"), InlineData("Ctrl+Y")]
    public void Shortcut_AppsCommonOnesAreRefused(string text) => Assert.Equal("هالاختصار بتستعمله البرامج كتير، اختار غيره.", Shortcuts.Check(text).Reason);

    [Fact]
    public void Shortcut_Null_IsTheDefault()
    {
        var c = Shortcuts.Check(null);
        Assert.True(c.Ok);
        Assert.Equal("Ctrl+Alt+S", c.Value.ToString());
        Assert.Equal((KeyMods.Ctrl | KeyMods.Alt, 0x53), (Shortcuts.Default.Mods, Shortcuts.Default.Vk));
    }
}
