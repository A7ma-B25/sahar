using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Sahar.Core.Mind.Runtime;

/// <summary>
/// <see cref="ILlm"/> over llama-server's OpenAI-compatible API on 127.0.0.1 with the launch's key: streamed
/// /v1/chat/completions (SSE), grammar-constrained with <c>response_format: json_schema</c> when a schema is given.
/// Errors become protocol codes: load-failed (no server), busy (503 while loading), timeout, bad-output.
/// Written with Utf8JsonWriter / JsonDocument only (AOT). Never logs text.
/// </summary>
public sealed class OpenAiClient(HttpMessageHandler handler, int port, string apiKey) : ILlm
{
    private readonly HttpClient _http = new(handler, disposeHandler: false) { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = Timeout.InfiniteTimeSpan };

    public async Task<bool> HealthyAsync(CancellationToken ct)
    {
        try
        {
            using var r = await _http.GetAsync("health", ct);
            return r.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException) { return false; }
    }

    public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = new ByteArrayContent(Body(request)) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException) { throw new MindRuntimeException("load-failed"); }
        using (resp)
        {
            if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) throw new MindRuntimeException("busy");
            if (!resp.IsSuccessStatusCode) throw new MindRuntimeException("bad-output");
            var text = new StringBuilder();
            int prompt = 0, output = 0;
            TimeSpan first = TimeSpan.Zero;
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct), Encoding.UTF8);
            try
            {
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line[5..].Trim();
                    if (data == "[DONE]") break;
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    {
                        var chunk = c.GetString()!;
                        if (chunk.Length > 0)
                        {
                            if (first == TimeSpan.Zero) first = sw.Elapsed;
                            text.Append(chunk);
                            partial?.Invoke(chunk);
                        }
                    }
                    if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                    {
                        if (u.TryGetProperty("prompt_tokens", out var p)) prompt = p.GetInt32();
                        if (u.TryGetProperty("completion_tokens", out var o)) output = o.GetInt32();
                    }
                }
            }
            catch (JsonException) { throw new MindRuntimeException("bad-output"); }
            catch (IOException) { throw new MindRuntimeException("load-failed"); }
            return new LlmReply(text.ToString(), prompt, output, first, sw.Elapsed);
        }
    }

    private static byte[] Body(LlmRequest r)
    {
        var s = r.Sampling ?? Sampling.Chat;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("model", "sahar");
            w.WriteStartArray("messages");
            foreach (var m in r.Messages)
            {
                w.WriteStartObject();
                w.WriteString("role", m.Role);
                w.WriteString("content", m.Content);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("temperature", s.Temperature);
            w.WriteNumber("top_p", s.TopP);
            w.WriteNumber("top_k", s.TopK);
            w.WriteNumber("max_tokens", s.MaxTokens);
            w.WriteBoolean("stream", true);
            w.WriteStartObject("stream_options");
            w.WriteBoolean("include_usage", true);
            w.WriteEndObject();
            if (r.JsonSchema is { } schema)
            {
                w.WriteStartObject("response_format");
                w.WriteString("type", "json_schema");
                w.WriteStartObject("json_schema");
                w.WriteString("name", "step");
                w.WriteBoolean("strict", true);
                w.WritePropertyName("schema");
                w.WriteRawValue(schema);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}
