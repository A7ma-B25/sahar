using Sahar.Core.Brain;

namespace Sahar.Core.Life;

/// <summary>
/// Turns "something is playing" readings into the brain's music start/stop events and a "video" flag.
/// Browsers report every tab as music (Chromium hard-codes it), so browser playback never shows the music face;
/// while the browser is in front it counts as video (she keeps still).
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

    public IReadOnlyList<EventKind> Update(bool playing, bool isVideo, bool fromBrowser, bool browserInFront)
    {
        bool music = playing && !isVideo && !fromBrowser;
        VideoPlaying = playing && (isVideo || (fromBrowser && browserInFront));
        if (music == _music) return [];
        _music = music;
        return [music ? EventKind.Music : EventKind.MusicStop];
    }
}
