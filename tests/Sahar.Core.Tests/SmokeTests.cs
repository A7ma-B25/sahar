using Sahar.Core;

namespace Sahar.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreInfo_Version_Is_0_10_1() => Assert.Equal("0.10.1", CoreInfo.Version);

    [Fact]
    public void CoreInfo_Version_MatchesBuildVersion() =>
        Assert.Equal(CoreInfo.Version, typeof(CoreInfo).Assembly.GetName().Version!.ToString(3));
}
