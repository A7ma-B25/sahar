using Sahar.Core.Mind;

namespace Sahar.Core.Tests.Mind;

/// <summary>0.15.0: his log had «mind: cancel (card closed)» ×4 — a closed card no longer stops her reply. Her words go
/// to the card's line while it is open and fits, else to her bubble; when the card closed (a game or a move closes it)
/// a long reply becomes the short line of what really ran.</summary>
public sealed class ReplyRouteTests
{
    [Theory]
    [InlineData(true, true, ReplyPlace.Header)]
    [InlineData(true, false, ReplyPlace.Bubble)]
    [InlineData(false, true, ReplyPlace.Bubble)]
    [InlineData(false, false, ReplyPlace.Bubble)]
    public void Where(bool cardOpen, bool fits, ReplyPlace place) => Assert.Equal(place, ReplyRoute.Where(cardOpen, fits));

    [Fact]
    public void AClosedCard_NeverCancels() => Assert.False(ReplyRoute.CardCloseCancels);

    [Fact]
    public void Final_ClosedCard_LongReply_IsTheShortConfirmation()
    {
        var longSay = "يلا! رح الحقك وما رح خليك تهرب مني أبداً، جهّز حالك لأني سريعة كتير اليوم وما بعرف التعب 😆";
        Assert.Equal("بلّشنا الحقني", ReplyRoute.Final(longSay, "بلّشنا الحقني", cardOpen: false));
        Assert.Equal("يلا! 😆", ReplyRoute.Final("يلا! 😆", "بلّشنا الحقني", cardOpen: false));   // short: her own words
        Assert.Equal(longSay, ReplyRoute.Final(longSay, "بلّشنا الحقني", cardOpen: true));
        Assert.Equal(longSay, ReplyRoute.Final(longSay, null, cardOpen: false));                // nothing ran: her words
    }
}

/// <summary>Review I2: with the card closed her bubble streams her words only while they are short, never for a turn that
/// acts (it gets its one short confirmation, no visible swap), and a bubble he clicked away stays away for that turn.</summary>
public sealed class BubbleStreamTests
{
    [Fact]
    public void Streams_WhileShort_ThenStops_AndTheFinalExtendsIt()
    {
        var b = new BubbleStream();
        Assert.Equal("يلا", b.Partial("يلا", acts: false, dismissed: false));
        Assert.Equal("يلا نحكي", b.Partial("يلا نحكي", acts: false, dismissed: false));
        Assert.Null(b.Partial(new string('ك', ReplyRoute.BubbleChars + 1), acts: false, dismissed: false));
        Assert.Null(b.Partial("يلا نحكي شي", acts: false, dismissed: false));   // stopped for good
        Assert.Equal("يلا نحكي شي حلو", b.Final("يلا نحكي شي حلو", dismissed: false));
        Assert.True(b.Shown);
    }

    [Fact]
    public void ATurnThatActs_NeverStreams_OnlyItsShortLine()
    {
        var b = new BubbleStream();
        Assert.Null(b.Partial("يلا", acts: true, dismissed: false));
        Assert.False(b.Shown);
        Assert.Equal("بلّشنا لقط النجوم", b.Final("بلّشنا لقط النجوم", dismissed: false));
    }

    [Fact]
    public void ADismiss_IsHonouredForTheTurn()
    {
        var b = new BubbleStream();
        Assert.Equal("يلا", b.Partial("يلا", acts: false, dismissed: false));
        Assert.Null(b.Partial("يلا نحكي", acts: false, dismissed: true));   // he clicked it away
        Assert.Null(b.Partial("يلا نحكي شي", acts: false, dismissed: false));
        Assert.Null(b.Final("يلا نحكي شي", dismissed: true));
    }
}
