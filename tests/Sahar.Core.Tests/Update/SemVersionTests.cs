using Sahar.Core.Update;

namespace Sahar.Core.Tests.Update;

public class SemVersionTests
{
    private static SemVersion V(string s) => SemVersion.TryParse(s, out var v) ? v : throw new FormatException(s);

    [Theory]
    [InlineData("0.12.0", "0.11.0")]
    [InlineData("0.11.10", "0.11.9")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.12.0", "0.12.0-rc.1")]
    [InlineData("0.12.0-rc.10", "0.12.0-rc.2")]
    [InlineData("0.12.0-alpha.1", "0.12.0-alpha")]
    [InlineData("0.12.0-beta", "0.12.0-alpha.1")]
    [InlineData("0.12.0-alpha.beta", "0.12.0-alpha.1")]
    public void Newer_IsGreater(string newer, string older)
    {
        Assert.True(V(newer) > V(older));
        Assert.True(V(older) < V(newer));
        Assert.True(V(newer).CompareTo(V(older)) > 0);
    }

    [Theory]
    [InlineData("v0.11.0", "0.11.0")]
    [InlineData("V0.11.0", "0.11.0")]
    [InlineData("0.11.0+build.7", "0.11.0")]
    public void SameVersion_ComparesEqual(string a, string b) => Assert.Equal(0, V(a).CompareTo(V(b)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    [InlineData(" 1.2.3")]
    public void Rejects(string? s) => Assert.False(SemVersion.TryParse(s, out _));

    [Fact]
    public void ThisBuildsVersionParses() => Assert.True(SemVersion.TryParse(CoreInfo.Version, out _));

    [Theory]
    [InlineData("0.12.0")]
    [InlineData("0.12.0-rc.1")]
    public void ToString_IsTheVersion(string s) => Assert.Equal(s, V(s).ToString());
}
