using System.Text.Json;
using Sahar.Core.Surfaces;
using Xunit;

namespace Sahar.Core.Tests.Surfaces;

public class JModelTests
{
    [Fact]
    public void Writes_ValidJson_WithNestedArraysAndEscapes()
    {
        var j = JModel.Of("list").S("title", "شو \"بدك\" <تسمع>؟\n").N("listHeight", 302).T("back", true).T("no", false)
            .O("sub").S("text", "بصوت مشاري").End()
            .A("rows").Item().S("id", "36").S("label", "يس 😊").N("num", 36).End().Item().S("id", "1").End().End()
            .A("tags").V("a").V("b").End();
        var doc = JsonDocument.Parse(j.ToString()).RootElement;
        Assert.Equal("list", doc.GetProperty("kind").GetString());
        Assert.Equal("شو \"بدك\" <تسمع>؟\n", doc.GetProperty("title").GetString());
        Assert.True(doc.GetProperty("back").GetBoolean());
        Assert.False(doc.TryGetProperty("no", out _));
        Assert.Equal(2, doc.GetProperty("rows").GetArrayLength());
        Assert.Equal("يس 😊", doc.GetProperty("rows")[0].GetProperty("label").GetString());
        Assert.Equal("b", doc.GetProperty("tags")[1].GetString());
        Assert.DoesNotContain("<", j.ToString());
    }

    [Fact]
    public void ToString_ClosesWhatIsOpen()
    {
        var j = JModel.Of("x").A("rows").Item().S("id", "1");
        Assert.Equal("1", JsonDocument.Parse(j.ToString()).RootElement.GetProperty("rows")[0].GetProperty("id").GetString());
    }
}
