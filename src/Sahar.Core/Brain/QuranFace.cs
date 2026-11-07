namespace Sahar.Core.Brain;

/// <summary>
/// While the Quran plays her resting face is the quran face: it covers idle, music (paused for it) and being
/// "away" (listening without touching the PC is not being away, so she never dozes off during a recitation).
/// Events that need you still show above it.
/// </summary>
public static class QuranFace
{
    public static string Over(string brainKey, bool quranPlaying) =>
        quranPlaying && brainKey is "idle" or "music" or "sleep" or "back" ? "quran" : brainKey;
}
