using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>Her words about her brain's download (spec §6 + owner answers: size first, plain words when blocked, the
/// VPN hint, never without his yes).</summary>
public sealed class RuntimeTextTests
{
    [Fact]
    public void Gb_ArabicDigitsAndSeparator() => Assert.Equal("٤٫٩٨", RuntimeText.Gb(4_977_171_584));

    [Fact]
    public void Ask_SaysTheSize_AndOffline() =>
        Assert.Equal("صار فيني فكّر وإحكي معك من جوّا جهازك، بلا نت وبلا ما يطلع شي لبرّا. بدي نزّل عقلي: حجمه تقريباً ٤٫٩٨ غيغا. بنزّله هلق؟",
            RuntimeText.Ask(4_977_171_584));

    [Fact]
    public void Blocked_NamesHfAndTheVpn()
    {
        var line = RuntimeText.Blocked;
        Assert.Contains("Hugging Face", line);
        Assert.Contains("VPN", line);
        Assert.Contains("ما نزّلت شي", line);
    }

    [Theory]
    [InlineData(0, 100, -1)]
    [InlineData(24, 100, -1)]
    [InlineData(25, 100, 25)]
    [InlineData(51, 100, 50)]
    [InlineData(99, 100, 75)]
    public void Milestone_Quarters(long done, long total, int want) => Assert.Equal(want, RuntimeText.Milestone(done, total, lastSaid: 0) ?? -1);

    [Fact]
    public void Milestone_SaidOnce() => Assert.Null(RuntimeText.Milestone(30, 100, lastSaid: 25));

    [Fact]
    public void NoDisk_SaysHowMuch() => Assert.Equal("ما في مكان كفاية عالجهاز: لازمني كمان ١٫٦١ غيغا.", RuntimeText.NoDisk(1_610_612_736));

    [Fact]
    public void Lines_NoLatinDigits() =>
        Assert.All(new[] { RuntimeText.Ask(3_106_738_272), RuntimeText.Progress(50), RuntimeText.NoDisk(5L << 30) }, l => Assert.DoesNotMatch("[0-9]", l));
}
