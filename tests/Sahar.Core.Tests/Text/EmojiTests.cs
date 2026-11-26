using Sahar.Core.Text;
using Xunit;

namespace Sahar.Core.Tests.Text;

public class EmojiTests
{
    [Theory]
    [InlineData("تشرفنا 💜", true)]
    [InlineData("🏆 ربحت", true)]
    [InlineData("✨", true)]
    [InlineData("⚽", true)]
    [InlineData("☀️", true)]
    [InlineData("سَحَر · Sahar", false)]
    [InlineData("١٢:٠٤ — «الكهف»…", false)]
    [InlineData("✓", false)]
    public void Has_FindsPictographsOnly(string text, bool has) => Assert.Equal(has, Emoji.Has(text));

    [Theory]
    [InlineData("تشرفنا 💜", "تشرفنا")]
    [InlineData("🏆 ربحت!", "ربحت!")]
    [InlineData("انسخها 📋", "انسخها")]
    [InlineData("بصوتك ✍️", "بصوتك")]
    [InlineData("سطر  عادي", "سطر  عادي")]
    public void Strip_LeavesCleanText(string text, string clean) => Assert.Equal(clean, Emoji.Strip(text));
}
