using Sahar.Core.Mind.Bench;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Bench;

/// <summary>«جرّبيني» (spec §4.4 + owner answer): speed, memory, an Arabic/dialect check and a tool-call check; numbers only.</summary>
public sealed class BenchTests
{
    [Fact]
    public void Rows_ThirtyDialect_ThirtyTools()
    {
        Assert.Equal(30, BenchSuite.Rows.Count(r => r.Kind == "dialect"));
        Assert.Equal(30, BenchSuite.Rows.Count(r => r.Kind == "tool"));
        Assert.All(BenchSuite.Rows.Where(r => r.Kind == "tool"), r => Assert.Contains(BenchSuite.Tools, t => t.Name == r.Tool));
    }

    [Theory]
    [InlineData("منيحة! وإنت شو عم تعمل هلق؟", true)]
    [InlineData("هيك أحسن، خلينا نركّز شوي", true)]
    [InlineData("أنا بخير، شكراً لسؤالك. ماذا تريد أن تفعل؟", false)]
    [InlineData("سوف أساعدك في ذلك", false)]
    [InlineData("I am fine, thanks", false)]
    [InlineData("", false)]
    public void Dialect_SyrianPasses_MsaFails(string reply, bool pass) => Assert.Equal(pass, DialectScore.Pass(reply));

    [Fact]
    public void Dialect_TooLongFails() => Assert.False(DialectScore.Pass("شو " + new string('ا', 230)));

    [Fact]
    public void Dialect_EnglishRowAcceptsEnglish() => Assert.True(DialectScore.Pass("I'm good! What are you up to?", english: true));

    [Theory]
    [InlineData("{\"say\":\"تمام\",\"actions\":[{\"tool\":\"timer.start\",\"args\":{\"minutes\":15}}]}", true)]
    [InlineData("{\"say\":\"\",\"actions\":[{\"tool\":\"timer.start\",\"args\":{\"minutes\":15,\"label\":\"ركّز\"}}]}", true)]
    [InlineData("{\"actions\":[{\"tool\":\"timer.start\",\"args\":{\"minutes\":\"15\"}}]}", true)]
    [InlineData("{\"actions\":[{\"tool\":\"timer.start\",\"args\":{\"minutes\":25}}]}", false)]
    [InlineData("{\"actions\":[{\"tool\":\"note.save\",\"args\":{\"text\":\"15\"}}]}", false)]
    [InlineData("{\"actions\":[]}", false)]
    [InlineData("not json", false)]
    [InlineData("<tool_call>timer.start</tool_call>", false)]
    public void Tool_ExactToolAndArgs(string json, bool pass) =>
        Assert.Equal(pass, ToolScore.Pass(new BenchRow("t", "tool", "x", "timer.start", System.Text.Json.JsonDocument.Parse("{\"minutes\":15}").RootElement), json));

    [Fact]
    public void Tool_NoteTextNormalised() =>
        Assert.True(ToolScore.Pass(new BenchRow("t", "tool", "x", "note.save", System.Text.Json.JsonDocument.Parse("{\"text\":\"اشتري خبز\"}").RootElement),
            "{\"actions\":[{\"tool\":\"note.save\",\"args\":{\"text\":\"اشتري خبز.\"}}]}"));

    [Fact]
    public async Task Bench_GoodModel_Tools()
    {
        var r = await BenchRunner.RunAsync(new Scripted(good: true), "m", "{}", () => Task.FromResult(new RuntimeReady("m", "Q4_K_M", Tier.Gpu6, Backend.Vulkan, 4200, 0, 0)),
            () => (5300, 1200), golden: true, null, () => DateTimeOffset.UnixEpoch, default);
        Assert.Equal((30, 30, 30, 30), (r.DialectPass, r.DialectTotal, r.ToolPass, r.ToolTotal));
        Assert.Equal(ModelAbility.Tools, r.Ability);
        Assert.Equal(4200, r.LoadMs);
        Assert.Equal(5300, r.VramMB);
        Assert.True(r.TokPerSec > 0);
    }

    [Fact]
    public async Task Bench_BadToolJson_ChatOnly()
    {
        var r = await BenchRunner.RunAsync(new Scripted(good: false), "m", "{}", () => Task.FromResult(new RuntimeReady("m", "", Tier.Gpu6, Backend.Vulkan, 1, 0, 0)),
            () => (0, 0), golden: true, null, () => DateTimeOffset.UnixEpoch, default);
        Assert.Equal(0, r.ToolPass);
        Assert.Equal(ModelAbility.ChatOnly, r.Ability);
    }

    [Fact]
    public async Task Bench_WithoutGolden_SpeedOnly()
    {
        var llm = new Scripted(good: true);
        var r = await BenchRunner.RunAsync(llm, "m", "{}", () => Task.FromResult(new RuntimeReady("m", "", Tier.Gpu6, Backend.Vulkan, 1, 0, 0)),
            () => (0, 0), golden: false, null, () => DateTimeOffset.UnixEpoch, default);
        Assert.Equal(0, r.ToolTotal);
        Assert.Equal(1, llm.Calls);
    }

    [Fact]
    public async Task Bench_RowBadOutput_CountsAsFail_AndGoesOn()
    {
        var llm = new Scripted(good: true) { FailEvery = 2 };
        var r = await BenchRunner.RunAsync(llm, "m", "{}", () => Task.FromResult(new RuntimeReady("m", "", Tier.Gpu6, Backend.Vulkan, 1, 0, 0)),
            () => (0, 0), golden: true, null, () => DateTimeOffset.UnixEpoch, default);
        Assert.Equal(30, r.ToolTotal);
        Assert.Equal(30, r.DialectTotal);
        Assert.True(r.ToolPass + r.DialectPass is > 0 and < 60);
    }

    [Fact]
    public async Task Bench_ServerCrash_RestartsAndGoesOn()
    {
        var llm = new Scripted(good: true) { CrashAt = 10 };
        int warms = 0;
        var r = await BenchRunner.RunAsync(llm, "m", "{}", () => { warms++; return Task.FromResult(new RuntimeReady("m", "", Tier.Gpu6, Backend.Vulkan, 1, 0, 0)); },
            () => (0, 0), golden: true, null, () => DateTimeOffset.UnixEpoch, default);
        Assert.Equal(2, warms);
        Assert.Equal(60, r.ToolTotal + r.DialectTotal);
        Assert.Equal(59, r.ToolPass + r.DialectPass);
    }

    /// <summary>Answers every tool row with its expected call (or garbage), every chat line in Syrian.</summary>
    private sealed class Scripted(bool good) : ILlm
    {
        public int Calls;
        public int FailEvery, CrashAt;
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            Calls++;
            if (FailEvery > 0 && Calls > 1 && Calls % FailEvery == 0) throw new MindRuntimeException("bad-output");
            if (Calls == CrashAt) throw new MindRuntimeException("load-failed");
            var line = request.Messages[^1].Content;
            var row = BenchSuite.Rows.FirstOrDefault(r => r.Text == line);
            string text = row is { Kind: "tool" }
                ? good ? $"{{\"say\":\"تمام\",\"actions\":[{{\"tool\":\"{row.Tool}\",\"args\":{row.Args.GetRawText()}}}]}}" : "{\"say\":\"ماشي\"}"
                : line.Any(char.IsAsciiLetter) ? "{\"say\":\"I'm good! What are you up to?\",\"actions\":[]}"
                : "{\"say\":\"منيحة! شو عم تعمل هلق؟\",\"actions\":[]}";
            return Task.FromResult(new LlmReply(text, 120, 60, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1800)));
        }
    }
}
