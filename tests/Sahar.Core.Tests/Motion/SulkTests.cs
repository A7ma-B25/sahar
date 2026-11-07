using Sahar.Core.Motion;

namespace Sahar.Core.Tests.Motion;

public class SulkTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sulk_TimesOut_EndsAndSaysSo()
    {
        var s = new Sulk();
        int gen = s.Start(T0);
        Assert.True(s.Active(T0.AddSeconds(19)));
        // Her timer fires a little after the 20 s: the sulk still ends (the 0.8 angry face stayed forever).
        Assert.True(s.TimerFired(gen));
        Assert.False(s.Active(T0.AddSeconds(21)));
        Assert.False(s.TimerFired(gen));   // once
    }

    [Fact]
    public void Sulk_AnOlderTimer_NeverEndsANewerSulk()
    {
        var s = new Sulk();
        int a = s.Start(T0);
        Assert.Equal(SulkPoke.Hmph, s.Poke());
        Assert.Equal(SulkPoke.Hmph, s.Poke());
        Assert.Equal(SulkPoke.MadeUp, s.Poke());   // the third poke makes up
        int b = s.Start(T0.AddSeconds(10));
        Assert.False(s.TimerFired(a));
        Assert.True(s.Active(T0.AddSeconds(20.5)));
        Assert.True(s.TimerFired(b));
    }

    [Fact]
    public void Sulk_NotSulking_PokeIsNormal()
    {
        var s = new Sulk();
        Assert.Equal(SulkPoke.NotSulking, s.Poke());
        s.Start(T0);
        Assert.True(s.End());
        Assert.False(s.End());
        Assert.Equal(SulkPoke.NotSulking, s.Poke());
    }
}
