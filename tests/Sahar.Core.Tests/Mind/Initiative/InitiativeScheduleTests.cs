using Sahar.Core.Mind.Initiative;

namespace Sahar.Core.Tests.Mind.Initiative;

/// <summary>0.15.0: «often» spoke 7 times in 7 minutes (his log 20:47–20:53: a check every minute). The schedule spaces
/// the checks themselves (a "nothing to say" backs off), keeps ≥ 20 min between her lines, caps them per hour, and
/// never says the same topic twice.</summary>
public sealed class InitiativeScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 20, 46, 0, TimeSpan.FromHours(3));

    /// <summary>His minute timer for <paramref name="minutes"/>: each tick asks when the schedule allows; the model
    /// answers with <paramref name="answer"/> (null = nothing to say).</summary>
    private static (int Checks, List<DateTimeOffset> Lines) Run(InitiativeSchedule s, int minutes, Func<int, string?> answer, InitiativeLevel level = InitiativeLevel.Often)
    {
        int checks = 0;
        var lines = new List<DateTimeOffset>();
        for (int m = 0; m < minutes; m++)
        {
            var now = T0.AddMinutes(m);
            if (!s.MayCheck(now, level)) continue;
            checks++;
            s.Asked(now, level);
            if (answer(checks) is { } line && !s.Repeats(line)) { s.Spoke(now, line, level); lines.Add(now); }
        }
        return (checks, lines);
    }

    [Fact]
    public void NothingToSay_ChecksBackOff_NotEveryMinute()
    {
        var (checks, lines) = Run(new InitiativeSchedule(T0), 60, _ => null);
        Assert.Empty(lines);
        Assert.InRange(checks, 1, 4);
    }

    [Fact]
    public void TheOwnersSevenMinutes_AtMostOneLine()
    {
        var (_, lines) = Run(new InitiativeSchedule(T0), 7, i => $"موضوع رقم {i} عن شغلك اليوم {new string('x', i)}");
        Assert.True(lines.Count <= 1, $"{lines.Count} lines");
    }

    [Theory]
    [InlineData(InitiativeLevel.Often, 2, 20)]
    [InlineData(InitiativeLevel.Normal, 1, 45)]
    [InlineData(InitiativeLevel.Rare, 1, 90)]
    public void Lines_AreSpacedAndCappedPerHour(InitiativeLevel level, int perHour, int gapMinutes)
    {
        var topics = new[] { "القهوة", "المشروع", "الرياضة", "الموسيقى", "الطقس", "المي", "النوم", "الأكل", "الصلاة", "الكتاب", "الأصحاب", "السفر" };
        var (_, lines) = Run(new InitiativeSchedule(T0), 180, i => $"شو رأيك ب{topics[i % topics.Length]} هلق؟", level);
        Assert.NotEmpty(lines);
        for (int i = 1; i < lines.Count; i++) Assert.True(lines[i] - lines[i - 1] >= TimeSpan.FromMinutes(gapMinutes), $"{lines[i - 1]:t} → {lines[i]:t}");
        foreach (var at in lines) Assert.True(lines.Count(l => l > at.AddHours(-1) && l <= at) <= perHour);
        Assert.Equal(perHour, InitiativeSchedule.HourCap(level));
    }

    [Fact]
    public void Off_NeverChecks() => Assert.False(new InitiativeSchedule(T0).MayCheck(T0.AddHours(1), InitiativeLevel.Off));

    [Theory]
    [InlineData("شو رأيك تاخد استراحة؟ صرلك كتير قاعد", "صرلك كتير قاعد، شو رأيك تاخد استراحة 😊", true)]
    [InlineData("شو رأيك تاخد استراحة؟ صرلك كتير قاعد", "اشربت مي اليوم؟", false)]
    [InlineData("اشربت مي اليوم؟", "اشربت مي اليوم؟", true)]
    public void SameTopic_IsARepeat(string first, string second, bool repeat)
    {
        var s = new InitiativeSchedule(T0);
        s.Spoke(T0, first, InitiativeLevel.Often);
        Assert.Equal(repeat, s.Repeats(second));
    }
}
