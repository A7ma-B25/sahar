namespace Sahar.Core.Life;

/// <summary>
/// Motion 23 "بتلفت انتباهك" for faces that need you (Claude asks permission, the timer is done, a reminder):
/// once when the face appears, then every 30 s while it stays, at most three times; never with do-not-disturb.
/// </summary>
public sealed class AttentionPacer
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(30);
    public const int MaxTimes = 3;

    private string _key = "";
    private int _count;
    private DateTimeOffset _last;

    public static bool NeedsYou(string key) => key is "claude-permission" or "timer-done" or "reminder";

    public bool ShouldPlay(string expressionKey, DateTimeOffset now, bool dnd)
    {
        if (expressionKey != _key) { _key = expressionKey; _count = 0; }
        if (dnd || !NeedsYou(expressionKey) || _count >= MaxTimes) return false;
        if (_count > 0 && now - _last < Every) return false;
        _count++;
        _last = now;
        return true;
    }
}
