using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Online;

namespace Sahar.Core.Mind.Runtime;

/// <summary>
/// <see cref="ILlm"/> over an OpenAI-compatible chat-completions endpoint (0.17: one client for every provider, its
/// <see cref="ProviderProfile"/> says where and how). Local: llama-server on 127.0.0.1 with the launch's key, streamed
/// /v1/chat/completions (SSE), grammar-constrained with <c>response_format: json_schema</c> when a schema is given.
/// Local errors: load-failed (no server), busy (503 while loading), timeout, bad-output, thinking (the model only
/// thought: no words); a connection lost before any byte is tried once more (0.14.1).
/// Online (0.17 spec §7): llama-only fields never sent; the status becomes our own code (key-bad, no-credit, blocked,
/// quota, server, bad-output), transport failures offline, a late first byte timeout, an SSE error event mid-stream
/// stream-error; rate headers ride along in <see cref="LlmReply.Rate"/> or the exception's <c>Data["rate"]</c>; the
/// provider's body is never read. Written with Utf8JsonWriter / JsonDocument only (AOT). Never logs text.
/// </summary>
public sealed class OpenAiClient : ILlm, IDisposable
{
    private readonly HttpClient _http;
    private readonly ProviderProfile _p;
    private readonly string _key;

    /// <summary>Online: no first byte within this → timeout (spec §7: 12 s).</summary>
    public TimeSpan FirstByte { get; init; } = TimeSpan.FromSeconds(12);
    /// <summary>Online: no byte for this long while streaming → timeout (spec §7: 20 s).</summary>
    public TimeSpan Stall { get; init; } = TimeSpan.FromSeconds(20);

    public OpenAiClient(HttpMessageHandler handler, int port, string apiKey) : this(handler, ProviderProfiles.Local(port, apiKey), apiKey) { }

    public OpenAiClient(HttpMessageHandler handler, ProviderProfile profile, string apiKey)
    {
        _p = profile;
        _key = apiKey;
        _http = new HttpClient(handler, disposeHandler: false) { BaseAddress = profile.BaseUrl, Timeout = Timeout.InfiniteTimeSpan };
    }

    public ProviderProfile Profile => _p;
    /// <summary>The Mind's cache key for this client (never the key itself).</summary>
    public string? Tag { get; init; }
    public void Dispose() => _http.Dispose();

    public async Task<bool> HealthyAsync(CancellationToken ct)
    {
        try
        {
            using var r = await _http.GetAsync("../health", ct);
            return r.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException) { return false; }
    }

    public async Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var body = Body(request);
        bool online = _p.Online;
        HttpResponseMessage resp;
        for (int attempt = 1; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions") { Content = new ByteArrayContent(body) };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            foreach (var (k, v) in _p.Headers) req.Headers.TryAddWithoutValidation(k, v);
            if (!online)
            {
                try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); break; }
                catch (HttpRequestException) when (attempt == 1) { await Task.Delay(150, ct); }
                catch (HttpRequestException e) { throw new MindRuntimeException("load-failed", e.HttpRequestError.ToString()); }
                continue;
            }
            using var first = CancellationTokenSource.CreateLinkedTokenSource(ct);
            first.CancelAfter(FirstByte);
            try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, first.Token); break; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new MindRuntimeException(OnlineErrors.Timeout, "first-byte"); }
            catch (HttpRequestException e) { throw new MindRuntimeException(OnlineErrors.Offline, e.HttpRequestError.ToString()); }
        }
        using (resp)
        {
            var rate = online ? Rate(resp) : null;
            if (!resp.IsSuccessStatusCode)
            {
                if (!online) throw new MindRuntimeException(resp.StatusCode == HttpStatusCode.ServiceUnavailable ? "busy" : "bad-output");
                var e = new MindRuntimeException(OnlineErrors.FromStatus((int)resp.StatusCode) ?? OnlineErrors.BadOutput,
                    ((int)resp.StatusCode).ToString(CultureInfo.InvariantCulture));
                if (rate is not null) e.Data["rate"] = rate;
                throw e;
            }
            var text = new StringBuilder();
            bool thought = false;
            int prompt = 0, output = 0;
            TimeSpan firstTok = TimeSpan.Zero;
            using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct), Encoding.UTF8);
            try
            {
                while (true)
                {
                    if (online) stall.CancelAfter(Stall);
                    string? line;
                    try { line = await reader.ReadLineAsync(stall.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new MindRuntimeException(OnlineErrors.Timeout, "stall"); }
                    if (line is null) break;
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;   // ": OPENROUTER PROCESSING" and blank lines
                    var data = line[5..].Trim();
                    if (data == "[DONE]") break;
                    using var doc = JsonDocument.Parse(data);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var err) && err.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                        throw new MindRuntimeException(online ? OnlineErrors.StreamError : "bad-output", "sse-error");
                    if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                    {
                        var c0 = choices[0];
                        if (c0.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
                        {
                            if ((delta.TryGetProperty("reasoning_content", out var rc) || delta.TryGetProperty("reasoning", out rc))
                                && rc.ValueKind == JsonValueKind.String && rc.GetString()!.Length > 0) thought = true;
                            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                            {
                                var chunk = c.GetString()!;
                                if (chunk.Length > 0)
                                {
                                    if (firstTok == TimeSpan.Zero) firstTok = sw.Elapsed;
                                    text.Append(chunk);
                                    partial?.Invoke(chunk);
                                }
                            }
                        }
                        if (c0.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String && fr.GetString() == "error")
                            throw new MindRuntimeException(online ? OnlineErrors.StreamError : "bad-output", "finish-error");
                    }
                    if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object) Usage(u, ref prompt, ref output);
                    // Groq puts the usage of a stream under x_groq.
                    if (root.TryGetProperty("x_groq", out var xg) && xg.ValueKind == JsonValueKind.Object && xg.TryGetProperty("usage", out var gu) && gu.ValueKind == JsonValueKind.Object)
                        Usage(gu, ref prompt, ref output);
                }
            }
            catch (JsonException) { throw new MindRuntimeException(online ? OnlineErrors.StreamError : "bad-output", "json"); }
            catch (IOException e) { throw new MindRuntimeException(online ? OnlineErrors.StreamError : "load-failed", e.GetType().Name); }
            catch (HttpRequestException e) when (online) { throw new MindRuntimeException(OnlineErrors.StreamError, e.HttpRequestError.ToString()); }
            if (text.Length == 0)
            {
                if (!online && thought) throw new MindRuntimeException("thinking");
                if (online) throw new MindRuntimeException(OnlineErrors.BadOutput, thought ? "only-reasoning" : "empty");
            }
            return new LlmReply(text.ToString(), prompt, output, firstTok, sw.Elapsed) { Rate = rate };
        }
    }

    private static void Usage(JsonElement u, ref int prompt, ref int output)
    {
        if (u.TryGetProperty("prompt_tokens", out var p) && p.ValueKind == JsonValueKind.Number) prompt = p.GetInt32();
        if (u.TryGetProperty("completion_tokens", out var o) && o.ValueKind == JsonValueKind.Number) output = o.GetInt32();
    }

    /// <summary>Groq-style request headers (the day's requests) and a 429's retry-after; null when none.</summary>
    internal static RateInfo? Rate(HttpResponseMessage r)
    {
        static int? Int(HttpResponseMessage r, string name) =>
            r.Headers.TryGetValues(name, out var v) && int.TryParse(v.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null;
        double? after = r.Headers.TryGetValues("retry-after", out var ra) && double.TryParse(ra.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) ? s : null;
        int? rem = Int(r, "x-ratelimit-remaining-requests"), lim = Int(r, "x-ratelimit-limit-requests");
        return rem is null && lim is null && after is null ? null : new RateInfo(rem, lim, after);
    }

    private byte[] Body(LlmRequest r)
    {
        var s = r.Sampling ?? Sampling.Chat;
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("model", _p.Model);
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
            if (_p.LlamaFields) w.WriteNumber("top_k", s.TopK);
            w.WriteNumber("max_tokens", s.MaxTokens);
            w.WriteBoolean("stream", true);
            if (_p.LlamaFields)
            {
                w.WriteStartObject("chat_template_kwargs");
                w.WriteBoolean("enable_thinking", false);
                w.WriteEndObject();
            }
            w.WriteStartObject("stream_options");
            w.WriteBoolean("include_usage", true);
            w.WriteEndObject();
            if (r.JsonSchema is { } schema && _p.LlamaFields)
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
            if (_p.ExtraBody is { Length: > 0 } extra)
            {
                // Fixed members from the profile (never his text): parsed so the body stays valid JSON.
                using var doc = JsonDocument.Parse("{" + extra + "}");
                foreach (var prop in doc.RootElement.EnumerateObject()) prop.WriteTo(w);
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}
