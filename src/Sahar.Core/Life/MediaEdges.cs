using Sahar.Core.Brain;

namespace Sahar.Core.Life;

/// <summary>
/// Turns "something is playing" readings into the brain's music start/stop events and a "video" flag.
/// Browsers report every tab as music (Chromium hard-codes it): browser playback shows as music (headphones),
/// and while the browser is in front it also counts as video, so she makes no big moves over it.
/// </summary>
public sealed class MediaEdges
{
    private static readonly string[] Browsers = ["chrome", "msedge", "firefox", "308046B0AF4A39CB", "brave", "opera", "vivaldi"];
    private bool _music;

    public bool VideoPlaying { get; private set; }

    public static bool IsBrowser(string appId)
    {
        foreach (var b in Browsers)
            if (appId.Contains(b, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Is this executable a web browser? (Electron apps share Chromium's window class, so the class can't tell.)</summary>
    public static bool IsBrowserProcess(string exePath) =>
        Path.GetFileNameWithoutExtension(exePath[(exePath.LastIndexOfAny(['\\', '/']) + 1)..]).ToLowerInvariant() is "chrome" or "msedge" or "firefox" or "brave" or "opera" or "vivaldi"
            or "arc" or "chromium" or "thorium" or "zen" or "librewolf" or "waterfox" or "floorp" or "yandex";

    public IReadOnlyList<EventKind> Update(bool playing, bool isVideo, bool fromBrowser, bool browserInFront)
    {
        bool music = playing && !isVideo;
        VideoPlaying = playing && (isVideo || (fromBrowser && browserInFront));
        if (music == _music) return [];
        _music = music;
        return [music ? EventKind.Music : EventKind.MusicStop];
    }
}
