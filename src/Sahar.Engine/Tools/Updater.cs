using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Sahar.Core.Update;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Tools;

/// <summary>
/// The update check's network and files, and nothing else (every decision is <see cref="UpdateRules"/> /
/// <see cref="UpdateSchedule"/> in Core): the release from api.github.com, then the setup and its ".sha256" from the
/// release's asset URLs. Redirects are followed by hand so every hop is checked against the allow-list. Sends only a
/// "Sahar/&lt;version&gt;" User-Agent. A failure is one log line (its kind, never a URL or a path: paths hold the user name).
/// </summary>
internal sealed class Updater : IDisposable
{
    private const int MaxHops = 5;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromMinutes(10);
    private const long MaxSetupBytes = 200L << 20;

    private readonly HttpClient _http;
    private readonly string? _apiBase;
    private readonly bool _loopback;

    /// <param name="apiBase">Test mode only: a fake GitHub API (loopback http is then allowed).</param>
    public Updater(string? apiBase)
    {
        _apiBase = apiBase;
        _loopback = apiBase is not null;
        _http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,   // each call has its own deadline
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UpdateRules.UserAgent);
    }

    public static string Folder => Path.Combine(Path.GetTempPath(), "Sahar-update");

    /// <summary>The offer in the channel's release (null: nothing newer); Ok is false when the check failed.</summary>
    public async Task<(UpdateOffer? Offer, bool Ok)> CheckAsync(UpdateChannel channel)
    {
        try
        {
            using var cts = new CancellationTokenSource(ApiTimeout);
            using var response = await Get(UpdateRules.ReleaseApi(channel, _apiBase).ToString(), api: true, cts.Token);
            var json = await response.Content.ReadAsStringAsync(cts.Token);
            if (Release.Parse(json) is not { } release) { Log.Write("update: check failed (not a release)"); return (null, false); }
            if (!SemVersion.TryParse(Sahar.Core.CoreInfo.Version, out var current)) return (null, false);
            return (UpdateRules.Pick(release, current, _loopback), true);
        }
        catch (Exception e)
        {
            Log.Write($"update: check failed ({Kind(e)})");
            return (null, false);
        }
    }

    /// <summary>Downloads the setup into %TEMP%\Sahar-update and checks it against the published SHA-256; the path of
    /// a verified setup, or null.</summary>
    public async Task<string?> DownloadAsync(UpdateOffer offer)
    {
        try
        {
            string dir = Folder;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            string hex;
            using (var cts = new CancellationTokenSource(ApiTimeout))
            using (var sum = await Get(offer.Checksum.Url, api: false, cts.Token))
            {
                if (sum.Content.Headers.ContentLength > 4096) { Log.Write("update: checksum file too big"); return null; }
                var text = await sum.Content.ReadAsStringAsync(cts.Token);
                if (UpdateRules.ParseChecksum(text, offer.Setup.Name) is not { } h) { Log.Write("update: checksum file not understood"); return null; }
                hex = h;
            }
            string path = Path.Combine(dir, offer.Setup.Name);
            using (var cts = new CancellationTokenSource(SetupTimeout))
            using (var setup = await Get(offer.Setup.Url, api: false, cts.Token))
            {
                if (setup.Content.Headers.ContentLength > MaxSetupBytes) { Log.Write("update: setup too big"); return null; }
                await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await using var body = await setup.Content.ReadAsStreamAsync(cts.Token);
                var buffer = new byte[81920];
                long total = 0;
                int n;
                while ((n = await body.ReadAsync(buffer, cts.Token)) > 0)
                {
                    total += n;
                    if (total > MaxSetupBytes) { Log.Write("update: setup too big"); return null; }
                    await file.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                }
            }
            bool ok;
            using (var check = File.OpenRead(path)) ok = UpdateRules.Matches(check, hex);
            if (!ok)
            {
                Log.Write("update: checksum mismatch (setup deleted)");
                File.Delete(path);
                return null;
            }
            Log.Write($"update: {offer.Version} downloaded and verified");
            return path;
        }
        catch (Exception e)
        {
            Log.Write($"update: download failed ({Kind(e)})");
            return null;
        }
    }

    /// <summary>Starts the verified setup for an unattended upgrade (it closes Sahar, installs and starts her again).</summary>
    public static bool Launch(string setupPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(setupPath)!;
            using var p = Process.Start(new ProcessStartInfo(setupPath, UpdateRules.SetupArguments(Path.Combine(dir, "setup.log")))
            {
                UseShellExecute = false,
                WorkingDirectory = dir,
            });
            return p is not null;
        }
        catch (Exception e)
        {
            Log.Write($"update: setup not started ({Kind(e)})");
            return false;
        }
    }

    private async Task<HttpResponseMessage> Get(string url, bool api, CancellationToken ct)
    {
        for (int hop = 0; hop <= MaxHops; hop++)
        {
            if (!UpdateRules.AllowedUrl(url, _loopback)) throw new UpdateRefusedException("address not allowed");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (api) request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
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
