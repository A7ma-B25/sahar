using Sahar.Core.Menu;
using Sahar.Core.Settings;
using P = Sahar.Core.Prayer.Prayer;

namespace Sahar.Core.Tests.Menu;

public class MenuHeaderFactTests
{
    private static readonly MenuFacts AllFacts = new(TimeSpan.FromSeconds(24 * 60 + 10), true, (P.Asr, TimeSpan.FromMinutes(42)), true,
        TimeSpan.FromMinutes(20), false);

    [Fact]
    public void Urgent_FollowsTheOrder()
    {
        Assert.Equal(new HeaderFact("timer", "المؤقت: باقي ٢٤:١٠", "timer"), MenuHeaderFact.Pick(AllFacts, MenuHeader.Urgent));
        var f = AllFacts with { TimerLeft = null };
        Assert.Equal(new HeaderFact("claude", "Claude ناطر جوابك", null), MenuHeaderFact.Pick(f, MenuHeader.Urgent));
        f = f with { ClaudeWaiting = false };
        Assert.Equal(new HeaderFact("prayer", "العصر بعد ٤٢ دقيقة", "prayer"), MenuHeaderFact.Pick(f, MenuHeader.Urgent));
        f = f with { NextPrayer = null };
        Assert.Equal(new HeaderFact("quran", "عم نسمع قرآن", "listen"), MenuHeaderFact.Pick(f, MenuHeader.Urgent));
        f = f with { QuranPlaying = false };
        Assert.Equal(new HeaderFact("hush", "رح ارجع احكي بعد ٢٠ دقيقة", null), MenuHeaderFact.Pick(f, MenuHeader.Urgent));
        Assert.Equal(new HeaderFact("hush", "ساكتة لحد ما تقلي", null), MenuHeaderFact.Pick(f with { HushLeft = null, HushUntilOff = true }, MenuHeader.Urgent));
        Assert.Null(MenuHeaderFact.Pick(f with { HushLeft = null }, MenuHeader.Urgent));
    }

    [Fact]
    public void Timer_FromAnHour_ShowsHours()
        => Assert.Equal("المؤقت: باقي ١:٠٥:٠٩", MenuHeaderFact.Pick(AllFacts with { TimerLeft = new TimeSpan(1, 5, 9) }, MenuHeader.Urgent)!.Text);

    [Fact]
    public void Prayer_And_None()
    {
        Assert.Null(MenuHeaderFact.Pick(AllFacts with { NextPrayer = null }, MenuHeader.Prayer));
        Assert.Equal("prayer", MenuHeaderFact.Pick(AllFacts, MenuHeader.Prayer)!.Kind);
        Assert.Null(MenuHeaderFact.Pick(AllFacts, MenuHeader.None));
    }

    [Fact]
    public void Greeting()
    {
        Assert.Equal("عم اسمعك…", MenuHeaderFact.Greeting(true, "x", true));
        Assert.Equal("حفظتها.", MenuHeaderFact.Greeting(false, "حفظتها.", true));
        Assert.Equal("عم اسكت هلق", MenuHeaderFact.Greeting(false, null, true));
        Assert.Equal("شو بدك نعمل؟", MenuHeaderFact.Greeting(false, "", false));
    }
}
