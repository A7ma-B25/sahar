using System.Text;
using System.Text.RegularExpressions;
using Sahar.Core.Text;

namespace Sahar.Core.Mind.Import;

/// <summary>
/// WhatsApp's "Export chat" .txt (plan 0.14 Task B2). A message starts with a date and a time, on Android
/// <c>12/11/2026, 21:41 - Name: text</c> and on iPhone <c>[12/11/2026, 21:41:05] Name: text</c>; Arabic phones write
/// Arabic-Indic digits, «،» and «ص/م», with RTL marks between. A line without a date continues the message before it.
/// Lines without "Name: " are WhatsApp's own notes; media, deleted and edited placeholders are dropped.
/// </summary>
public static partial class WhatsAppImport
{
    private static readonly string[] Placeholders =
    [
        "<Media omitted>", "<الوسائط غير مضمنة>", "<تم استبعاد الوسائط>", "image omitted", "video omitted", "audio omitted",
        "sticker omitted", "GIF omitted", "document omitted", "Contact card omitted", "This message was deleted",
        "You deleted this message", "تم حذف هذه الرسالة", "حذفت هذه الرسالة", "لقد حذفت هذه الرسالة", "null",
        "Missed voice call", "Missed video call", "مكالمة صوتية فائتة", "مكالمة فيديو فائتة", "الصورة غير مضمنة",
        "الفيديو غير مضمن", "الصوت غير مضمن", "الملصق غير مضمن", "Waiting for this message",
    ];

    private static readonly string[] EditTags = ["<This message was edited>", "<تم تعديل هذه الرسالة>"];

    public static ChatExport? Parse(string content)
    {
        var lines = new List<ChatLine>();
        string? sender = null;
        StringBuilder? text = null;
        void Flush()
        {
            if (sender is not null && text is not null && Keep(text.ToString()) is { } t) lines.Add(new ChatLine(sender, t));
            sender = null;
            text = null;
        }

        foreach (var raw in content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = ChatImport.StripMarks(raw);
            var m = Header().Match(Digits.ToLatin(line));   // one character for one: positions hold in the original
            if (!m.Success)
            {
                text?.Append('\n').Append(line.TrimEnd());   // the message before goes on
                continue;
            }
            Flush();
            var rest = line[m.Groups["rest"].Index..];
            int colon = rest.IndexOf(": ", StringComparison.Ordinal);
            if (colon <= 0) continue;   // WhatsApp's own note (encryption, someone joined, …)
            sender = CleanName(rest[..colon]);
            text = new StringBuilder(rest[(colon + 2)..].TrimEnd());
            if (sender.Length == 0) { sender = null; text = null; }
        }
        Flush();
        return lines.Count == 0 ? null : new ChatExport("whatsapp", lines);
    }

    /// <summary>The name as written. "~ Name" stays as is: WhatsApp marks someone not in his contacts that way, so it
    /// is never him, even when the name is the same.</summary>
    private static string CleanName(string s) => s.Trim();

    private static string? Keep(string text)
    {
        var t = text.Trim();
        foreach (var tag in EditTags) if (t.EndsWith(tag, StringComparison.Ordinal)) t = t[..^tag.Length].TrimEnd();
        if (t.Length == 0) return null;
        foreach (var p in Placeholders)
            if (string.Equals(t, p, StringComparison.OrdinalIgnoreCase)) return null;
        if (t.EndsWith("(file attached)", StringComparison.Ordinal) || t.EndsWith("(ملف مرفق)", StringComparison.Ordinal)) return null;
        return t;
    }

    // [date, time] rest   or   date, time - rest   (digits already Latin; «،» or "," after the date; AM/PM or ص/م).
    [GeneratedRegex(@"^\s*(?:\[(?<d>\d{1,4}[./-]\d{1,2}[./-]\d{1,4})[,،]?\s*(?<t>\d{1,2}:\d{2}(?::\d{2})?)(?:\s*(?:[AaPp]\.?\s?[Mm]\.?|ص|م))?\]\s?|(?<d>\d{1,4}[./-]\d{1,2}[./-]\d{1,4})[,،]?\s*(?<t>\d{1,2}:\d{2}(?::\d{2})?)(?:\s*(?:[AaPp]\.?\s?[Mm]\.?|ص|م))?\s+-\s)(?<rest>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Header();
}
