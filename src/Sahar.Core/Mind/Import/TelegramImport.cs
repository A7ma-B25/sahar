using System.Text;
using System.Text.Json;

namespace Sahar.Core.Mind.Import;

/// <summary>
/// Telegram Desktop's export (result.json; plan 0.14 Task B2): one chat (<c>{"messages":[…]}</c>) or his whole export
/// (<c>{"personal_information":…,"chats":{"list":[…]}}</c>, which also says who he is). Only <c>type: "message"</c>
/// with text is read; text is a string or a list of plain strings and entity objects (links, mentions) joined.
/// </summary>
public static class TelegramImport
{
    public static ChatExport? Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 }); }
        catch (JsonException) { return null; }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var lines = new List<ChatLine>();
            string? myId = null;
            if (root.TryGetProperty("personal_information", out var me) && me.ValueKind == JsonValueKind.Object
                && me.TryGetProperty("user_id", out var uid) && uid.ValueKind == JsonValueKind.Number)
                myId = "user" + uid.GetRawText();
            if (root.TryGetProperty("messages", out var msgs)) Read(msgs, lines, myId, out _);
            string? mine = null;
            if (root.TryGetProperty("chats", out var chats) && chats.ValueKind == JsonValueKind.Object
                && chats.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
                foreach (var chat in list.EnumerateArray())
                    if (chat.ValueKind == JsonValueKind.Object && chat.TryGetProperty("messages", out var m))
                    {
                        Read(m, lines, myId, out var found);
                        mine ??= found;
                    }
            return lines.Count == 0 ? null : new ChatExport("telegram", lines, mine);
        }
    }

    /// <summary>Messages with text; a forwarded one is someone else's words and is dropped. <paramref name="mine"/>: his
    /// id when a message carries it.</summary>
    private static void Read(JsonElement messages, List<ChatLine> into, string? myId, out string? mine)
    {
        mine = null;
        if (messages.ValueKind != JsonValueKind.Array) return;
        foreach (var m in messages.EnumerateArray())
        {
            if (m.ValueKind != JsonValueKind.Object) continue;
            if (!m.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "message") continue;
            if (!m.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.String) continue;
            if (m.TryGetProperty("forwarded_from", out _)) continue;
            string? senderId = m.TryGetProperty("from_id", out var fid) && fid.ValueKind == JsonValueKind.String ? fid.GetString() : null;
            var sender = from.GetString()!.Trim();
            var text = m.TryGetProperty("text", out var t) ? Text(t).Trim() : "";
            if (sender.Length == 0 || text.Length == 0) continue;
            if (myId is not null && senderId == myId) mine = myId;
            into.Add(new ChatLine(sender, text, senderId));
        }
    }

    private static string Text(JsonElement t)
    {
        if (t.ValueKind == JsonValueKind.String) return t.GetString()!;
        if (t.ValueKind != JsonValueKind.Array) return "";
        var b = new StringBuilder();
        foreach (var part in t.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String) b.Append(part.GetString());
            else if (part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var pt) && pt.ValueKind == JsonValueKind.String) b.Append(pt.GetString());
        }
        return b.ToString();
    }
}
