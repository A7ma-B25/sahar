namespace Sahar.Core.Life;

/// <summary>Her last own action, for learning: a reaction within 15 s of it counts, and only once per action.</summary>
public sealed class ActionMemory
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(15);
    private (LifeAction Action, DateTimeOffset At)? _last;

    public void Started(LifeAction action, DateTimeOffset at) => _last = (action, at);

    /// <summary>The action to learn from now, or null; after it is taken, further reactions to it are ignored.</summary>
    public LifeAction? TakeForFeedback(DateTimeOffset now)
    {
        if (_last is not { } l || now - l.At > Window || now < l.At) return null;
        _last = null;
        return l.Action;
    }
}
