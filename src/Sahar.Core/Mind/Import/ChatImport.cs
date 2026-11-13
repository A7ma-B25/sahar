namespace Sahar.Core.Mind.Import;

/// <summary>One message of an exported chat: who sent it and its text (multi-line kept).</summary>
public sealed record ChatLine(string Sender, string Text);

/// <summary>How many messages a sender has (the page asks «مين إنت؟» with these; never stored, never logged).</summary>
public sealed record SenderCount(string Name, int Count);

/// <summary>A parsed export (plan 0.14 Task B2; spec §7.5). Other people's messages live only in this object for the
/// preview; <see cref="His"/> is all that may be kept. <see cref="Me"/>: his sender name when the file says it
/// (Telegram's own export).</summary>
public sealed record ChatExport(string Format, IReadOnlyList<ChatLine> Lines, string? Me = null)
{
    public IReadOnlyList<SenderCount> Senders =>
        Lines.GroupBy(l => l.Sender, StringComparer.Ordinal).Select(g => new SenderCount(g.Key, g.Count()))
            .OrderByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();

    /// <summary>Only his own lines (exact sender name: "Ahmad Ali" is not "Ahmad"), scrubbed, empty ones dropped.</summary>
    public IReadOnlyList<string> His(string me) =>
        Lines.Where(l => string.Equals(l.Sender, me, StringComparison.Ordinal))
            .Select(l => Scrub.Clean(l.Text).Trim()).Where(t => t.Length > 0).ToList();
}

/// <summary>
/// Reads a chat he exported: WhatsApp's .txt (Android or iPhone, Arabic or English phones) or Telegram Desktop's
/// result.json. Pure parsing in Core: no model, no tools, no network (spec §3.10.3: the import runs with no tools).
/// Null when the content is not a chat.
/// </summary>
public static class ChatImport
{
    /// <summary>The largest export read (a few years of one chat); bigger files are refused by the page.</summary>
    public const int MaxChars = 8 * 1024 * 1024;

    public static ChatExport? Parse(string content, string? fileName = null)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxChars) return null;
        var start = content.TrimStart('﻿', ' ', '\r', '\n', '\t');
        var chat = start.StartsWith('{') ? TelegramImport.Parse(start) : WhatsAppImport.Parse(content);
        return chat is { Lines.Count: > 0 } ? chat : null;
    }

    /// <summary>Directional and invisible marks phones put in exports (LRM, RLM, embeddings, isolates, ALM, BOM).</summary>
    internal static string StripMarks(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '‎' or '‏' or '؜' or '﻿' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩')) continue;
            b.Append(c is ' ' or ' ' ? ' ' : c);
        }
        return b.ToString();
    }
}
