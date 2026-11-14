using Sahar.Core.Mind;
using Sahar.Core.Mind.Initiative;

namespace Sahar.Core.Tests.Mind.Initiative;

/// <summary>C2: when she may start a line on her own (spec §3.8, Ruling 2, the owner's never-when list: busy or
/// typing hard, fullscreen, away or locked, prayer or adhan quiet, DND/hush).</summary>
public sealed class InitiativeGateTests
{
    private static readonly IInitiativeGate Gate = MindParts.Gate();
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(3));

    /// <summary>Everything calm: the gate is open.</summary>
    private static InitiativeInputs Open(InitiativeLevel level = InitiativeLevel.Often) => new(
        Now, level, Away: false, Locked: false, SecondsSinceTyping: 600, Busy: false, Fullscreen: false, CameraMic: false, Dnd: false,
        QuranPlaying: false, AdhanQuiet: false, QuietHours: false, MenuOpen: false, Hidden: false, GameOrShowRunning: false,
        ClaudeWaiting: false, TimerDoneShowing: false, MinutesToPrayer: 90, MinutesSincePrayer: 60, LastLine: null, DismissedInARow: 0,
        LinesToday: 0, OnAc: true, BatteryPercent: 100);

    [Fact]
    public void Gate_CalmIsOpen() => Assert.Equal(InitiativeVerdict.Opened, Gate.Check(Open()));

    public static TheoryData<string, string> NeverWhen => new()
    {
        { "away", "away" }, { "locked", "locked" }, { "typing", "typing" }, { "busy", "busy" }, { "fullscreen", "fullscreen" },
        { "camera", "camera" }, { "dnd", "dnd" }, { "quran", "quran" }, { "adhan", "adhan" }, { "quiet-hours", "quiet-hours" },
        { "menu", "menu" }, { "hidden", "hidden" }, { "game", "game" }, { "claude", "claude" }, { "timer-done", "timer-done" },
        { "prayer-soon", "prayer-soon" }, { "prayer-just", "prayer" }, { "battery", "battery" }, { "paused", "paused" }, { "off", "off" },
    };

    [Theory]
    [MemberData(nameof(NeverWhen))]
    public void Gate_EachNeverWhenRuleCloses(string rule, string key)
    {
        var i = Open();
        i = rule switch
        {
            "away" => i with { Away = true },
            "locked" => i with { Locked = true },
            "typing" => i with { SecondsSinceTyping = 4 },
            "busy" => i with { Busy = true },
            "fullscreen" => i with { Fullscreen = true },
            "camera" => i with { CameraMic = true },
            "dnd" => i with { Dnd = true },
            "quran" => i with { QuranPlaying = true },
            "adhan" => i with { AdhanQuiet = true },
            "quiet-hours" => i with { QuietHours = true },
            "menu" => i with { MenuOpen = true },
            "hidden" => i with { Hidden = true },
            "game" => i with { GameOrShowRunning = true },
            "claude" => i with { ClaudeWaiting = true },
            "timer-done" => i with { TimerDoneShowing = true },
            "prayer-soon" => i with { MinutesToPrayer = 15 },
            "prayer-just" => i with { MinutesSincePrayer = 5 },
            "battery" => i with { OnAc = false, BatteryPercent = 35 },
            "paused" => i with { PausedUntil = Now.AddHours(3) },
            "off" => i with { Level = InitiativeLevel.Off },
            _ => throw new ArgumentException(rule),
        };
        var v = Gate.Check(i);
        Assert.False(v.Open);
        Assert.Equal(key, v.ClosedBy);
    }

    [Theory]
    [InlineData(19.9, false)]
    [InlineData(20.1, true)]
    public void Gate_TypingWindowIsTwentySeconds(double seconds, bool open) =>
        Assert.Equal(open, Gate.Check(Open() with { SecondsSinceTyping = seconds }).Open);

    [Theory]
    [InlineData(20.0, false)]
    [InlineData(20.5, true)]
    [InlineData(null, true)]
    public void Gate_PrayerTwentyBefore(double? toPrayer, bool open) =>
        Assert.Equal(open, Gate.Check(Open() with { MinutesToPrayer = toPrayer }).Open);

    [Theory]
    [InlineData(9.9, false)]
    [InlineData(10.0, true)]
    public void Gate_PrayerTenAfter(double since, bool open) =>
        Assert.Equal(open, Gate.Check(Open() with { MinutesSincePrayer = since }).Open);

    [Fact]
    public void Gate_BatteryAboveFortyOrOnAc()
    {
        Assert.True(Gate.Check(Open() with { OnAc = false, BatteryPercent = 41 }).Open);
        Assert.False(Gate.Check(Open() with { OnAc = false, BatteryPercent = 40 }).Open);
        Assert.True(Gate.Check(Open() with { OnAc = true, BatteryPercent = 5 }).Open);
    }

    [Fact]
    public void Gate_OftenCapAndGap()
    {
        Assert.Equal(12, Gate.Cap(InitiativeLevel.Often));
        Assert.Equal(6, Gate.Cap(InitiativeLevel.Normal));
        Assert.Equal(3, Gate.Cap(InitiativeLevel.Rare));
        Assert.Equal(0, Gate.Cap(InitiativeLevel.Off));
        Assert.True(Gate.Check(Open() with { LinesToday = 11 }).Open);
        Assert.Equal("cap", Gate.Check(Open() with { LinesToday = 12 }).ClosedBy);
        Assert.Equal("cap", Gate.Check(Open(InitiativeLevel.Rare) with { LinesToday = 3 }).ClosedBy);
        Assert.Equal("gap", Gate.Check(Open() with { LastLine = Now.AddMinutes(-19) }).ClosedBy);
        Assert.True(Gate.Check(Open() with { LastLine = Now.AddMinutes(-20) }).Open);
        Assert.Equal("gap", Gate.Check(Open(InitiativeLevel.Normal) with { LastLine = Now.AddMinutes(-44) }).ClosedBy);
        Assert.True(Gate.Check(Open(InitiativeLevel.Rare) with { LastLine = Now.AddMinutes(-45) }).Open);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 40)]
    [InlineData(2, 80)]
    [InlineData(3, 160)]
    [InlineData(4, 240)]
    [InlineData(30, 240)]
    public void Gate_DismissalDoubles(int dismissed, int gapMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(gapMinutes), InitiativeGate.Gap(InitiativeLevel.Often, dismissed));
        var i = Open() with { DismissedInARow = dismissed };
        Assert.Equal("gap", Gate.Check(i with { LastLine = Now.AddMinutes(-gapMinutes + 0.5) }).ClosedBy);
        Assert.True(Gate.Check(i with { LastLine = Now.AddMinutes(-gapMinutes) }).Open);
    }

    [Fact]
    public void Gate_FirstClosingRuleIsNamed_AwayBeforeTyping()
    {
        var v = Gate.Check(Open() with { Away = true, SecondsSinceTyping = 1, Fullscreen = true });
        Assert.Equal("away", v.ClosedBy);
    }
}
