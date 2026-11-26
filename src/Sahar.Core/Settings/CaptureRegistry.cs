namespace Sahar.Core.Settings;

/// <summary>
/// T8 «اسمح بتصويرها» (0.15.0): every window of hers that screen capture leaves out (WDA_EXCLUDEFROMCAPTURE) — the
/// overlay, her input window, bubbles, the card, the chat field, her tag and panels. A window's owner says whether it
/// wants to be left out (always, except with the test switch --capturable); he decides whether that happens
/// (<see cref="SaharSettings.AllowCapture"/>). When he changes it, every window still open is set again, live.
/// </summary>
public sealed class CaptureRegistry
{
    private readonly Dictionary<nint, bool> _wanted = [];

    /// <summary>He allows her windows in recordings and screenshots.</summary>
    public bool Allowed { get; private set; }

    public int Count => _wanted.Count;

    /// <summary>The windows left out of capture right now.</summary>
    public int ExcludedCount => _wanted.Count(w => Excluded(w.Value));

    private bool Excluded(bool wanted) => wanted && !Allowed;

    /// <summary>A new window (or one set again): whether it is left out of capture.</summary>
    public bool Add(nint hwnd, bool wanted)
    {
        _wanted[hwnd] = wanted;
        return Excluded(wanted);
    }

    /// <summary>The window is gone.</summary>
    public void Remove(nint hwnd) => _wanted.Remove(hwnd);

    /// <summary>His choice; the windows to set again (every one, with its new state), or none when nothing changed.</summary>
    public IReadOnlyList<(nint Hwnd, bool Excluded)> SetAllowed(bool allowed)
    {
        if (allowed == Allowed) return [];
        Allowed = allowed;
        return [.. _wanted.Select(w => (w.Key, Excluded(w.Value)))];
    }
}
