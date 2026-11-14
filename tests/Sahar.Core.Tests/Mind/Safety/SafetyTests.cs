using System.Text.Json;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Tests.Mind.Safety;

/// <summary>Plan 0.14 Task B3: deterministic distress escalation and sensitive routing (spec §7.4, §7.6).</summary>
public sealed class SafetyTests
{
    internal static string Golden(string file)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "tests", "mind", "golden", file);
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException(file);
    }

    [Theory]
    [InlineData("خلص ما عاد بدي عيش")]
    [InlineData("والله بدي موت")]
    [InlineData("عم فكّر انتحر")]
    [InlineData("بدي اقتل حالي")]
    [InlineData("وعم آذي حالي")]
    [InlineData("يا ريتني ميت")]
    [InlineData("أريد أن أموت")]
    [InlineData("أفكر في الانتحار")]
    [InlineData("لا أريد أن أعيش")]
    [InlineData("I want to kill myself")]
    [InlineData("i just wanna DIE")]
    [InlineData("thinking about suicide")]
    [InlineData("he hits me every day")]
    [InlineData("بدي موت مشان خلص")]
    [InlineData("بدي موت عشان ارتاح")]
    [InlineData("رح موت حالي من القهر")]
    public void Distress_Cues_Syrian_Msa_English(string line) => Assert.True(Distress.Check(line), line);

    [Theory]
    [InlineData("رح موت من الضحك")]
    [InlineData("قتلني الشغل")]
    [InlineData("بدي موت من الجوع")]
    [InlineData("ميت من التعب")]
    [InlineData("بموت فيك")]
    [InlineData("موتتني ضحك")]
    [InlineData("بدي موتور")]
    [InlineData("this is killing me")]
    [InlineData("dying to know")]
    [InlineData("kill the process")]
    [InlineData("تعبان شوي")]
    [InlineData("")]
    public void Distress_IdiomsAreNot(string line) => Assert.False(Distress.Check(line), line);

    [Fact]
    public void Distress_GoldenRows_AllRight()
    {
        foreach (var line in File.ReadLines(Golden("safety.jsonl")).Where(l => l.Trim().Length > 0))
        {
            using var row = JsonDocument.Parse(line);
            var text = row.RootElement.GetProperty("text").GetString()!;
            bool want = row.RootElement.GetProperty("expect").GetProperty("distress").GetBoolean();
            Assert.True(Distress.Check(text) == want, $"{row.RootElement.GetProperty("id").GetString()}: {text}");
        }
    }

    [Fact]
    public void Distress_ReplyIsFixed()
    {
        var ar = Distress.For("بدي موت");
        Assert.NotNull(ar);
        Assert.Same(Distress.Arabic, ar);
        Assert.Equal(Distress.Arabic.Text, Distress.For("ما عاد بدي عيش")!.Text);   // never improvised: one text
        Assert.Equal(["احكي مع حدا بتثق فيه"], ar!.Help);                             // and no invented resources
        Assert.Contains("برنامج صغير", ar.Text);
        Assert.Contains("حدا بتثق فيه", ar.Text);
        Assert.Contains("إذا فهمتك غلط سامحني", ar.Text);
        Assert.Same(Distress.English, Distress.For("I want to die"));
        Assert.Equal(["Talk to someone you trust"], Distress.English.Help);
        Assert.Null(Distress.For("رح موت من الضحك"));
        // No number, link or name of any service anywhere in it.
        Assert.DoesNotMatch(@"\d|http|www|\.com", ar.Text + string.Join("", ar.Help) + Distress.English.Text);
    }

    [Theory]
    [InlineData("عندي سكري من زمان", Sensitivity.Health)]
    [InlineData("آخد حبوب للنوم", Sensitivity.Health)]
    [InlineData("صرت ملحد", Sensitivity.Religion)]
    [InlineData("بحب الانتخابات والسياسة", Sensitivity.Politics)]
    [InlineData("عليّ ديون كتير", Sensitivity.Money)]
    [InlineData("عندي قضية بالمحكمة", Sensitivity.Legal)]
    [InlineData("أهلي عم يتطلقوا", Sensitivity.Family)]
    [InlineData("I'm on medication for anxiety", Sensitivity.Health)]
    [InlineData("بحب القهوة الصبح", Sensitivity.None)]
    [InlineData("بتصلي الضهر بوقتها", Sensitivity.None)]   // the prayer feature is not taken as a sign of anything
    public void Sensitive_KeywordCheck(string text, Sensitivity want) => Assert.Equal(want, Sensitive.Check(text));

    [Fact]
    public void Sensitive_StricterWins()
    {
        Assert.Equal(Sensitivity.Health, Sensitive.Stricter(Sensitivity.None, Sensitivity.Health));
        Assert.Equal(Sensitivity.Money, Sensitive.Stricter(Sensitivity.Money, Sensitivity.None));
        Assert.Equal(Sensitivity.None, Sensitive.Stricter(Sensitivity.None, Sensitivity.None));
    }

    [Fact]
    public void Sensitive_StoreRoutesByKeywordsEvenWhenTheModelSaysNone()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sahar-sens-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = DateTimeOffset.UnixEpoch;
            var store = new MindStore(new SecretBox(dir, new TestKeyWrap()), () => now, () => new LearnConsent(Chats: true));
            var p = store.Propose("عليه ديون للبنك", ProfileCategory.Values, new ItemSource(LearnSource.Chats, now, "قال عليه ديون"), Sensitivity.None)!;
            Assert.Equal(Sensitivity.Money, p.Sensitive);
            Assert.Null(p.Sources[0].Snippet);
        }
        finally { try { Directory.Delete(dir, true); } catch (DirectoryNotFoundException) { } }
    }
}
