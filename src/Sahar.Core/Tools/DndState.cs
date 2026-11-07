namespace Sahar.Core.Tools;

/// <summary>"Do not disturb" from her menu (spec §7.1): for a while, or until turned off.</summary>
public sealed class DndState
{
    private bool _on;
    private DateTimeOffset? _until;

    public DateTimeOffset? Until => _on ? _until : null;

    /// <param name="length">How long; null = until turned off.</param>
    public void TurnOn(TimeSpan? length, DateTimeOffset now)
    {
        _on = true;
        _until = length is { } l ? now + l : null;
    }

    public void TurnOff() => _on = false;

    public bool IsOn(DateTimeOffset now) => _on && (_until is not { } u || now < u);

    /// <summary>True once, when a timed "do not disturb" has run out.</summary>
    public bool TakeEnded(DateTimeOffset now)
    {
        if (!_on || _until is not { } u || now < u) return false;
        _on = false;
        return true;
    }
}
