namespace Sahar.Core.Brain;

/// <summary>Where the fullscreen app is, seen from her: nowhere, on her monitor (she hides), or on another one (she stays).</summary>
public enum FullscreenWhere { None, Hers, Elsewhere }

/// <summary>
/// The owner's rule (0.10.1): she hides only when she would be over the fullscreen content — on other monitors she
/// stays. Monitor indexes are positions in the engine's monitor list.
/// </summary>
public static class FullscreenScope
{
    /// <summary>Nothing fullscreen.</summary>
    public const int NoMonitor = -1;
    /// <summary>Fullscreen on a monitor not in her list (it just appeared): treated as hers, so she hides as before.</summary>
    public const int UnknownMonitor = -2;

    public static FullscreenWhere Where(int fullscreenMonitor, int herMonitor) =>
        fullscreenMonitor == NoMonitor ? FullscreenWhere.None
        : fullscreenMonitor == UnknownMonitor || herMonitor < 0 || fullscreenMonitor == herMonitor ? FullscreenWhere.Hers
        : FullscreenWhere.Elsewhere;
}
