namespace Sahar.Core.Life;

/// <summary>
/// Are you typing? Recent input while the mouse stays still is the keyboard; it counts as typing until
/// three seconds after the last key. Works in apps without a system caret (browsers, Electron).
/// </summary>
public sealed class TypingDetector
{
    public static readonly TimeSpan RecentInput = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(3);
    private DateTimeOffset? _lastKey;

    public bool Update(DateTimeOffset now, TimeSpan sinceInput, bool cursorMoved)
    {
        if (sinceInput <= RecentInput && !cursorMoved) _lastKey = now - sinceInput;
        return _lastKey is { } k && now - k <= Hold;
    }
}
