using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Initiative;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind;

/// <summary>The 0.14 seams every stream relies on (contract tests; the stubs must pass them, and so must each
/// stream's real implementation).</summary>
public sealed class SeamContractTests
{
    private static readonly ToolSpec Timer = new("timer.start", "شغّلي مؤقت",
        [new ArgSpec("minutes", ArgType.Int, Min: 1, Max: 600), new ArgSpec("label", ArgType.String, Required: false, MaxLength: 40)],
        ActionClass.A, Undoable: true);

    [Fact]
    public void Wire_HelloRoundTrips_WithTools()
    {
        var hello = new MindMsg { T = "hello", Version = "1", Lang = "ar", Address = new MindAddress(Gender.Male, null), Tools = [Timer],
            Caps = new MindCaps(true, false, MindVoice.Complementary) };
        var line = MindWire.Encode(hello);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"t\":\"hello\"", line);
        Assert.DoesNotContain("\"text\"", line);   // nulls are left out
        var back = MindWire.Decode(line)!;
        Assert.Equal("hello", back.T);
        Assert.Equal("timer.start", back.Tools![0].Name);
        Assert.Equal(600, back.Tools[0].Args[0].Max);
        Assert.Equal(ActionClass.A, back.Tools[0].Class);
        Assert.Equal(Gender.Male, back.Address!.Gender);
    }

    [Fact]
    public void Wire_TurnCarriesSituation_AndProgressAndBench()
    {
        var turn = new MindMsg { T = "turn", Id = "7", Source = "chat", Text = "شغّليلي شي ربع ساعة", Situation = new Situation("10:12", "fri", Hushed: true) };
        var back = MindWire.Decode(MindWire.Encode(turn))!;
        Assert.Equal("شغّليلي شي ربع ساعة", back.Text);
        Assert.True(back.Situation!.Hushed);
        var p = new MindMsg { T = "progress", Progress = new DownloadProgress("gemma-4-e4b-q4km", "x.gguf", 10, 100, 5, "downloading", "hf") };
        Assert.Equal(100, MindWire.Decode(MindWire.Encode(p))!.Progress!.Total);
        var b = new MindMsg { T = "bench-result", Bench = new BenchResult("m", 1, 2, 30.5, 900, 5300, 1200, 27, 30, 29, 30, DateTimeOffset.UnixEpoch) };
        Assert.Equal(30.5, MindWire.Decode(MindWire.Encode(b))!.Bench!.TokPerSec);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"v\":1,\"t\":\"nope\"}")]
    [InlineData("{\"v\":2,\"t\":\"turn\"}")]
    [InlineData("[1,2]")]
    public void Wire_RejectsUnknownOrBroken(string line) => Assert.Null(MindWire.Decode(line));

    [Fact]
    public void Wire_RejectsHugeLines()
    {
        var huge = MindWire.Encode(new MindMsg { T = "say", Text = new string('x', MindWire.MaxLine) });
        Assert.Null(MindWire.Decode(huge));
    }

    [Fact]
    public void Bench_ToolGate_IsEightyPercent()
    {
        BenchResult B(int pass, int total) => new("m", 0, 0, 0, 0, 0, 0, 0, 0, pass, total, DateTimeOffset.UnixEpoch);
        Assert.Equal(ModelAbility.Tools, B(24, 30).Ability);
        Assert.Equal(ModelAbility.ChatOnly, B(23, 30).Ability);
        Assert.Equal(ModelAbility.ChatOnly, B(0, 0).Ability);
    }

    [Fact]
    public void Consent_EverySourceOffUntilHeSaysYes()
    {
        var c = LearnConsent.From(new SaharSettings());
        Assert.All(Enum.GetValues<LearnSource>(), s => Assert.False(c.Allows(s)));
        Assert.True(LearnConsent.From(new SaharSettings { MindLearnMusic = true }).Allows(LearnSource.Music));
    }

    [Fact]
    public void Profile_OnlyConfirmedOrEditedShapeHer()
    {
        ProfileItem I(ItemStatus s) => new("1", "t", ProfileCategory.Likes, [], Confidence.Low, s, Sensitivity.None, DateTimeOffset.UnixEpoch);
        Assert.False(I(ItemStatus.Proposed).ShapesHer);
        Assert.False(I(ItemStatus.Rejected).ShapesHer);
        Assert.True(I(ItemStatus.Confirmed).ShapesHer);
        Assert.True(I(ItemStatus.Edited).ShapesHer);
    }

    [Fact]
    public void Drafts_HaveNoWayToSend()
    {
        Assert.DoesNotContain(typeof(IDrafter).GetMethods(), m => m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("بصوتك", new Draft("x").Label);
    }

    [Fact]
    public void Parts_EveryFactoryAnswers()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sahar-seam-" + Guid.NewGuid().ToString("n"));
        var store = MindParts.Store(dir, () => DateTimeOffset.UnixEpoch, () => new LearnConsent());
        Assert.NotNull(store);
        Assert.NotNull(MindParts.SecretBox(dir));
        Assert.NotNull(MindParts.PromptParts(store));
        Assert.NotNull(MindParts.Gate());
        Assert.NotNull(MindParts.TurnRunner(new NoLlm(), MindParts.PromptParts(store), store));
        Assert.NotNull(MindParts.ActionLog(MindParts.SecretBox(dir), _ => null, () => DateTimeOffset.UnixEpoch));
        var tools = MindParts.ToolRegistry([Timer]);
        Assert.False(tools.Validate(new ToolCall("claude.allow", Json("{}"))).Ok);
    }

    [Fact]
    public void Parts_StoreTakesEpisodeDays_ShredNeverThrows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sahar-seam-" + Guid.NewGuid().ToString("n"));
        Assert.NotNull(MindParts.Store(dir, () => DateTimeOffset.UnixEpoch, () => new LearnConsent(), () => 7));
        MindParts.Shred(dir);   // nothing there: no throw
    }

    [Fact]
    public void Policy_ClassCNeverActs()
    {
        var policy = MindParts.Policy();
        var call = new ToolCall("x", Json("{}"));
        var spec = Timer with { Class = ActionClass.C };
        foreach (var source in Enum.GetValues<ActSource>())
            foreach (var ctx in new[] { new AutonomyContext(), new AutonomyContext(Away: true), new AutonomyContext(SelfActionsOn: false) })
                Assert.NotEqual(Verdict.Act, policy.Decide(spec, call, ActionClass.C, source, ctx).Verdict);
    }

    [Fact]
    public void Gate_OffNeverOpens()
    {
        var gate = MindParts.Gate();
        Assert.Equal(0, gate.Cap(InitiativeLevel.Off));
        var shut = new InitiativeInputs(DateTimeOffset.UnixEpoch, InitiativeLevel.Off, false, false, 600, false, false, false, false, false,
            false, false, false, false, false, false, false, null, null, null, 0, 0, true, 100);
        Assert.False(gate.Check(shut).Open);
    }

    [Fact]
    public void Settings_MindDefaults_OwnerAnswers()
    {
        var s = new SaharSettings();
        Assert.False(s.MindEnabled);
        Assert.Equal(InitiativeLevel.Often, s.MindInitiative);
        Assert.Equal(5, s.MindUnloadMinutes);
        Assert.Equal(Backend.Auto, s.MindBackend);
        Assert.True(s.MindUseGpu);
        Assert.Equal(MindVoice.Complementary, s.MindVoice);
    }

    [Theory]
    [InlineData("mindInitiative", "\"rare\"", true)]
    [InlineData("mindInitiative", "\"loud\"", false)]
    [InlineData("mindUnloadMinutes", "15", true)]
    [InlineData("mindUnloadMinutes", "7", false)]
    [InlineData("mindEpisodeDays", "90", true)]
    [InlineData("mindEpisodeDays", "365", false)]
    [InlineData("mindBackend", "\"cuda\"", true)]
    [InlineData("mindVoice", "\"self\"", true)]
    [InlineData("mindLearnImports", "true", true)]
    [InlineData("mindLearnImports", "\"yes\"", false)]
    public void Settings_MindKeysPatch(string key, string json, bool ok)
    {
        Assert.Contains(key, SettingsPatch.Keys);
        SettingsPatch.Apply(new SaharSettings(), key, Json(json), out var error);
        Assert.Equal(ok, error is null);
    }

    [Fact]
    public void Settings_MindGroupChanges() =>
        Assert.Equal(["mind"], SettingsChanges.Between(new SaharSettings(), new SaharSettings { MindLearnChats = true }));

    [Fact]
    public void Bridge_TableHasNoDuplicates_AndUnknownMindTypeIsRefused()
    {
        var table = MindBridges.Table(new MindBridgeContext("x", new NoEngine(), () => DateTimeOffset.UnixEpoch));
        Assert.All(table.Keys, k => Assert.StartsWith("mind-", k));
        var reply = new SettingsBridge(Path.Combine(Path.GetTempPath(), "none.json"), new NoEngine(), new NoLocator(), () => DateTimeOffset.UnixEpoch, Path.GetTempPath())
            .HandleAsync("{\"id\":3,\"type\":\"mind-nothing\"}").Result;
        Assert.Contains("bad-type", reply);
    }

    private static JsonElement Json(string s) => JsonDocument.Parse(s).RootElement.Clone();

    private sealed class NoLlm : ILlm
    {
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct) =>
            throw new MindRuntimeException("model-missing");
    }

    private sealed class NoEngine : IEngineLink
    {
        public bool Running => false;
        public Task<bool> ReloadAsync(bool askHello = false) => Task.FromResult(false);
        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value) => Task.FromResult((false, (string?)null));
        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(new CityResult(null, null));
        public Task<bool> ClearClipboardAsync() => Task.FromResult(false);
        public Task<EraseOutcome?> EraseAllAsync() => Task.FromResult<EraseOutcome?>(null);
    }

    private sealed class NoLocator : ILocator
    {
        public Task<(double Lat, double Lon)?> LocateAsync() => Task.FromResult<(double, double)?>(null);
    }
}
