namespace Sahar.Core.Mind;

/// <summary>Where her chat reply is drawn.</summary>
public enum ReplyPlace { Header, Bubble }

/// <summary>
/// 0.15.0 (owner: «mind: cancel (card closed)» ×4): closing the card — by him, Esc, or a game or move that closes it —
/// never stops her reply. Her words go to the card's line while it is open and they fit, else to her speech bubble;
/// when the card is closed and her words are long, the bubble gets the short line of what really ran. Pure.
/// </summary>
public static class ReplyRoute
{
    public const bool CardCloseCancels = false;
    /// <summary>Longer than this, a reply in her bubble after the card closed is the confirmation instead.</summary>
    public const int BubbleChars = 60;

    public static ReplyPlace Where(bool cardOpen, bool fitsHeader) => cardOpen && fitsHeader ? ReplyPlace.Header : ReplyPlace.Bubble;

    public static string Final(string said, string? ranLine, bool cardOpen) =>
        !cardOpen && ranLine is { Length: > 0 } && said.Length > BubbleChars ? ranLine : said;
}

/// <summary>
/// Review I2: one chat turn's words in her speech bubble while the card is closed. Her streamed words show only while
/// they are short (≤ <see cref="ReplyRoute.BubbleChars"/>); a turn that acts never streams there (it gets one short
/// confirmation, so nothing long is swapped for a short line before his eyes); once he clicked the bubble away during
/// the turn, nothing more of it appears (a bubble that only timed out is not a dismissal). Pure.
/// </summary>
public sealed class BubbleStream
{
    private bool _stopped, _shown;

    /// <summary>What to draw for her words so far, or null (nothing more streams for this turn).</summary>
    public string? Partial(string words, bool acts, bool dismissed)
    {
        if (_stopped) return null;
        if (dismissed || acts || words.Length > ReplyRoute.BubbleChars) { _stopped = true; return null; }
        _shown = true;
        return words;
    }

    /// <summary>Did this turn draw in the bubble (the engine counts his clicks from then on)?</summary>
    public bool Shown => _shown;

    /// <summary>Her final line for the bubble, or null when he clicked this turn's bubble away.</summary>
    public string? Final(string line, bool dismissed) => dismissed ? null : line;
}
