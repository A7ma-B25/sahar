using Sahar.Core.Diagnostics;

namespace Sahar.Core.Tests.Diagnostics;

/// <summary>0.14.1 fix 3: a loop that throws every second logs the error once (with its trace), then only a count.</summary>
public sealed class ErrorTallyTests
{
    private DateTimeOffset _now = new(2026, 11, 20, 13, 52, 0, TimeSpan.Zero);

    private static Exception Thrown(Action a) { try { a(); } catch (Exception e) { return e; } throw new InvalidOperationException("no throw"); }
    private static Exception Clamp() => Thrown(() => Math.Clamp(5, 3, 1));
    private static Exception Other() => Thrown(() => throw new InvalidOperationException("x"));

    [Fact]
    public void TheFirstIsLoggedWithItsTrace_RepeatsAreCounted()
    {
        var t = new ErrorTally(() => _now);
        var first = t.Record("activity", Clamp());
        Assert.NotNull(first);
        Assert.StartsWith("activity error: ArgumentException", first);
        Assert.Contains("ThrowMinMaxException", first);   // the trace, once
        for (int i = 0; i < 1272; i++) { _now += TimeSpan.FromMilliseconds(400); Assert.Null(t.Record("activity", Clamp())); }
        _now += TimeSpan.FromMinutes(10);
        var tally = t.Record("activity", Clamp());
        Assert.Equal("activity error: ArgumentException again ×1273 since the first", tally);
    }

    [Fact]
    public void ADifferentErrorIsItsOwn()
    {
        var t = new ErrorTally(() => _now);
        Assert.NotNull(t.Record("activity", Clamp()));
        Assert.NotNull(t.Record("activity", Other()));
        Assert.NotNull(t.Record("screen time", Clamp()));
        Assert.Null(t.Record("activity", Clamp()));
    }
}

/// <summary>0.14.1 fix 5: routine reads from the settings page are never logged (4 145 «page: mind-status» lines).</summary>
public sealed class PageCallLogTests
{
    [Theory]
    [InlineData("get", false)]
    [InlineData("prayer-today", false)]
    [InlineData("mind-status", false)]
    [InlineData("set", true)]
    [InlineData("mind-download", true)]
    [InlineData(null, true)]
    public void RoutinePollsAreNotLogged(string? type, bool logged) => Assert.Equal(logged, LogRules.PageCallLogged(type));
}
