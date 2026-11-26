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

/// <summary>0.15.0: «شغلي المؤقت 5 ثواني» started 5 MINUTES — the fast path reads seconds, minutes and hours, and
/// «المؤقت» / «تايمر» are timer words too.</summary>
public sealed class ChatCommandsTimerUnitsTests
{
    [Theory]
    [InlineData("شغلي المؤقت 5 ثواني", 5)]
    [InlineData("مؤقت ٣٠ ثانية", 30)]
    [InlineData("timer 10 sec", 10)]
    [InlineData("حطيلي مؤقت 5 دقايق", 300)]
    [InlineData("مؤقت ٢٥", 1500)]
    [InlineData("حطيلي تايمر ساعة", 3600)]
    [InlineData("مؤقت ساعتين", 7200)]
    [InlineData("مؤقت دقيقة", 60)]
    [InlineData("مؤقت 2 ساعات", 7200)]
    public void Timer_ReadsItsUnit(string text, int seconds)
    {
        var c = Sahar.Core.Chat.ChatCommands.Parse(text);
        Assert.Equal(Sahar.Core.Chat.ChatKind.Timer, c.Kind);
        Assert.False(c.OutOfRange);
        Assert.Equal(TimeSpan.FromSeconds(seconds), c.Duration);
    }

    [Theory]
    [InlineData("مؤقت ٠ ثانية")]
    [InlineData("مؤقت 11 ساعة")]
    public void Timer_OutOfRange_InAnyUnit(string text) => Assert.True(Sahar.Core.Chat.ChatCommands.Parse(text).OutOfRange);

    [Fact]
    public void Reply_SaysSeconds() =>
        Assert.Equal("تمام! شغّلت مؤقت ٥ ثواني.", Sahar.Core.Chat.ChatReply.For(Sahar.Core.Chat.ChatCommands.Parse("شغلي المؤقت 5 ثواني"),
            new Sahar.Core.Chat.ChatContext(null, 0), Sahar.Core.Settings.Gender.Male));
}
