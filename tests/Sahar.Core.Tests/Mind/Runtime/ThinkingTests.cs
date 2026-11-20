using System.Net;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>0.14.1 fix 1 (owner: "she never replied"): Gemma 4's chat template thinks by default; llama-server b12207
/// streams the thought as <c>reasoning_content</c>, so all 160 tokens went to thinking, <c>content</c> stayed empty and
/// every turn ended in «ما فهمت عليك منيح…» (the 43-character line of every "said"/"initiative line" in his log).</summary>
public sealed class ThinkingTests
{
    private static readonly HardwareProbe HisPc = new(32_510, [new Gpu(Gpu.Nvidia, 6_012, false)], true, 200_000);

    [Fact]
    public void Args_ThinkingOff() =>
        Assert.Equal("off", Val(ServerArgs.For(Tiers.Plan(HisPc), "m", 1, "k", 4), "--reasoning"));

    [Fact]
    public async Task Client_AsksForNoThinking_AndRoomForAWholeDecision()
    {
        var fake = new Sse([Delta("content", "{}")]);
        await new OpenAiClient(fake, 1, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")], "{\"type\":\"object\"}"), null, default);
        using var body = JsonDocument.Parse(fake.Bodies[0]);
        Assert.False(body.RootElement.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean());
        Assert.True(body.RootElement.GetProperty("max_tokens").GetInt32() >= 256);   // Arabic say + remember + JSON
    }

    [Fact]
    public async Task Client_OnlyThinking_IsANamedDrop()
    {
        var fake = new Sse([Delta("reasoning_content", "Thinking Process: 1. Analyze"), Delta("reasoning_content", " the request")]);
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() =>
            new OpenAiClient(fake, 1, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")], "{}"), null, default));
        Assert.Equal("thinking", e.Code);
    }

    [Fact]
    public async Task Client_ThinkingThenContent_KeepsTheContentOnly()
    {
        var fake = new Sse([Delta("reasoning_content", "hmm"), Delta("content", "{\"say\":\"هلق\"}")]);
        var r = await new OpenAiClient(fake, 1, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")]), null, default);
        Assert.Equal("{\"say\":\"هلق\"}", r.Text);
    }

    [Fact]
    public async Task Client_AConnectionLostBeforeAnyByte_IsTriedOnceMore()
    {
        var fake = new Sse([Delta("content", "{}")]) { FailFirst = 1 };
        var r = await new OpenAiClient(fake, 1, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")]), null, default);
        Assert.Equal("{}", r.Text);
        Assert.Equal(2, fake.Bodies.Count);
    }

    [Fact]
    public async Task Client_ServerReallyGone_StillLoadFailed_WithItsKind()
    {
        var fake = new Sse([]) { FailFirst = 9 };
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() =>
            new OpenAiClient(fake, 1, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")]), null, default));
        Assert.Equal("load-failed", e.Code);
        Assert.Equal("ConnectionError", e.Detail);   // a kind, never text
    }

    private static string Val(IReadOnlyList<string> a, string flag) { int i = a.ToList().IndexOf(flag); return i < 0 ? "" : a[i + 1]; }

    private static string Delta(string field, string text) =>
        "data: " + JsonSerializer.Serialize(new Dictionary<string, object> { ["choices"] = new[] { new Dictionary<string, object> { ["delta"] = new Dictionary<string, string> { [field] = text } } } }) + "\n\n";

    private sealed class Sse(string[] events) : HttpMessageHandler
    {
        public int FailFirst;
        public readonly List<string> Bodies = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Bodies.Add(req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct));
            if (FailFirst-- > 0) throw new HttpRequestException(HttpRequestError.ConnectionError, "lost");
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append(e);
            sb.Append("data: [DONE]\n\n");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
        }
    }
}

/// <summary>0.14.1, after the first CI eval (run 44740612947): with thinking off, Gemma 4 still opened a thought on ~1 in
/// 3 lines (reproduced locally with --reasoning off); <c>--reasoning-budget 0</c> ends any thought at once, a turn whose
/// step came back as thought only is tried once more, and her line never stays empty.</summary>
public sealed class ThinkingGuardTests
{
    [Fact]
    public void Args_AThoughtEndsAtOnce()
    {
        var a = ServerArgs.For(Tiers.Plan(new HardwareProbe(32_510, [], true, 200_000)), "m", 1, "k", 4);
        Assert.Equal("0", a[a.ToList().IndexOf("--reasoning-budget") + 1]);
    }
}
