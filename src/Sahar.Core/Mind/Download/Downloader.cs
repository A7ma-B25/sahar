using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text.Json;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Download;

/// <summary>What to fetch: <see cref="ApprovedAt"/> is his «إي» (owner answer: never without his yes).</summary>
public sealed record DownloadPlan(string ModelId, Uri Url, string File, long Bytes, string Sha256, DateTimeOffset? ApprovedAt);

/// <summary>The outcome. State: done | paused | mismatch | failed | no-disk | not-approved. <see cref="Error"/>:
/// network | host | http-NNN | io; <see cref="Missing"/> = bytes to free for no-disk.</summary>
public sealed record DownloadResult(string State, long Done, string? Error = null, long Missing = 0);

/// <summary>Reachability (owner answer: test first, say plainly when blocked). <see cref="Why"/>: dns | refused | tls |
/// timeout | network | host | http-NNN.</summary>
public sealed record Reach(bool Ok, string? Why = null);

/// <summary>The hosts a download may touch, on every redirect hop (the updater's pattern), https only.</summary>
public static class DownloadRules
{
    private static readonly string[] Exact = ["huggingface.co", "hf.co", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];
    private static readonly string[] Suffix = [".huggingface.co", ".hf.co"];

    public static bool Allowed(Uri u) => u.Scheme == "https" && u.IsDefaultPort &&
        (Exact.Contains(u.Host, StringComparer.OrdinalIgnoreCase) || Suffix.Any(s => u.Host.EndsWith(s, StringComparison.OrdinalIgnoreCase)));
}

/// <summary>Spec §6: the size plus 1 GB must be free.</summary>
public static class DiskCheck
{
    public const long Spare = 1L << 30;
    public static long Missing(long free, long size) => Math.Max(0, size + Spare - free);
}

/// <summary>
/// The resumable, verified download (spec §6). Bytes go to <c>&lt;file&gt;.part</c>; <c>&lt;file&gt;.part.json</c>
/// (<see cref="DownloadState"/>) keeps the offset and the ETag so it continues after a restart. A resumed request
/// sends Range + If-Range: a 200 answer (the file changed) starts over. The whole file's SHA-256 must equal the pin;
/// a mismatch deletes everything. Redirects are followed by hand so every hop is checked. Never logs anything.
/// Production passes a handler with automatic redirects off.
/// </summary>
public sealed class Downloader(HttpMessageHandler handler, Func<DateTimeOffset> now, Func<string, long> freeBytes)
{
    private readonly HttpClient _http = new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(8);
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public static DownloadState? ReadState(string file)
    {
        var path = file + ".part.json";
        if (!System.IO.File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize(System.IO.File.ReadAllText(path), MindJson.Default.DownloadState); }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
    }

    /// <summary>Deletes a download's part and state («ألغي»).</summary>
    public static void Discard(string file)
    {
        foreach (var p in new[] { file + ".part", file + ".part.json" })
            if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
    }

    public async Task<Reach> CheckAsync(Uri url, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CheckTimeout);
        try
        {
            using var r = await SendAsync(HttpMethod.Head, url, null, cts.Token);
            if (r is null) return new(false, "host");
            return r.IsSuccessStatusCode ? new(true) : new(false, $"http-{(int)r.StatusCode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "timeout"); }
        catch (HttpRequestException e) { return new(false, Why(e)); }
    }

    public async Task<DownloadResult> RunAsync(DownloadPlan plan, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        if (plan.ApprovedAt is null) return new("not-approved", 0);
        if (!DownloadRules.Allowed(plan.Url)) return new("failed", 0, "host");
        var part = plan.File + ".part";
        var state = ReadState(plan.File);
        long offset = state is { } s0 && s0.Sha256 == plan.Sha256 && s0.Url == plan.Url.ToString() && System.IO.File.Exists(part)
            ? Math.Min(s0.Offset, new FileInfo(part).Length) : 0;
        string? etag = offset > 0 ? state!.ETag : null;
        long missing = DiskCheck.Missing(freeBytes(Path.GetDirectoryName(Path.GetFullPath(plan.File))!), plan.Bytes - offset);
        if (missing > 0) return new("no-disk", offset, Missing: missing);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(plan.File))!);
        try
        {
            using var resp = await SendAsync(HttpMethod.Get, plan.Url, offset > 0 ? (offset, etag) : null, ct);
            if (resp is null) return new("failed", offset, "host");
            if (!resp.IsSuccessStatusCode) return new("failed", offset, $"http-{(int)resp.StatusCode}");
            if (resp.StatusCode != HttpStatusCode.PartialContent) offset = 0;   // a whole answer: start over
            etag = resp.Headers.ETag?.Tag ?? etag;
            await using (var fs = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true))
            {
                fs.SetLength(offset);
                fs.Position = offset;
                await using var body = await resp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[1 << 16];
                long lastAt = 0, lastDone = offset;
                var started = now();
                var tick = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    while (true)
                    {
                        int n = await ReadAsync(body, buf, ct);
                        if (n == 0) break;
                        await fs.WriteAsync(buf.AsMemory(0, n), ct);
                        offset += n;
                        if (tick.ElapsedMilliseconds - lastAt >= 250 || offset == plan.Bytes)
                        {
                            long bps = tick.ElapsedMilliseconds - lastAt > 0 ? (offset - lastDone) * 1000 / Math.Max(1, tick.ElapsedMilliseconds - lastAt) : 0;
                            lastAt = tick.ElapsedMilliseconds;
                            lastDone = offset;
                            await fs.FlushAsync(CancellationToken.None);
                            SaveState(plan, offset, etag, "downloading");
                            progress?.Report(new DownloadProgress(plan.ModelId, Path.GetFileName(plan.File), offset, plan.Bytes, bps, "downloading", Source(plan.Url)));
                        }
                    }
                }
                catch (Exception e) when (e is OperationCanceledException or IOException or HttpRequestException)
                {
                    await fs.FlushAsync(CancellationToken.None);
                    SaveState(plan, offset, etag, "paused");
                    progress?.Report(new DownloadProgress(plan.ModelId, Path.GetFileName(plan.File), offset, plan.Bytes, 0, "paused", Source(plan.Url)));
                    return new("paused", offset, e is OperationCanceledException ? null : "network");
                }
            }
            if (offset < plan.Bytes)
            {
                SaveState(plan, offset, etag, "paused");
                return new("paused", offset, "network");
            }
            SaveState(plan, offset, etag, "verifying");
            progress?.Report(new DownloadProgress(plan.ModelId, Path.GetFileName(plan.File), offset, plan.Bytes, 0, "verifying", Source(plan.Url)));
            if (!await HashMatches(part, plan.Sha256, ct))
            {
                Discard(plan.File);
                return new("mismatch", 0);
            }
            System.IO.File.Move(part, plan.File, overwrite: true);
            System.IO.File.Delete(plan.File + ".part.json");
            progress?.Report(new DownloadProgress(plan.ModelId, Path.GetFileName(plan.File), offset, plan.Bytes, 0, "done", Source(plan.Url)));
            return new("done", offset);
        }
        catch (OperationCanceledException)
        {
            SaveState(plan, offset, etag, "paused");
            return new("paused", offset);
        }
        catch (HttpRequestException e)
        {
            SaveState(plan, offset, etag, "paused");
            return new("paused", offset, Why(e));
        }
    }

    /// <summary>A read that gives up when the server goes silent for <see cref="ReadTimeout"/>.</summary>
    private async Task<int> ReadAsync(Stream body, byte[] buf, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReadTimeout);
        try { return await body.ReadAsync(buf, cts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("stalled"); }
    }

    private static async Task<bool> HashMatches(string path, string sha, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexStringLower(hash) == sha.ToLowerInvariant();
    }

    private void SaveState(DownloadPlan plan, long offset, string? etag, string state)
    {
        var tmp = plan.File + ".part.json.tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(new DownloadState(plan.Url.ToString(), plan.Sha256, plan.Bytes, offset, etag, state, now()), MindJson.Default.DownloadState));
        System.IO.File.Move(tmp, plan.File + ".part.json", overwrite: true);
    }

    /// <summary>Sends, following up to 5 redirects by hand; null when a hop leaves the allow-list.</summary>
    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, Uri url, (long From, string? ETag)? range, CancellationToken ct)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            if (!DownloadRules.Allowed(url)) return null;
            var req = new HttpRequestMessage(method, url);
            req.Headers.UserAgent.ParseAdd("Sahar/" + CoreInfo.Version);
            if (range is { } r)
            {
                req.Headers.Range = new RangeHeaderValue(r.From, null);
                if (r.ETag is { } tag) req.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(tag.StartsWith('"') || tag.StartsWith("W/") ? tag : $"\"{tag}\""));
            }
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
            {
                url = loc.IsAbsoluteUri ? loc : new Uri(url, loc);
                resp.Dispose();
                continue;
            }
            return resp;
        }
        return null;
    }

    private static string Source(Uri u) => u.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) ? "github" : "hf";

    private static string Why(HttpRequestException e) => e.InnerException switch
    {
        SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain } => "dns",
        SocketException { ErrorCode: 11001 or 11002 or 11004 } => "dns",
        SocketException { SocketErrorCode: SocketError.ConnectionRefused } => "refused",
        SocketException { SocketErrorCode: SocketError.TimedOut } => "timeout",
        AuthenticationException => "tls",
        _ when e.HttpRequestError == HttpRequestError.NameResolutionError => "dns",
        _ when e.HttpRequestError == HttpRequestError.SecureConnectionError => "tls",
        _ => "network",
    };
}
