using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Autonomy;

/// <summary>0.19 spec §5/§7: MCP and media tools — class B by default, «بس بتقرا» acts, «دايماً» acts unless sensitive
/// (D4) or the turn is tainted by untrusted output; never for her own (Self) turns; class C stays refused.</summary>
public sealed class AutonomyPolicyToolTests
{
    private static readonly IAutonomyPolicy Policy = MindParts.Policy();
    private static readonly ToolCall Call = new("mcp.files.read_file", JsonDocument.Parse("{}").RootElement.Clone());

    private static ToolSpec Mcp(ToolGrant grant, bool sensitive = false) =>
        new("mcp.files.read_file", "x", [], grant == ToolGrant.Read && !sensitive ? ActionClass.Read : ActionClass.B,
            Source: ToolSource.Mcp, ServerId: "s1", Sensitive: sensitive, Grant: grant);

    private static AutonomyDecision D(ToolSpec spec, ActSource source = ActSource.User, bool tainted = false) =>
        Policy.Decide(spec, Call, spec.Class, source, new AutonomyContext(Tainted: tainted));

    [Fact] public void Always_Acts() => Assert.Equal(Verdict.Act, D(Mcp(ToolGrant.Always)).Verdict);
    [Fact] public void Read_Acts() => Assert.Equal(Verdict.Act, D(Mcp(ToolGrant.Read)).Verdict);
    [Fact] public void Ask_Asks() => Assert.Equal(new AutonomyDecision(Verdict.Ask, "b-class"), D(Mcp(ToolGrant.Ask)));
    [Fact] public void Sensitive_AlwaysStillAsks() => Assert.Equal(new AutonomyDecision(Verdict.Ask, "sensitive"), D(Mcp(ToolGrant.Always, sensitive: true)));
    [Fact] public void Tainted_AlwaysAsks() => Assert.Equal(new AutonomyDecision(Verdict.Ask, "tainted"), D(Mcp(ToolGrant.Always), tainted: true));
    [Fact] public void Tainted_ReadAsks() => Assert.Equal(Verdict.Ask, D(Mcp(ToolGrant.Read), tainted: true).Verdict);

    [Fact]
    public void Tainted_BuiltinAsks()
    {
        var timer = ToolCatalog.Get("timer.start")!;
        var call = new ToolCall("timer.start", JsonDocument.Parse("""{"minutes":5}""").RootElement.Clone());
        Assert.Equal(Verdict.Act, Policy.Decide(timer, call, ActionClass.A, ActSource.User, new AutonomyContext()).Verdict);
        Assert.Equal(new AutonomyDecision(Verdict.Ask, "tainted"), Policy.Decide(timer, call, ActionClass.A, ActSource.User, new AutonomyContext(Tainted: true)));
    }

    [Fact] public void Self_MindSideRefused() => Assert.Equal(new AutonomyDecision(Verdict.Refuse, "self-tool"), D(Mcp(ToolGrant.Always), ActSource.Self));

    [Fact]
    public void ClassC_StillRefused()
    {
        var c = new ToolSpec("mcp.x.y", "", [], ActionClass.C, Source: ToolSource.Mcp, Grant: ToolGrant.Always);
        Assert.Equal(Verdict.Refuse, Policy.Decide(c, Call, ActionClass.C, ActSource.User, new AutonomyContext(Tainted: true)).Verdict);
    }

    [Fact]
    public void Media_DefaultAsks() => Assert.Equal(Verdict.Ask, D(MediaTools.Image).Verdict);

    [Theory]
    [InlineData(ToolGrant.Always, false, ToolGrant.Always)]
    [InlineData(ToolGrant.Read, false, ToolGrant.Read)]
    [InlineData(ToolGrant.Always, true, ToolGrant.Ask)]
    [InlineData(ToolGrant.Read, true, ToolGrant.Ask)]
    public void Effective_SensitiveIsAlwaysAsk(ToolGrant g, bool sensitive, ToolGrant want) => Assert.Equal(want, ToolGrants.Effective(g, sensitive));
}
