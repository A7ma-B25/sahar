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
