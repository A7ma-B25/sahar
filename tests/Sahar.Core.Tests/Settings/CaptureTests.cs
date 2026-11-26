using System.Text.Json;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>T8 «اسمح بتصويرها» (0.15.0): her windows are hidden from screen capture unless he allows it; the change
/// reaches every window she has open, live.</summary>
public class CaptureTests
{
    private static (int, bool)[] Ints(IEnumerable<(nint Hwnd, bool Excluded)> list) => [.. list.Select(x => ((int)x.Hwnd, x.Excluded)).OrderBy(x => x.Item1)];

    [Fact]
    public void AllowCapture_IsOffByDefault_AndInAnOldFile()
    {
        Assert.False(new SaharSettings().AllowCapture);
        Assert.False(SettingsStore.FromJson("""{ "size": "large" }""").AllowCapture);
        Assert.True(SettingsStore.FromJson("""{ "allowCapture": true }""").AllowCapture);
        Assert.Contains("\"allowCapture\": true", SettingsStore.ToJson(new SaharSettings { AllowCapture = true }));
    }

    [Fact]
    public void Patch_AllowCapture_TakesABoolOnly()
    {
        var on = SettingsPatch.Apply(new SaharSettings(), "allowCapture", JsonDocument.Parse("true").RootElement, out var error);
        Assert.Null(error);
        Assert.True(on.AllowCapture);
        var bad = SettingsPatch.Apply(new SaharSettings(), "allowCapture", JsonDocument.Parse("\"yes\"").RootElement, out error);
        Assert.Equal(SettingsPatch.BadValue, error);
        Assert.False(bad.AllowCapture);
    }

    [Fact]
    public void Changes_CaptureIsItsOwnGroup() =>
        Assert.Equal(["capture"], SettingsChanges.Between(new SaharSettings(), new SaharSettings { AllowCapture = true }));

    [Fact]
    public void Registry_AWindowIsExcluded_UntilHeAllowsIt()
    {
        var r = new CaptureRegistry();
        Assert.True(r.Add(1, wanted: true));
        Assert.False(r.Add(2, wanted: false));   // --capturable (CI): never excluded
        Assert.Equal((2, 1), (r.Count, r.ExcludedCount));
        Assert.Equal([(1, false), (2, false)], Ints(r.SetAllowed(true)));
        Assert.Equal((2, 0), (r.Count, r.ExcludedCount));
        Assert.False(r.Add(3, wanted: true));    // a window opened while allowed (a bubble) is capturable too
        Assert.Equal([(1, true), (2, false), (3, true)], Ints(r.SetAllowed(false)));
        Assert.Equal(2, r.ExcludedCount);
    }

    [Fact]
    public void Registry_TheSameValue_ChangesNothing_AndAGoneWindowIsForgotten()
    {
        var r = new CaptureRegistry();
        r.Add(1, true);
        r.Add(2, true);
        Assert.Empty(r.SetAllowed(false));
        r.Remove(1);
        r.Remove(9);   // never added: nothing
        Assert.Equal([(2, false)], Ints(r.SetAllowed(true)));
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Registry_AddingAgain_KeepsOneEntry()
    {
        var r = new CaptureRegistry();
        r.Add(1, true);
        r.Add(1, true);
        Assert.Equal(1, r.Count);
    }

    [Fact]
    public void HerLine_IsShort_AndAboutHerself()
    {
        Assert.Equal("تمام، هلق بطلع بالتصوير.", CaptureRegistry.Said(true));
        Assert.Equal("تمام، ما عاد بطلع بالتصوير.", CaptureRegistry.Said(false));
        // Her words about herself never change for a woman (no word of Address's table in them).
        Assert.Equal(CaptureRegistry.Said(true), Address.For(Gender.Female, CaptureRegistry.Said(true)));
        Assert.Equal(CaptureRegistry.Said(false), Address.For(Gender.Female, CaptureRegistry.Said(false)));
    }

    [Fact]
    public void Registry_NamesTheWindowsThatWantToBeHidden()
    {
        var r = new CaptureRegistry();
        r.Add(1, true);
        r.Add(2, false);
        r.Add(3, true);
        Assert.Equal([1, 3], r.Wanting.Select(h => (int)h).Order());
        r.SetAllowed(true);
        Assert.Equal([1, 3], r.Wanting.Select(h => (int)h).Order());   // what they want, not what he allows
    }
}
