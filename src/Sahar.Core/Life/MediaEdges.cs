using Sahar.Core.Brain;

namespace Sahar.Core.Life;

/// <summary>Turns "something is playing" readings into the brain's music start/stop events; video is tracked separately.</summary>
public sealed class MediaEdges
{
    private bool _music;

    public bool VideoPlaying { get; private set; }

    public IReadOnlyList<EventKind> Update(bool playing, bool isVideo)
    {
        bool music = playing && !isVideo;
        VideoPlaying = playing && isVideo;
        if (music == _music) return [];
        _music = music;
        return [music ? EventKind.Music : EventKind.MusicStop];
    }
}
