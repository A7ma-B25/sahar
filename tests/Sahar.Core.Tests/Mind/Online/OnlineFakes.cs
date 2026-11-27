using System.Net;
using System.Text;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>A recorded provider: each request gets the next scripted answer (status, headers, SSE body, or a thrown
/// transport error). Requests are kept (URL, headers, body) for the assertions. No network.</summary>
internal sealed class FakeProvider : HttpMessageHandler
{
    public sealed record Answer(HttpStatusCode Status, string Body, IReadOnlyDictionary<string, string>? Headers = null, HttpRequestError? Throw = null, TimeSpan Delay = default);

    private readonly Queue<Answer> _answers = new();
    public readonly List<(string Url, string Body, HttpRequestMessage Req)> Seen = [];
    public Answer? Always;

    public FakeProvider(params Answer[] answers) { foreach (var a in answers) _answers.Enqueue(a); }

    public static Answer Ok(params string[] words) => new(HttpStatusCode.OK, Sse(words));
    public static Answer Status(int code, IReadOnlyDictionary<string, string>? headers = null) => new((HttpStatusCode)code, "{\"error\":{\"message\":\"SECRET-BODY\"}}", headers);
    public static Answer Raw(string sse, IReadOnlyDictionary<string, string>? headers = null) => new(HttpStatusCode.OK, sse, headers);
    public static Answer Fail(HttpRequestError e) => new(HttpStatusCode.OK, "", null, e);

    /// <summary>An SSE body: one delta per word, a comment line, usage, [DONE].</summary>
    public static string Sse(params string[] words)
    {
        var sb = new StringBuilder(": OPENROUTER PROCESSING\n\n");
        foreach (var w in words) sb.Append("data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":").Append(System.Text.Json.JsonSerializer.Serialize(w)).Append("}}]}\n\n");
        sb.Append("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7}}\n\n");
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        Seen.Add((req.RequestUri!.ToString(), req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct), req));
        var a = _answers.Count > 0 ? _answers.Dequeue() : Always ?? throw new InvalidOperationException("no more scripted answers");
        if (a.Delay > TimeSpan.Zero) await Task.Delay(a.Delay, ct);
        if (a.Throw is { } e) throw new HttpRequestException(e, "transport");
        var r = new HttpResponseMessage(a.Status) { Content = new StringContent(a.Body, Encoding.UTF8, "text/event-stream") };
        foreach (var (k, v) in a.Headers ?? new Dictionary<string, string>()) r.Headers.TryAddWithoutValidation(k, v);
        return r;
    }
}
