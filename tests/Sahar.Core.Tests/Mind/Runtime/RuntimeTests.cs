using System.Net;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Tests.Mind.Runtime;

/// <summary>Spec §3.1, §3.5: llama-server's command line, the OpenAI-compatible client, idle unload.</summary>
public sealed class RuntimeTests
{
    private static readonly HardwareProbe HisPc = new(32_510, [new Gpu(Gpu.Nvidia, 6_012, false)], true, 200_000);

    [Fact]
    public void Args_HisPc_AllLayersVulkan()
    {
        var a = ServerArgs.For(Tiers.Plan(HisPc), @"C:\m\gemma.gguf", 51234, "k3y", threads: 6);
        string Val(string flag) => a[a.ToList().IndexOf(flag) + 1];
        Assert.Equal(@"C:\m\gemma.gguf", Val("-m"));
        Assert.Equal("127.0.0.1", Val("--host"));
        Assert.Equal("51234", Val("--port"));
        Assert.Equal("k3y", Val("--api-key"));
        Assert.Equal("8192", Val("-c"));
        Assert.Equal("999", Val("-ngl"));
        Assert.Equal("q8_0", Val("--cache-type-k"));
        Assert.Equal("q8_0", Val("--cache-type-v"));
        Assert.Equal("6", Val("-t"));
        Assert.Contains("--no-webui", a);
        Assert.Contains("--jinja", a);
        Assert.DoesNotContain("--sleep-idle-seconds", a);   // we kill the process instead (spec §3.1)
    }

    [Fact]
    public void Args_Fit_And_Cpu()
    {
        var fit = ServerArgs.For(Tiers.Plan(new HardwareProbe(16_100, [new Gpu(Gpu.Amd, 4_080, false)], true, 200_000)), "m", 1, "k", 4);
        Assert.Contains("--fit", fit);
        var cpu = ServerArgs.For(Tiers.Plan(HisPc, useGpu: false), "m", 1, "k", 4);
        Assert.Equal("0", cpu[cpu.ToList().IndexOf("-ngl") + 1]);
    }

    [Fact]
    public void Args_NeverBindsOutsideLoopback() =>
        Assert.Equal(1, ServerArgs.For(Tiers.Plan(HisPc), "m", 1, "k", 2).Count(x => x == "--host"));

    [Theory]
    [InlineData(16, 8, 6)]
    [InlineData(4, 2, 2)]
    [InlineData(2, 1, 2)]
    public void Threads_PhysicalMinusTwo_AtLeastTwo(int logical, int physical, int want) => Assert.Equal(want, ServerArgs.Threads(logical, physical));

    [Theory]
    [InlineData("vulkan", "runtime/vulkan")]
    [InlineData("cpu", "runtime/cpu")]
    public void RuntimeDir_PerBackend(string variant, string tail) =>
        Assert.EndsWith(tail.Replace('/', Path.DirectorySeparatorChar), ServerArgs.RuntimeDir("/app", "/local", variant == "vulkan" ? Backend.Vulkan : Backend.Cpu));

    [Fact]
    public void RuntimeDir_CudaIsDownloadedNotBundled() =>
        Assert.StartsWith(Path.Combine("/local", "cuda"), ServerArgs.RuntimeDir("/app", "/local", Backend.Cuda));

    [Fact]
    public async Task Client_StreamsChunks_AndSendsSchemaAndKey()
    {
        var fake = new FakeServer(["{\"say\":", "\"هلق\"", "}"]);
        var llm = new OpenAiClient(fake, 5000, "secret");
        var parts = new List<string>();
        var r = await llm.CompleteAsync(new LlmRequest([new ChatMessage("user", "شو")], "{\"type\":\"object\"}"), parts.Add, default);
        Assert.Equal("{\"say\":\"هلق\"}", r.Text);
        Assert.Equal(["{\"say\":", "\"هلق\"", "}"], parts);
        Assert.Equal(12, r.PromptTokens);
        Assert.Equal(3, r.OutputTokens);
        Assert.Equal("Bearer secret", fake.Auth);
        Assert.Equal("http://127.0.0.1:5000/v1/chat/completions", fake.Url);
        using var body = JsonDocument.Parse(fake.Body!);
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("object", body.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").GetProperty("type").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(160, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Client_ServerGone_LoadFailed()
    {
        var llm = new OpenAiClient(new FakeServer([]) { Gone = true }, 5000, "k");
        var e = await Assert.ThrowsAsync<MindRuntimeException>(() => llm.CompleteAsync(new LlmRequest([new ChatMessage("user", "x")]), null, default));
        Assert.Equal("load-failed", e.Code);
    }

    [Fact]
    public async Task Client_Loading503_Busy()
    {
        var llm = new OpenAiClient(new FakeServer([]) { Status = HttpStatusCode.ServiceUnavailable }, 5000, "k");
        Assert.Equal("busy", (await Assert.ThrowsAsync<MindRuntimeException>(() => llm.CompleteAsync(new LlmRequest([new ChatMessage("user", "x")]), null, default))).Code);
    }

    [Fact]
    public async Task Client_Health()
    {
        Assert.True(await new OpenAiClient(new FakeServer([]), 1, "k").HealthyAsync(default));
        Assert.False(await new OpenAiClient(new FakeServer([]) { Status = HttpStatusCode.ServiceUnavailable }, 1, "k").HealthyAsync(default));
        Assert.False(await new OpenAiClient(new FakeServer([]) { Gone = true }, 1, "k").HealthyAsync(default));
    }

    [Theory]
    [InlineData(4, true, false, 5, IdleStep.Keep)]
    [InlineData(5, true, false, 5, IdleStep.Unload)]
    [InlineData(2, true, true, 5, IdleStep.Unload)]     // on battery: after 1 min
    [InlineData(9, false, false, 5, IdleStep.Keep)]
    [InlineData(10, false, false, 5, IdleStep.Exit)]   // nothing loaded for 10 min: the Mind exits
    public void Idle_UnloadAfterMinutes(int idleMinutes, bool loaded, bool battery, int unload, IdleStep want)
    {
        var t0 = new DateTimeOffset(2026, 11, 13, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(want, IdlePolicy.Decide(t0.AddMinutes(idleMinutes), t0, loaded, battery, unload));
    }

    private sealed class FakeServer(string[] chunks) : HttpMessageHandler
    {
        public bool Gone;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string? Auth, Url, Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (Gone) throw new HttpRequestException("refused", new System.Net.Sockets.SocketException(10061));
            Auth = req.Headers.Authorization?.ToString();
            Url = req.RequestUri!.ToString();
            Body = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);
            if (req.RequestUri.AbsolutePath == "/health") return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"ok\"}") };
            var sb = new StringBuilder();
            foreach (var c in chunks) sb.Append("data: ").Append(JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = c } } } })).Append("\n\n");
            sb.Append("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":3}}\n\n");
            sb.Append("data: [DONE]\n\n");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream") };
        }
    }
}
