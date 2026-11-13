using Sahar.Core.Activities;
using Sahar.Core.Art;

namespace Sahar.Core.Tests.Activities;

public class ActivityIconsTests
{
    [Fact]
    public void EveryIconParses_InsideItsGrid()
    {
        foreach (var (key, icon) in ActivityIcons.All)
        foreach (var d in icon.Paths)
        {
            var commands = PathData.Parse(d);
            Assert.NotEmpty(commands);
        }
    }

    [Theory]
    [InlineData("terminal")]
    [InlineData("bell")]
    [InlineData("stack")]
    [InlineData("hourglass")]
    [InlineData("speaker")]
    public void TagIcons_Exist(string key) => Assert.True(ActivityIcons.All.ContainsKey(key));

    [Fact]
    public void ActivityIconOf_EveryKind()
    {
        foreach (var k in Enum.GetValues<ActivityKind>())
        {
            var icon = ActivityIcons.For(k, null);
            Assert.True(icon is "" || ActivityIcons.All.ContainsKey(icon), $"{k}: {icon}");
        }
        Assert.Equal("wind", ActivityIcons.For(ActivityKind.WeatherAlert, WeatherAlert.Wind));
        Assert.Equal("rain", ActivityIcons.For(ActivityKind.WeatherAlert, WeatherAlert.Rain));
    }
}
