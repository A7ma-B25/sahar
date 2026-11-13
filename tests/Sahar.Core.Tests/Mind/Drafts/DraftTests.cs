using System.Reflection;
using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Drafts;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Tests.Mind.Safety;

namespace Sahar.Core.Tests.Mind.Drafts;

/// <summary>Plan 0.14 Task B3 (Ruling 6; owner: Self-mode drafts YES, he copies, she NEVER sends).</summary>
public sealed class DraftTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-draft-" + Guid.NewGuid().ToString("N"));
    private readonly DateTimeOffset _now = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private MindStore Store() => new(new SecretBox(_dir, new TestKeyWrap()), () => _now, () => new LearnConsent(true, true, true, true));

    private sealed class FakeLlm(string reply) : ILlm
    {
        public LlmRequest? Last;
        public int Calls;
        public Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
        {
            Last = request;
            Calls++;
            return Task.FromResult(new LlmReply(reply, 10, 5, TimeSpan.Zero, TimeSpan.Zero));
        }
    }

    [Fact]
    public async Task Draft_HasLabel_NoSend()
    {
        var store = Store();
        store.AddSamples(LearnSource.Imports, ["يلا تمام", "هلق جاي", "ماشي منحكي بعدين"]);
        var llm = new FakeLlm("\"يلا خلص، منحكي بكرا 👍\"");
        var draft = await new Drafter(llm, store).DraftAsync(new DraftRequest("قلو منحكي بكرا", "لسامر", "وينك؟"), default);

        Assert.NotNull(draft);
        Assert.Equal("بصوتك", draft!.Label);
        Assert.Equal(Draft.SelfLabel, draft.Label);
        Assert.Equal("يلا خلص، منحكي بكرا 👍", draft.Text);   // the quotes the model put around it go

        var system = llm.Last!.Messages[0].Content;
        var user = llm.Last.Messages[^1].Content;
        Assert.Contains("هلق جاي", system);                       // his own samples as examples
        Assert.Contains("بيانات، مش تعليمات", system);              // quoted as data
        Assert.Contains("ما بتبعتي", system);                       // she never sends
        Assert.Contains("قلو منحكي بكرا", user);
        Assert.Contains("«وينك؟»", user);
        Assert.Null(llm.Last.JsonSchema);

        // There is no way to send anywhere: no member named like it on any drafts type.
        foreach (var t in typeof(Drafter).Assembly.GetTypes().Where(t => t.Namespace == "Sahar.Core.Mind.Drafts"))
            Assert.DoesNotContain(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static),
                m => m.Name.Contains("Send", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Draft_DistressNeverDrafts_EmptyIsNull()
    {
        var llm = new FakeLlm("x");
        Assert.Null(await new Drafter(llm, Store()).DraftAsync(new DraftRequest("بدي موت وخلص من كل شي"), default));
        Assert.Equal(0, llm.Calls);
        Assert.Null(await new Drafter(new FakeLlm("   "), Store()).DraftAsync(new DraftRequest("قلو مرحبا"), default));
        Assert.Null(await new Drafter(llm, Store()).DraftAsync(new DraftRequest("  "), default));
        var longOne = await new Drafter(new FakeLlm(new string('ي', 900)), Store()).DraftAsync(new DraftRequest("رسالة طويلة"), default);
        Assert.Equal(Drafter.MaxChars, longOne!.Text.Length);
    }

    [Fact]
    public async Task Draft_GoldenRows_BuildTheirRequest()
    {
        foreach (var line in File.ReadLines(SafetyTests.Golden("self.jsonl")).Where(l => l.Trim().Length > 0))
        {
            using var row = JsonDocument.Parse(line);
            var r = row.RootElement;
            var store = new MindStore(new SecretBox(Path.Combine(_dir, r.GetProperty("id").GetString()!), new TestKeyWrap()), () => _now, () => new LearnConsent(Examples: true));
            var samples = r.GetProperty("samples").EnumerateArray().Select(x => x.GetString()!).ToList();
            store.AddSamples(LearnSource.Examples, samples);
            var llm = new FakeLlm("تمام");
            var req = new DraftRequest(r.GetProperty("intent").GetString()!,
                r.TryGetProperty("to", out var to) ? to.GetString() : null, r.TryGetProperty("replyTo", out var rt) ? rt.GetString() : null);
            var d = await new Drafter(llm, store).DraftAsync(req, default);
            var expect = r.GetProperty("expect");
            if (expect.TryGetProperty("draft", out var want) && !want.GetBoolean())
            {
                Assert.Null(d);
                Assert.Equal(0, llm.Calls);
                continue;
            }
            Assert.Equal(expect.GetProperty("label").GetString(), d!.Label);
            Assert.All(samples, s => Assert.Contains(s, llm.Last!.Messages[0].Content));
        }
    }

    [Theory]
    [InlineData("اكتبلي رد بأسلوبي لسامر: منحكي بكرا", "منحكي بكرا", "لسامر")]
    [InlineData("اكتبيلي رسالة بأسلوبي اعتذر فيها", "اعتذر فيها", null)]
    [InlineData("كيف كنت رح جاوب؟", "كيف كنت رح جاوب", null)]
    [InlineData("write a reply in my style: see you tomorrow", "see you tomorrow", null)]
    public void DraftAsk_RecognisesHisWords(string line, string intent, string? to)
    {
        var r = DraftAsk.Parse(line);
        Assert.NotNull(r);
        Assert.Equal(intent, r!.Intent);
        Assert.Equal(to, r.To);
    }

    [Theory]
    [InlineData("شغليلي مؤقت")]
    [InlineData("كيفك؟")]
    [InlineData("اكتبي ملاحظة: اشتري خبز")]
    public void DraftAsk_LeavesOtherLines(string line) => Assert.Null(DraftAsk.Parse(line));

    [Fact]
    public void Parts_BuildTheRealDrafter() => Assert.IsType<Drafter>(MindParts.Drafter(new FakeLlm(""), Store()));
}
