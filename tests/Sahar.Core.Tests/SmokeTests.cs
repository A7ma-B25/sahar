using Sahar.Core;

namespace Sahar.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void CoreInfo_Version_Is_0_1_0() => Assert.Equal("0.1.0", CoreInfo.Version);
}
