using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Mind.Turn;

namespace Sahar.Mind.Tools;

/// <summary>
/// 0.19 spec §8: the two built-in media tools. <c>image.make</c>: Pollinations (no key; one picture at a time, at least
/// 45 s apart — a 402 or 429 says how long to wait, she tells him, no retry, no queue). <c>voice.make</c>: Groq's Saudi
/// Orpheus voice with his 0.17 key (≤ 200 characters). Both go through the system proxy (his VPN), 90 s at most, and save
/// into <c>Pictures\Sahar</c>. Logs codes and ms only — never a prompt, a word or a file name.
/// </summary>
internal sealed class MediaHost
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);
    /// <summary>45 s (Pollinations' published per-IP gap). CI only (with the loopback test base): <c>SAHAR_MIND_IMAGE_GAP_S</c>.</summary>
    public static readonly TimeSpan ImageGap = ProviderProfiles.TestBase is not null && int.TryParse(Environment.GetEnvironmentVariable("SAHAR_MIND_IMAGE_GAP_S"), out var g) && g is >= 0 and <= 45
        ? TimeSpan.FromSeconds(g) : TimeSpan.FromSeconds(45);
    public const string Voice = "noura";

    private readonly HttpClient _images = new(new SocketsHttpHandler { UseProxy = ProviderProfiles.TestBase is null, AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(15) })
    { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _groq = new(new SocketsHttpHandler { UseProxy = ProviderProfiles.TestBase is null, AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) })
    { Timeout = Timeout.InfiniteTimeSpan };
    private DateTimeOffset _lastImage = DateTimeOffset.MinValue;

    /// <summary>Pictures\Sahar (created on first use).</summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sahar");

    /// <summary>The check on «الأدوات»: a tiny picture is fetched and thrown away. Codes: ok | wait:&lt;s&gt; | offline | failed.</summary>
    public async Task<string> CheckImageAsync(CancellationToken ct)
    {
        var r = await ImageAsync("a small paper star, simple", save: false, ct);
        return r.Ok ? "ok" : r.Error == "wait" ? "wait:" + (r.Text ?? "45") : r.Error ?? "failed";
    }

    public async Task<MindToolResult> ImageAsync(string prompt, bool save, CancellationToken ct)
    {
        var since = DateTimeOffset.UtcNow - _lastImage;
        if (since < ImageGap) return Wait((int)Math.Ceiling((ImageGap - since).TotalSeconds));
        var sw = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        try
        {
            _lastImage = DateTimeOffset.UtcNow;
            using var res = await _images.GetAsync(MediaTools.ImageUri(prompt, Random.Shared.Next(1, int.MaxValue)), HttpCompletionOption.ResponseHeadersRead, limit.Token);
            MindLog.Write($"tool: image {(int)res.StatusCode} {sw.ElapsedMilliseconds}ms");
            if (res.StatusCode is HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests) return Wait(RetryAfter(res) ?? 45);
            if (!res.IsSuccessStatusCode) return Fail("failed");
            var bytes = await res.Content.ReadAsByteArrayAsync(limit.Token);
            string? ext = bytes is [0xFF, 0xD8, 0xFF, ..] ? "jpg" : bytes is [0x89, 0x50, 0x4E, 0x47, ..] ? "png" : null;
            if (ext is null || bytes.Length > 20 << 20) return Fail("failed");
            if (!save) return new MindToolResult(true, "image", ToolTemplates.MindDone(MediaTools.Image));
            var name = Save("image", bytes, ext);
            return name is null ? Fail("failed") : new MindToolResult(true, "image", ToolTemplates.MindDone(MediaTools.Image), File: name);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { MindLog.Write("tool: image timeout"); return new(false, "none", ToolTemplates.ToolTimeout, Error: "timeout"); }
        catch (HttpRequestException e) { MindLog.Write($"tool: image offline ({e.HttpRequestError})"); return new(false, "none", ToolTemplates.NoInternet, Error: "offline"); }
    }

    /// <summary>The voice check: one short line (one of the day's requests). Codes: ok | no-key | key-bad | quota | offline | failed.</summary>
    public async Task<string> CheckVoiceAsync(string? key, CancellationToken ct)
    {
        if (key is null) return "no-key";
        var r = await VoiceAsync("أهلين", key, save: false, ct);
        return r.Ok ? "ok" : r.Error ?? "failed";
    }

    public async Task<MindToolResult> VoiceAsync(string text, string? key, bool save, CancellationToken ct)
    {
        text = text.Trim();
        if (key is null) return new(false, "none", ToolTemplates.Unavailable, Error: "no-key");
        if (text.Length > MediaTools.VoiceMax) return new(false, "none", ToolTemplates.VoiceTooLong, Error: "too-long");
        var sw = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(Limit);
        try
        {
            using var body = new MemoryStream();
            using (var w = new Utf8JsonWriter(body))
            {
                w.WriteStartObject();
                w.WriteString("model", MediaTools.VoiceModel);
                w.WriteString("input", text);
                w.WriteString("voice", Voice);
                w.WriteString("response_format", "wav");
                w.WriteEndObject();
            }
            using var req = new HttpRequestMessage(HttpMethod.Post, MediaTools.VoiceUri()) { Content = new ByteArrayContent(body.ToArray()) };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            using var res = await _groq.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            MindLog.Write($"tool: voice {(int)res.StatusCode} {sw.ElapsedMilliseconds}ms");
            if (res.StatusCode == HttpStatusCode.TooManyRequests) return new(false, "none", ToolTemplates.VoiceOut, Error: "quota");
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return new(false, "none", ToolTemplates.ToolFailed, Error: "key-bad");
            if (!res.IsSuccessStatusCode) return Fail("failed");
            var bytes = await res.Content.ReadAsByteArrayAsync(limit.Token);
            if (bytes is not [0x52, 0x49, 0x46, 0x46, ..] || bytes.Length > 30 << 20) return Fail("failed");   // RIFF
            if (!save) return new MindToolResult(true, "voice", ToolTemplates.MindDone(MediaTools.Voice));
            var name = Save("voice", bytes, "wav");
            return name is null ? Fail("failed") : new MindToolResult(true, "voice", ToolTemplates.MindDone(MediaTools.Voice), File: name);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { MindLog.Write("tool: voice timeout"); return new(false, "none", ToolTemplates.ToolTimeout, Error: "timeout"); }
        catch (HttpRequestException e) { MindLog.Write($"tool: voice offline ({e.HttpRequestError})"); return new(false, "none", ToolTemplates.NoInternet, Error: "offline"); }
    }

    private static MindToolResult Wait(int seconds) =>
        new(false, "none", ToolTemplates.ImageWait(Math.Clamp(seconds, 1, 3600)), Text: Math.Clamp(seconds, 1, 3600).ToString(System.Globalization.CultureInfo.InvariantCulture), Error: "wait");

    private static MindToolResult Fail(string code) => new(false, "none", ToolTemplates.ToolFailed, Error: code);

    private static int? RetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter is { } ra)
        {
            if (ra.Delta is { } d) return (int)Math.Ceiling(d.TotalSeconds);
            if (ra.Date is { } at) return (int)Math.Ceiling((at - DateTimeOffset.UtcNow).TotalSeconds);
        }
        return null;
    }

    /// <summary>Saves into Pictures\Sahar as sahar-&lt;kind&gt;-yyyyMMdd-HHmmss(.n).ext; the bare name, or null.</summary>
    private static string? Save(string kind, byte[] bytes, string ext)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var stem = Path.GetFileNameWithoutExtension(MediaTools.FileName(kind, DateTimeOffset.Now));
            for (int i = 1; i < 60; i++)
            {
                var name = stem + (i == 1 ? "" : "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)) + "." + ext;
                var path = Path.Combine(Folder, name);
                try
                {
                    using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                    f.Write(bytes);
                    return name;
                }
                catch (IOException) when (File.Exists(path)) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MindLog.Write($"tool: {kind} not saved ({e.GetType().Name})"); }
        return null;
    }
}
