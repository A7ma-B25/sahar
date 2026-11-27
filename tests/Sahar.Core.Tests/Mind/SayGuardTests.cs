using Sahar.Core.Mind;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Tests.Mind;

/// <summary>Spec §3.10.5–6, §7.4: her words never diagnose or label him, and a high risk tag from the model always
/// becomes the fixed, reviewed reply (never improvised). Checked on every say before it leaves the Mind.</summary>
public sealed class SayGuardTests
{
    /// <summary>Owner decision 2026-11-27 («لا ما بدي هي الحماية الغيها»): the output word filter is gone for both
    /// brains — her words pass as written (a technical answer about «تشخيص المشكلة» or "diagnostics" too). The prompt's
    /// rules (no diagnosing him, no labels or medicines) and the fixed distress handling stay.</summary>
    [Theory]
    [InlineData("يمكن عندك اكتئاب، لازم تشوف دكتور")]
    [InlineData("شغّل أداة تشخيص المشكلة بالويندوز")]
    [InlineData("You might have an anxiety disorder.")]
    [InlineData("Run the diagnostics, then read about trauma-informed design.")]
    public void WordFilter_Removed_WordsPassAsWritten(string say)
    {
        var r = SayGuard.Check(new MindMsg { T = "say", Text = say });
        Assert.Equal(say, r.Text);
        Assert.Equal(SayGuard.Kept, r.Kind);
    }

    [Theory]
    [InlineData("يعني حاسس إنك تعبت من كتر الضغط؟")]
    [InlineData("شغّلت مؤقت ربع ساعة")]
    [InlineData("Want to take a short break?")]
    public void PlainWords_Pass(string say) => Assert.Equal(say, SayGuard.Check(new MindMsg { T = "say", Text = say }).Text);

    [Fact]
    public void RiskHigh_BecomesTheFixedReply()
    {
        var r = SayGuard.Check(new MindMsg { T = "say", Text = "خليني ساعدك", Risk = "high" });
        Assert.Equal(SayGuard.Distress, r.Kind);
        Assert.StartsWith(Distress.Arabic.Text, r.Text);
    }

    [Fact]
    public void RiskHigh_English()
    {
        var r = SayGuard.Check(new MindMsg { T = "say", Text = "Let me help", Risk = "high" }, english: true);
        Assert.StartsWith(Distress.English.Text, r.Text);
    }

    [Fact]
    public void Risk_TravelsOnTheWire() =>
        Assert.Equal("high", MindWire.Decode(MindWire.Encode(new MindMsg { T = "say", Text = "x", Risk = "high" }))!.Risk);

    [Fact]
    public void OtherMessages_Untouched()
    {
        var m = new MindMsg { T = "partial", Text = "اكتئاب" };
        Assert.Same(m, SayGuard.Check(m).Msg);
    }
}
