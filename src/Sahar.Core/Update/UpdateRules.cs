using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Sahar.Core.Update;

/// <summary>
/// What the update check may do (owner roadmap item, 2026-11-13): which setup a release offers, which URLs may be
/// fetched (api.github.com and the release asset hosts only), how the published checksum is read and checked, and
/// how the setup is run for an upgrade. The engine only does the HTTP, the files and the process start.
/// </summary>
public static partial class UpdateRules
{
    public const string Repo = "A7ma-B25/sahar";
    private const string GitHubApi = "https://api.github.com";

    /// <summary>All she sends about herself: her name and version.</summary>
    public static string UserAgent => $"Sahar/{CoreInfo.Version}";

    /// <summary>The GitHub API address of the channel's release (<paramref name="apiBase"/>: test mode's fake API).</summary>
    public static Uri ReleaseApi(UpdateChannel channel, string? apiBase = null) =>
        new($"{(apiBase ?? GitHubApi).TrimEnd('/')}/repos/{Repo}/releases/" + (channel == UpdateChannel.Stable ? "latest" : "tags/test-build"));

    [GeneratedRegex(@"^Sahar-Setup-(?<v>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\.exe$")]
    private static partial Regex SetupName();

    [GeneratedRegex(@"^(?<hex>[0-9A-Fa-f]{64})(?:\s+\*?(?<name>\S.*?))?\s*$")]
    private static partial Regex ChecksumLine();

    /// <summary>The newest setup in <paramref name="release"/> that is newer than <paramref name="current"/> and has a
    /// "&lt;setup&gt;.sha256" beside it, both at allowed URLs; null when there is none (never an unverified setup).</summary>
    public static UpdateOffer? Pick(Release release, SemVersion current, bool allowLoopback)
    {
        if (release.Draft) return null;
        UpdateOffer? best = null;
        foreach (var setup in release.Assets)
        {
            var m = SetupName().Match(setup.Name);
            if (!m.Success || !SemVersion.TryParse(m.Groups["v"].Value, out var v) || v <= current) continue;
            var sum = release.Assets.FirstOrDefault(a => a.Name == setup.Name + ".sha256");
            if (sum is null || !AllowedUrl(setup.Url, allowLoopback) || !AllowedUrl(sum.Url, allowLoopback)) continue;
            if (best is null || v > best.Version) best = new UpdateOffer(v, setup, sum);
        }
        return best;
    }

    /// <summary>https to api.github.com, github.com or a *.githubusercontent.com asset host; plain http only to this
    /// machine and only when <paramref name="allowLoopback"/> (test mode's fake API).</summary>
    public static bool AllowedUrl(string url, bool allowLoopback)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        string host = u.IdnHost.ToLowerInvariant();
        if (u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort)
            return host is "api.github.com" or "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        return allowLoopback && u.Scheme == Uri.UriSchemeHttp && (host is "127.0.0.1" or "localhost");
    }

    /// <summary>The SHA-256 in a ".sha256" file ("hex  name", "hex *name" or bare hex), lowercase; null when there is
    /// none or the file names another file.</summary>
    public static string? ParseChecksum(string text, string fileName)
    {
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line is null) return null;
        var m = ChecksumLine().Match(line);
        if (!m.Success) return null;
        if (m.Groups["name"].Success && m.Groups["name"].Value != fileName) return null;
        return m.Groups["hex"].Value.ToLowerInvariant();
    }

    /// <summary>True when the SHA-256 of <paramref name="content"/> is <paramref name="hex"/> (any case).</summary>
    public static bool Matches(Stream content, string hex) =>
        hex.Length == 64 && string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), hex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Inno Setup's flags for an unattended upgrade; the installer itself closes and restarts Sahar.</summary>
    public static string SetupArguments(string logPath) => $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG=\"{logPath}\"";
}
