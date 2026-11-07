using Sahar.Core.Art;
using Sahar.Core.Expressions;

namespace Sahar.Core.Tests.Expressions;

public class ExpressionCatalogTests
{
    [Fact]
    public void Catalog_Has27_UniqueIdsAndKeys()
    {
        Assert.Equal(27, ExpressionCatalog.All.Count);
        Assert.Equal(27, ExpressionCatalog.All.Select(x => x.Id).Distinct().Count());
        Assert.Equal(27, ExpressionCatalog.All.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void Catalog_Ids_Are_0_To_26() =>
        Assert.Equal(Enumerable.Range(0, 27), ExpressionCatalog.All.Select(x => x.Id).Order());

    [Fact]
    public void Catalog_Idle_IsCalmOpenSmile()
    {
        var p = ExpressionCatalog.Get("idle");
        Assert.Equal((0, Mood.Calm, Eyes.Open, Mouth.Smile, (string?)null), (p.Id, p.Mood, p.Eyes, p.Mouth, p.OrbGlyph));
    }

    [Fact]
    public void Catalog_Keys_MatchSpecOrder() => Assert.Equal(
        new[] { "music", "claude-working", "claude-permission", "claude-done", "timer-done", "celebrate", "rain",
                "battery-low", "charging", "download", "sleep", "back", "notification", "dnd", "offline", "online",
                "screenshot", "copied", "reminder", "error", "hot", "poked", "dragged", "break", "late-night", "quran" },
        ExpressionCatalog.All.Where(x => x.Id > 0).OrderBy(x => x.Id).Select(x => x.Key));

    [Fact]
    public void Catalog_PermissionPreset()
    {
        var p = ExpressionCatalog.Get("claude-permission");
        Assert.Equal((Mood.Alert, Eyes.Open, Mouth.O, "؟"), (p.Mood, p.Eyes, p.Mouth, p.OrbGlyph));
    }

    [Fact]
    public void Catalog_Quran_CalmClosedEyesHeadphonesBook()
    {
        var p = ExpressionCatalog.Get("quran");
        Assert.Equal((26, Mood.Calm, Eyes.Closed, "📖"), (p.Id, p.Mood, p.Eyes, p.OrbGlyph));
        Assert.True(p.Extras.HasFlag(Extras.Headphones));
        Assert.False(p.Extras.HasFlag(Extras.Notes));
    }

    [Fact]
    public void Catalog_MusicHasHeadphones() =>
        Assert.True(ExpressionCatalog.Get("music").Extras.HasFlag(Extras.Headphones));

    [Fact]
    public void Catalog_UnknownKey_Throws() =>
        Assert.Throws<KeyNotFoundException>(() => ExpressionCatalog.Get("nope"));
}
