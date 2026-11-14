using System.Globalization;
using System.Security.Cryptography;

namespace Sahar.Core.Update;

/// <summary>
/// What the update check may do (owner roadmap item, 2026-11-13; review fixes): where the channel's signed manifest
/// and setup are (release-download URLs only, no API), which addresses may be fetched (the repo's release downloads
/// first, then only GitHub's asset hosts on a redirect), how the setup is checked and run, and how a portable Sahar
/// learns where she lives after the install. The engine only does the HTTP, the files and the process start.
/// </summary>
public static class UpdateRules
{
    public const string Repo = "A7ma-B25/sahar";
    /// <summary>The installer's AppId (packaging/installer/Sahar.iss): its uninstall entry is "&lt;AppId&gt;_is1".</summary>
    public const string InstallerAppId = "{B24750DD-9A27-4894-8A97-D099CBA9B19C}";
    private const string GitHub = "https://github.com";
    private static readonly string[] FirstHopPaths = [$"/{Repo}/releases/download/", $"/{Repo}/releases/latest/download/"];

    /// <summary>All she sends about herself: her name and version.</summary>
    public static string UserAgent => $"Sahar/{CoreInfo.Version}";

    /// <summary>The folder URL of the channel's release files (<paramref name="host"/>: test mode's fake GitHub).</summary>
    public static string DownloadBase(UpdateChannel channel, string? host = null) =>
        $"{(host ?? GitHub).TrimEnd('/')}/{Repo}/releases/" + (channel == UpdateChannel.Stable ? "latest/download/" : "download/test-build/");

    /// <summary>The signed manifest as an offer when its version is newer than <paramref name="current"/>.</summary>
    public static UpdateOffer? Offer(UpdateManifest manifest, SemVersion current, string downloadBase) =>
        manifest.Version > current ? new UpdateOffer(manifest.Version, manifest.Setup, downloadBase + manifest.Setup, manifest.Sha256) : null;

    /// <summary>The first request: an allowed host and one of this repo's release-download paths.</summary>
    public static bool AllowedFirstHop(string url, bool allowLoopback) =>
        AllowedUrl(url, allowLoopback) && Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        FirstHopPaths.Any(p => u.AbsolutePath.StartsWith(p, StringComparison.Ordinal));

    /// <summary>Any hop: https to github.com or a *.githubusercontent.com asset host on the default port; plain http
    /// only to this machine and only when <paramref name="allowLoopback"/> (test mode's fake GitHub).</summary>
    public static bool AllowedUrl(string url, bool allowLoopback)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        string host = u.IdnHost.ToLowerInvariant();
        if (u.Scheme == Uri.UriSchemeHttps && u.IsDefaultPort)
            return host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
        return allowLoopback && u.Scheme == Uri.UriSchemeHttp && (host is "127.0.0.1" or "localhost");
    }

    /// <summary>True when the SHA-256 of <paramref name="content"/> is <paramref name="hex"/> (any case).</summary>
    public static bool Matches(Stream content, string hex) => Matches(SHA256.HashData(content), hex);

    public static bool Matches(byte[] sha256, string hex) =>
        hex.Length == 64 && string.Equals(Convert.ToHexStringLower(sha256), hex, StringComparison.OrdinalIgnoreCase);

    /// <summary>Inno Setup's flags: silent for an installed Sahar (the installer closes her, then starts her again);
    /// the wizard for a portable one, so you see where she goes and choose start-with-Windows yourself.</summary>
    public static string SetupArguments(string logPath, bool silent) =>
        (silent ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART " : "") + $"/SP- /LOG=\"{logPath}\"";

    /// <summary>She runs installed when her folder is the uninstall entry's InstallLocation; anything else (a zip
    /// folder, no entry) is portable.</summary>
    public static bool IsInstalled(string? installLocation, string exeDirectory)
    {
        static string Norm(string p) => p.Trim().TrimEnd('\\', '/');
        return !string.IsNullOrWhiteSpace(installLocation) &&
            string.Equals(Norm(installLocation), Norm(exeDirectory), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly TimeSpan MoveNoteFor = TimeSpan.FromHours(2);

    /// <summary>What a portable Sahar leaves behind before the wizard: "portable &lt;unix seconds&gt;".</summary>
    public static string MoveMarker(DateTimeOffset now) => "portable " + now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>The installed Sahar says where she lives now when a portable one left for the wizard lately.</summary>
    public static bool SayMoved(string? marker, DateTimeOffset now, bool installed)
    {
        if (!installed || marker is null) return false;
        var parts = marker.Trim().Split(' ');
        if (parts is not ["portable", var s] || !long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out long t)) return false;
        var age = now - DateTimeOffset.FromUnixTimeSeconds(t);
        return age >= TimeSpan.Zero && age <= MoveNoteFor;
    }

    public const string MovedLine = "صرت ساكنة بـ %LocalAppData%\\Programs\\Sahar 🏠 — لاقيني بقائمة ابدأ: «سحر». مجلد الـ zip القديم فيك تمسحو.";
}
