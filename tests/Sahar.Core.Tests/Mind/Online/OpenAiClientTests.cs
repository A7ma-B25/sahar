using System.Text.Json;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>0.17 Task 1: one client for every OpenAI-compatible provider (spec §3, §7), against recorded fixtures.</summary>
public sealed class OpenAiClientTests
{
    private static readonly LlmRequest Hi = new([new ChatMessage("system", "s"), new ChatMessage("user", "مرحبا")], null, Sampling.Online);

    private static JsonElement Body(FakeProvider f, int i = 0) => JsonDocument.Parse(f.Seen[i].Body).RootElement;

    [Fact]
    public async Task Local_KeepsLlamaFields()
    {
        var f = new FakeProvider(FakeProvider.Ok("هلا"));
        await new OpenAiClient(f, 5000, "k").CompleteAsync(new LlmRequest([new ChatMessage("user", "x")], "{\"type\":\"object\"}"), null, default);
        var b = Body(f);
        Assert.Equal("http://127.0.0.1:5000/v1/chat/completions", f.Seen[0].Url);
        Assert.Equal("sahar", b.GetProperty("model").GetString());
        Assert.True(b.TryGetProperty("top_k", out _));
        Assert.True(b.TryGetProperty("chat_template_kwargs", out _));
        Assert.True(b.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task Groq_GptOss_ReasoningLow_NoLlamaFields_Bearer()
    {
        var f = new FakeProvider(FakeProvider.Ok("أهلين"));
        var r = await new OpenAiClient(f, ProviderProfiles.Groq(), "gsk_test").CompleteAsync(Hi, null, default);
        Assert.Equal("أهلين", r.Text);
        var b = Body(f);
        Assert.StartsWith("https://api.groq.com/openai/v1/chat/completions", f.Seen[0].Url);
        Assert.Equal("openai/gpt-oss-120b", b.GetProperty("model").GetString());
        Assert.False(b.GetProperty("include_reasoning").GetBoolean());
        Assert.Equal("low", b.GetProperty("reasoning_effort").GetString());
        Assert.False(b.TryGetProperty("top_k", out _));
        Assert.False(b.TryGetProperty("chat_template_kwargs", out _));
        Assert.False(b.TryGetProperty("response_format", out _));
        Assert.Equal(1500, b.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Bearer gsk_test", f.Seen[0].Req.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Groq_OtherModel_NoReasoningFields()
    {
        var f = new FakeProvider(FakeProvider.Ok("x"));
        await new OpenAiClient(f, ProviderProfiles.Groq("llama-3.3-70b-versatile"), "k").CompleteAsync(Hi, null, default);
        Assert.False(Body(f).TryGetProperty("include_reasoning", out _));
    }

    [Fact]
    public async Task OpenRouter_AttributionHeaders_AndStreamsWords()
    {
        var f = new FakeProvider(FakeProvider.Ok("يا ", "هلا ", "فيك"));
        var parts = new List<string>();
        var r = await new OpenAiClient(f, ProviderProfiles.OpenRouter(), "sk-or").CompleteAsync(Hi, parts.Add, default);
        Assert.Equal("يا هلا فيك", r.Text);
        Assert.Equal(["يا ", "هلا ", "فيك"], parts);
        Assert.Equal(11, r.PromptTokens);
        Assert.Equal(7, r.OutputTokens);
        Assert.Equal("Sahar", f.Seen[0].Req.Headers.GetValues("X-Title").Single());
        Assert.Equal(ProviderProfiles.OpenRouterDefault, Body(f).GetProperty("model").GetString());
    }

    [Theory]
    [InlineData(401, "key-bad")]
    [InlineData(402, "no-credit")]
    [InlineData(403, "blocked")]
    [InlineData(429, "quota")]
    [InlineData(500, "server")]
    [InlineData(502, "server")]
    [InlineData(503, "server")]
    [InlineData(404, "model-bad")]
    [InlineData(400, "bad-output")]
    public async Task Online_StatusMapsToOwnCode_BodyNeverInTheError(int status, string code)
    {
        var f = new FakeProvider(FakeProvider.Status(status));
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(f, ProviderProfiles.Groq(), "k").CompleteAsync(Hi, null, default));
        Assert.Equal(code, e.Code);
        Assert.DoesNotContain("SECRET-BODY", e.Message + e.Detail);
        Assert.Equal(status.ToString(System.Globalization.CultureInfo.InvariantCulture), e.Detail);
    }

    [Fact]
    public async Task Local_503_StillBusy()
    {
        var f = new FakeProvider(FakeProvider.Status(503));
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(f, 1, "k").CompleteAsync(Hi, null, default));
        Assert.Equal("busy", e.Code);
    }

    [Fact]
    public async Task Online_429_RateHeadersRead()
    {
        var f = new FakeProvider(FakeProvider.Status(429, new Dictionary<string, string> { ["retry-after"] = "2", ["x-ratelimit-remaining-requests"] = "0", ["x-ratelimit-limit-requests"] = "1000" }));
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(f, ProviderProfiles.Groq(), "k").CompleteAsync(Hi, null, default));
        var rate = Assert.IsType<RateInfo>(e.Data["rate"]);
        Assert.Equal(0, rate.Remaining);
        Assert.Equal(1000, rate.Limit);
        Assert.Equal(2, rate.RetryAfterS);
    }

    [Fact]
    public async Task Online_Success_RateHeadersOnTheReply()
    {
        var f = new FakeProvider(FakeProvider.Raw(FakeProvider.Sse("x"), new Dictionary<string, string> { ["x-ratelimit-remaining-requests"] = "987", ["x-ratelimit-limit-requests"] = "1000" }));
        var r = await new OpenAiClient(f, ProviderProfiles.Groq(), "k").CompleteAsync(Hi, null, default);
        Assert.Equal(987, r.Rate!.Remaining);
        Assert.Equal(1000, r.Rate.Limit);
    }

    [Fact]
    public async Task MidStream_ErrorEvent_IsStreamError()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"كان \"}}]}\n\ndata: {\"error\":{\"code\":429,\"message\":\"SECRET-BODY\"},\"choices\":[{\"delta\":{},\"finish_reason\":\"error\"}]}\n\n";
        var parts = new List<string>();
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(new FakeProvider(FakeProvider.Raw(sse)), ProviderProfiles.OpenRouter(), "k").CompleteAsync(Hi, parts.Add, default));
        Assert.Equal("stream-error", e.Code);
        Assert.Equal(["كان "], parts);
    }

    [Fact]
    public async Task FinishReasonError_IsStreamError()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"content\":\"a\"},\"finish_reason\":\"error\"}]}\n\n";
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(new FakeProvider(FakeProvider.Raw(sse)), ProviderProfiles.OpenRouter(), "k").CompleteAsync(Hi, null, default));
        Assert.Equal("stream-error", e.Code);
    }

    [Fact]
    public async Task Online_NoWords_IsBadOutput()
    {
        var sse = "data: {\"choices\":[{\"delta\":{\"reasoning\":\"hmm\"}}]}\n\ndata: [DONE]\n\n";
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(new FakeProvider(FakeProvider.Raw(sse)), ProviderProfiles.Groq(), "k").CompleteAsync(Hi, null, default));
        Assert.Equal("bad-output", e.Code);
    }

    [Theory]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.SecureConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public async Task OpenAiClient_ConnectFailure_IsOffline(HttpRequestError err)
    {
        var f = new FakeProvider(FakeProvider.Fail(err), FakeProvider.Fail(err));
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => new OpenAiClient(f, ProviderProfiles.Groq(), "k").CompleteAsync(Hi, null, default));
        Assert.Equal("offline", e.Code);
        Assert.Single(f.Seen);   // online: no silent second try inside the client (the turn decides)
    }

    [Fact]
    public async Task Online_FirstByteTooLate_IsTimeout()
    {
        var f = new FakeProvider(FakeProvider.Ok("x") with { Delay = TimeSpan.FromSeconds(5) });
        var c = new OpenAiClient(f, ProviderProfiles.Groq(), "k") { FirstByte = TimeSpan.FromMilliseconds(100) };
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => c.CompleteAsync(Hi, null, default));
        Assert.Equal("timeout", e.Code);
    }

    [Fact]
    public void Profiles_Urls()
    {
        Assert.True(ProviderProfiles.IsAllowedUrl("https://api.example.com/v1"));
        Assert.True(ProviderProfiles.IsAllowedUrl("http://127.0.0.1:8080/v1"));
        Assert.True(ProviderProfiles.IsAllowedUrl("http://localhost:8080/v1"));
        Assert.False(ProviderProfiles.IsAllowedUrl("http://api.example.com/v1"));
        Assert.False(ProviderProfiles.IsAllowedUrl("https://user:pw@api.example.com/v1"));
        Assert.False(ProviderProfiles.IsAllowedUrl("https://api.example.com/v1?key=1"));
        Assert.False(ProviderProfiles.IsAllowedUrl("file:///c:/x"));
        Assert.False(ProviderProfiles.IsAllowedUrl(""));
        Assert.Null(ProviderProfiles.Custom("https://x.ai/v1", "bad model"));
        Assert.Equal("https://x.ai/v1/", ProviderProfiles.Custom("https://x.ai/v1", "grok-4")!.BaseUrl.ToString());
        Assert.True(ProviderProfiles.Groq().UseProxy);
        Assert.False(ProviderProfiles.Local(1, "k").UseProxy);
        Assert.Equal("openai/gpt-oss-120b", ProviderProfiles.Groq("bad id!").Model);
    }

    [Fact]
    public void ErrorLines_Arabic()
    {
        Assert.Equal("يشتغل ✓", OnlineErrors.Line("ok"));
        Assert.Equal("المفتاح غلط", OnlineErrors.Line("key-bad"));
        Assert.Equal("خلصت طلبات اليوم", OnlineErrors.Line("quota"));
        Assert.Equal("محجوب أو ما في نت", OnlineErrors.Line("blocked"));
        Assert.Equal("محجوب أو ما في نت", OnlineErrors.Line("offline"));
        Assert.True(OnlineErrors.Retryable("quota"));
        Assert.True(OnlineErrors.Retryable("server"));
        Assert.False(OnlineErrors.Retryable("key-bad"));
    }
}
