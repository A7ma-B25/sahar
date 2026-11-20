namespace Sahar.Core.Tests.Chat;


/// <summary>0.14.1: his own words for a timer («حطيلي مؤقت 5 دقايق», the eval's key line) went past the fast path: a polite
/// verb before the command word is skipped as «يلا» is.</summary>
public sealed class ChatCommandsPoliteTests
{
    [Theory]
    [InlineData("حطيلي مؤقت 5 دقايق", 5)]
    [InlineData("شغليلي مؤقت ١٠ دقايق", 10)]
    [InlineData("اعمليلي مؤقت ٣٠", 30)]
    [InlineData("بدي مؤقت 25", 25)]
    public void APoliteVerb_ThenATimer(string text, int minutes)
    {
        var c = Sahar.Core.Chat.ChatCommands.Parse(text);
        Assert.Equal(Sahar.Core.Chat.ChatKind.Timer, c.Kind);
        Assert.Equal(minutes, c.Minutes);
    }

    [Fact]
    public void APoliteVerbAlone_IsStillFreeWords() =>
        Assert.Equal(Sahar.Core.Chat.ChatKind.Unknown, Sahar.Core.Chat.ChatCommands.Parse("حطيلي شي غنية").Kind);
}

public sealed class ChatCommandsPoliteNoteTests
{
    [Fact]
    public void APoliteVerb_ThenANote_KeepsTheNoteText()
    {
        var c = Sahar.Core.Chat.ChatCommands.Parse("سجلي ملاحظة: اشتري خبز وحليب");
        Assert.Equal(Sahar.Core.Chat.ChatKind.Note, c.Kind);
        Assert.Equal("اشتري خبز وحليب", c.Text);
    }
}
