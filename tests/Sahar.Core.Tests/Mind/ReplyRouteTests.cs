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
