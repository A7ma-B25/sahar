using Sahar.Core;

namespace Sahar.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreInfo_Version_Is_0_14_0() => Assert.Equal("0.14.0", CoreInfo.Version);

    [Fact]
    public void CoreInfo_Version_MatchesBuildVersion() =>
        Assert.Equal(CoreInfo.Version, typeof(CoreInfo).Assembly.GetName().Version!.ToString(3));
}
