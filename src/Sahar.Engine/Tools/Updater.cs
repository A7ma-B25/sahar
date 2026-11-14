using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Sahar.Core.Update;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Tools;

/// <summary>A verified setup on disk, held open (others may read, nobody may write) until it is started.</summary>
internal sealed class PreparedSetup(string path, FileStream held) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose() => held.Dispose();
}

/// <summary>
/// The update check's network and files, and nothing else (every decision is <see cref="UpdateRules"/>,
/// <see cref="UpdateManifest"/> and <see cref="UpdateSchedule"/> in Core): the channel's signed manifest from the
/// release-download URL (no API, no rate limit), then the setup beside it. The first request must be one of this
/// repo's release downloads; redirects are followed by hand and every hop is checked. Bodies are capped whatever the
/// server says. Sends only a "Sahar/&lt;version&gt;" User-Agent. A failure is one log line (its kind, never a URL or a
/// path: paths hold the user name).
/// </summary>
internal sealed class Updater : IDisposable
{
    private const int MaxHops = 5;
    private const int MaxManifestBytes = 4096, MaxSignatureBytes = 1024;
    private const long MaxSetupBytes = 200L << 20;
    private static readonly TimeSpan SmallTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly string? _host;
    private readonly string _key;
    private readonly bool _loopback;

    /// <param name="host">Test mode only: a fake GitHub (loopback http is then allowed).</param>
    /// <param name="publicKey">The manifest key: <see cref="UpdateKeys.Production"/> unless test mode gives another.</param>
    public Updater(string? host, string publicKey)
    {
        (_host, _key, _loopback) = (host, publicKey, host is not null);
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None })
        {
            Timeout = Timeout.InfiniteTimeSpan,   // each call has its own deadline
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UpdateRules.UserAgent);
    }

    /// <summary>%TEMP%\Sahar-update: each download gets its own random folder inside (the uninstaller removes it).</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "Sahar-update");

    /// <summary>The offer in the channel's signed manifest (null: nothing newer); Ok is false when the check failed
    /// or the manifest is not signed by the key.</summary>
    public async Task<(UpdateOffer? Offer, bool Ok)> CheckAsync(UpdateChannel channel)
    {
        try
        {
            string root = UpdateRules.DownloadBase(channel, _host);
            using var cts = new CancellationTokenSource(SmallTimeout);
            var manifest = await GetBytes(root + UpdateManifest.FileName, MaxManifestBytes, cts.Token);
            var signature = await GetBytes(root + UpdateManifest.SignatureName, MaxSignatureBytes, cts.Token);
            if (UpdateManifest.Verify(manifest, Encoding.UTF8.GetString(signature), _key, channel) is not { } m)
            {
                Log.Write("update: check failed (manifest refused: signature or fields)");
                return (null, false);
            }
            if (!SemVersion.TryParse(Sahar.Core.CoreInfo.Version, out var current)) return (null, false);
            return (UpdateRules.Offer(m, current, root), true);
        }
        catch (Exception e)
        {
            Log.Write($"update: check failed ({Kind(e)})");
            return (null, false);
        }
    }

    /// <summary>Downloads the setup into a new random folder, hashing as it comes; then re-reads it under a no-write
    /// lock and checks the signed SHA-256 again. The held, verified setup, or null (nothing partial is left).</summary>
    public async Task<PreparedSetup?> DownloadAsync(UpdateOffer offer)
    {
        ClearOld();
        string dir = Path.Combine(Folder, Path.GetRandomFileName());
        string path = Path.Combine(dir, offer.SetupName);
        FileStream? held = null;
        try
        {
            Directory.CreateDirectory(dir);
            byte[] streamed;
            using (var cts = new CancellationTokenSource(SetupTimeout))
            using (var response = await Get(offer.SetupUrl, cts.Token))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var body = await response.Content.ReadAsStreamAsync(cts.Token))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                if (response.Content.Headers.ContentLength > MaxSetupBytes) throw new UpdateRefusedException("setup too big");
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await body.ReadAsync(buffer, cts.Token)) > 0)
                {
                    total += n;
                    if (total > MaxSetupBytes) throw new UpdateRefusedException("setup too big");
                    hash.AppendData(buffer, 0, n);
                    await file.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                }
                streamed = hash.GetHashAndReset();
            }
            if (!UpdateRules.Matches(streamed, offer.Sha256)) throw new UpdateRefusedException("checksum mismatch");
            // What runs is what is checked: read it again under a lock that lets nobody write until it has started.
            held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!UpdateRules.Matches(held, offer.Sha256)) throw new UpdateRefusedException("checksum mismatch");
            Log.Write($"update: {offer.Version} downloaded and verified");
            return new PreparedSetup(path, held);
        }
        catch (Exception e)
        {
            held?.Dispose();
            Log.Write($"update: download failed ({Kind(e)}; nothing kept)");
            try { Directory.Delete(dir, recursive: true); } catch (Exception) { }
            return null;
        }
    }

    /// <summary>Starts the held setup (silent for an installed Sahar, the wizard for a portable one); the lock goes
    /// once the process exists. The setup asks Sahar to quit by itself when it is ready to install.</summary>
    public static Process? Launch(PreparedSetup setup, bool silent)
    {
        try
        {
            string dir = Path.GetDirectoryName(setup.Path)!;
            return Process.Start(new ProcessStartInfo(setup.Path, UpdateRules.SetupArguments(Path.Combine(dir, "setup.log"), silent))
            {
                UseShellExecute = false,
                WorkingDirectory = dir,
            });
        }
        catch (Exception e)
        {
            Log.Write($"update: setup not started ({Kind(e)})");
            return null;
        }
        finally { setup.Dispose(); }
    }

    /// <summary>Earlier downloads (their setups have run or failed): best effort.</summary>
    private static void ClearOld()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var d in Directory.EnumerateDirectories(Folder)) try { Directory.Delete(d, recursive: true); } catch (Exception) { }
            foreach (var f in Directory.EnumerateFiles(Folder)) try { File.Delete(f); } catch (Exception) { }
        }
        catch (Exception) { }
    }

    private async Task<byte[]> GetBytes(string url, int cap, CancellationToken ct)
    {
        using var response = await Get(url, ct);
        if (response.Content.Headers.ContentLength > cap) throw new UpdateRefusedException("answer too big");
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[cap + 1];
        int total = 0, n;
        while ((n = await body.ReadAsync(buffer.AsMemory(total), ct)) > 0)
        {
            total += n;
            if (total > cap) throw new UpdateRefusedException("answer too big");   // no Content-Length: the cap still holds
        }
        return buffer[..total];
    }

    private async Task<HttpResponseMessage> Get(string url, CancellationToken ct)
    {
        for (int hop = 0; hop <= MaxHops; hop++)
        {
            bool allowed = hop == 0 ? UpdateRules.AllowedFirstHop(url, _loopback) : UpdateRules.AllowedUrl(url, _loopback);
            if (!allowed) throw new UpdateRefusedException(hop == 0 ? "address not allowed" : "redirect not allowed");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            int code = (int)response.StatusCode;
            if (code is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } next)
            {
                url = new Uri(new Uri(url), next).ToString();
                response.Dispose();
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new UpdateRefusedException($"HTTP {code}");
            }
            return response;
        }
        throw new UpdateRefusedException("too many redirects");
    }

    /// <summary>What went wrong, in words safe for the log: the exception's kind (and the refusal's own reason).</summary>
    private static string Kind(Exception e) => e switch
    {
        UpdateRefusedException r => r.Message,
        OperationCanceledException => "timeout",
        _ => e.GetType().Name,
    };

    public void Dispose() => _http.Dispose();
}

internal sealed class UpdateRefusedException(string reason) : Exception(reason);
