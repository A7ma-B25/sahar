using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>0.19 D4: tools that delete, send in his name or touch money ask every time.</summary>
public sealed class ToolSafetyTests
{
    [Theory]
    [InlineData("delete_file", null)]
    [InlineData("sendEmail", null)]
    [InlineData("post-tweet", null)]
    [InlineData("transfer", null)]
    [InlineData("buyTicket", null)]
    [InlineData("notes", "Removes a note forever")]
    public void IsSensitive_EnglishVerbsInSnakeAndCamel(string name, string? description) => Assert.True(ToolSafety.IsSensitive(name, description));

    [Theory]
    [InlineData("files", "بتحذف الملف")]
    [InlineData("msg", "ابعت رسالة لحدا")]
    [InlineData("bank", "ادفع الفاتورة")]
    [InlineData("x", "تحويل مصاري")]
    public void IsSensitive_Arabic(string name, string description) => Assert.True(ToolSafety.IsSensitive(name, description));

    [Theory]
    [InlineData("get_messages", "Lists the latest messages")]
    [InlineData("list_files", "Lists files in a folder")]
    [InlineData("weather", "الطقس هلق")]
    [InlineData("postcode_lookup", "Finds a postcode")]
    public void IsSensitive_ReadTool_False(string name, string description) => Assert.False(ToolSafety.IsSensitive(name, description));
}
