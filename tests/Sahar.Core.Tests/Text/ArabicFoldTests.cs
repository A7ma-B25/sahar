using Sahar.Core.Text;
using Xunit;

namespace Sahar.Core.Tests.Text;

public class ArabicFoldTests
{
    [Theory]
    [InlineData("يٰسٓ", "يس")]
    [InlineData("الإسراء", "الاسراء")]
    [InlineData("آل عمران", "ال عمران")]
    [InlineData("فاطِمَة", "فاطمه")]
    [InlineData("الضحى", "الضحي")]
    [InlineData("المؤمنون", "المومنون")]
    [InlineData("  الكهف ", "الكهف")]
    [InlineData("AbC", "abc")]
    public void Fold_IgnoresTashkeelHamzaTaMarbutaAndAlifMaqsura(string input, string folded) =>
        Assert.Equal(folded, ArabicFold.Fold(input));

    [Theory]
    [InlineData("يس", true)]
    [InlineData("يٰس", true)]
    [InlineData("٣٦", true)]
    [InlineData("36", true)]
    [InlineData("3", false)]
    [InlineData("يسن", false)]
    public void Matches_NameOrNumber(string query, bool hit) =>
        Assert.Equal(hit, ArabicFold.Matches(query, "يس", 36));

    [Fact]
    public void Matches_ByNumberPrefixAndByAnyWord()
    {
        Assert.True(ArabicFold.Matches("عمران", "آل عمران", 3));
        Assert.True(ArabicFold.Matches("", "الفاتحة", 1));
        Assert.True(ArabicFold.Matches("١١", "هود", 11));
        Assert.True(ArabicFold.Matches("11", "المائدة", 5) == false);
    }

    [Fact]
    public void SurahSearch_YaSeenGivesOneRow()
    {
        var rows = Enumerable.Range(1, 114).Where(n => ArabicFold.Matches("يس", Sahar.Core.Quran.Quran.SurahName(n), n)).ToList();
        Assert.Equal([36], rows);
    }
}
