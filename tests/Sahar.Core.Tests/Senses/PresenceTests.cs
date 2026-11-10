using Sahar.Core.Senses;

namespace Sahar.Core.Tests.Senses;

/// <summary>Away comes from real input, never from her face (the owner's machine: no "away" overnight, and "away"
/// while typing).</summary>
public class PresenceTests
{
    private static readonly TimeSpan Min = TimeSpan.FromMinutes(1);

    [Fact]
    public void SystemIdleButTypingNow_IsNotAway() =>
        Assert.False(Presence.Away(10 * Min, TimeSpan.FromSeconds(2), 10 * Min, locked: false));

    [Fact]
    public void PointerMovedRecently_IsNotAway() =>
        Assert.False(Presence.Away(10 * Min, 10 * Min, TimeSpan.FromSeconds(10), locked: false));

    [Fact]
    public void EverySourceQuietFiveMinutes_IsAway() =>
        Assert.True(Presence.Away(5 * Min, 6 * Min, 7 * Min, locked: false));

    [Fact]
    public void JustUnderFiveMinutes_IsNotAway() =>
        Assert.False(Presence.Away(5 * Min - TimeSpan.FromSeconds(1), 6 * Min, 7 * Min, locked: false));

    [Fact]
    public void Locked_IsAway() => Assert.True(Presence.Away(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, locked: true));

    [Fact]
    public void NoInputEverSeen_IsAway() =>
        Assert.True(Presence.Away(TimeSpan.MaxValue, TimeSpan.MaxValue, TimeSpan.MaxValue, locked: false));
}
