using Sahar.Core.Prayer;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Settings;

/// <summary>What a settings reload must apply in the engine (plan 0.10c Task 2): the groups that changed, by name only.</summary>
public class SettingsChangesTests
{
    private static readonly SaharSettings D = new();

    [Fact]
    public void Same_IsNothing() => Assert.Empty(SettingsChanges.Between(D, new SaharSettings()));

    [Theory]
    [InlineData("size")]
    [InlineData("activity")]
    [InlineData("address")]
    [InlineData("menu")]
    [InlineData("stars")]
    [InlineData("prayer")]
    [InlineData("weather")]
    [InlineData("shortcut")]
    public void EachGroup_IsNamed(string group)
    {
        var next = group switch
        {
            "size" => D with { Size = SaharSize.Large },
            "activity" => D with { Activity = ActivityLevel.Lively },
            "address" => D with { UserName = "Sami" },
            "menu" => D with { MenuItems = ["play"] },
            "stars" => D with { InnerStars = false },
            "prayer" => D with { PrayerMethod = PrayerMethod.Egyptian },
            "weather" => D with { WeatherEnabled = true, WeatherLatitude = 1, WeatherLongitude = 2 },
            "shortcut" => D with { MenuShortcut = "Ctrl+Alt+K" },
            _ => throw new ArgumentException(group),
        };
        Assert.Equal([group], SettingsChanges.Between(D, next));
    }

    [Fact]
    public void Lists_CompareByContent() =>
        Assert.Empty(SettingsChanges.Between(D with { MenuItems = ["play", "note"] }, D with { MenuItems = ["play", "note"] }));

    [Fact]
    public void Gender_Appearance_AndReminder_AreTheirGroups()
    {
        Assert.Equal(["address"], SettingsChanges.Between(D, D with { UserGender = Gender.Female }));
        Assert.Equal(["menu"], SettingsChanges.Between(D, D with { Appearance = Appearance.Dark }));
        Assert.Equal(["prayer"], SettingsChanges.Between(D, D with { PrayerReminderOn = false }));
        Assert.Equal(["size", "menu"], SettingsChanges.Between(D, D with { Size = SaharSize.Small, MenuHeader = MenuHeader.None }));
    }
}
