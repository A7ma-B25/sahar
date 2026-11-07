namespace Sahar.Core.Motion;

public enum SulkPoke { NotSulking, Hmph, MadeUp }

/// <summary>
/// After an angry shake she sulks for <see cref="Length"/>: a poke gets "هِم!", the third makes up. Each sulk has its
/// own number, so a timer from an earlier sulk never ends a later one, and her own timer always ends hers.
/// </summary>
public sealed class Sulk
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(20);
    private int _generation;
    private bool _on;
    private DateTimeOffset _until;
    private int _pokes;

    public int Start(DateTimeOffset now)
    {
        _on = true;
        _until = now + Length;
        _pokes = 0;
        return ++_generation;
    }

    public bool Active(DateTimeOffset now) => _on && now < _until;

    /// <summary>Her timer for sulk <paramref name="generation"/>: ends it if it is still the current one.</summary>
    public bool TimerFired(int generation) => generation == _generation && End();

    /// <summary>Ends the sulk (made up, a face that needs you, or time); false if she wasn't sulking.</summary>
    public bool End()
    {
        if (!_on) return false;
        _on = false;
        return true;
    }

    public SulkPoke Poke()
    {
        if (!_on) return SulkPoke.NotSulking;
        if (++_pokes < 3) return SulkPoke.Hmph;
        End();
        return SulkPoke.MadeUp;
    }

    public int Pokes => _pokes;
}
