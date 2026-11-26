using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Chat;

public enum ChatWho { Me, Her }
public enum ChatLine { Text, Draft, Ask, Receipt }

/// <summary>One line in a conversation. <see cref="Answer"/> (yes | no | timeout) only for an ask.</summary>
public sealed record ChatEntry(DateTimeOffset At, ChatWho Who, ChatLine Kind, string Text)
{
    public string? Answer { get; init; }
}

/// <summary>A conversation in the list: its title and last line are content too (encrypted like the rest).</summary>
public sealed record ChatInfo(string Id, string Title, DateTimeOffset Created, DateTimeOffset Last, int Count, string LastLine);

/// <summary>
/// Chat history (0.15 spec §6; owner ruling 2): <c>chats.index</c> and <c>chat.&lt;id&gt;</c> in the Mind's personal
/// SecretBox (AES-GCM, DPAPI key, cross-process mutex). Retention follows MindEpisodeDays (clamped 1–3650, default 30):
/// older messages never show and go at the next write or <see cref="Prune"/>; an empty conversation disappears.
/// A box that refuses (locked / forgotten / busy) keeps the session going in memory with <see cref="Saving"/> false.
/// Never logs; names carry no text.
/// </summary>
public sealed class ChatStore(ISecretBox box, Func<DateTimeOffset> now, Func<int>? days = null)
{
    public const string IndexName = "chats.index";
    public const int TitleWords = 4;
    private Dictionary<string, Conversation>? _all;

    /// <summary>False once the box refused a write this session (the list says «ما قدرت احفظ الحكي هلق»).</summary>
    public bool Saving { get; private set; } = true;

    public static string FileName(string id) => "chat." + id;

    private sealed class Conversation
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Named { get; set; }
        public DateTimeOffset Created { get; set; }
        public List<ChatEntry> Messages { get; set; } = [];
    }


    private DateTimeOffset Cut => now().AddDays(-Math.Clamp(days?.Invoke() ?? 30, 1, 3650));

    private Dictionary<string, Conversation> All()
    {
        if (_all is not null) return _all;
        _all = [];
        var index = Parse(box.Read(IndexName), ChatJson.Default.ListChatIndexRow) ?? [];
        foreach (var row in index)
        {
            var messages = Parse(box.Read(FileName(row.Id)), ChatJson.Default.ListChatEntry) ?? [];
            _all[row.Id] = new Conversation { Id = row.Id, Title = row.Title, Named = row.Named, Created = row.Created, Messages = messages };
        }
        return _all;
    }

    private static T? Parse<T>(byte[]? data, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        if (data is null) return default;
        try { return JsonSerializer.Deserialize(data, info); }
        catch (JsonException) { return default; }
    }

    /// <summary>What is past the days is never shown, even before a write drops it.</summary>
    private List<Conversation> Live()
    {
        var cut = Cut;
        return All().Values.Select(c => (c, kept: c.Messages.Where(m => m.At >= cut).ToList()))
            .Where(x => x.kept.Count > 0)
            .Select(x => new Conversation { Id = x.c.Id, Title = x.c.Title, Named = x.c.Named, Created = x.c.Created, Messages = x.kept })
            .ToList();
    }

    public IReadOnlyList<ChatInfo> List() =>
        Live().Select(c => new ChatInfo(c.Id, c.Title, c.Created, c.Messages[^1].At, c.Messages.Count, c.Messages[^1].Text))
            .OrderByDescending(c => c.Last).ToList();

    public IReadOnlyList<ChatEntry> Load(string id) =>
        Live().FirstOrDefault(c => c.Id == id)?.Messages ?? (IReadOnlyList<ChatEntry>)[];

    /// <summary>Adds a line; a null or unknown id starts a conversation (its id is returned).</summary>
    public string Append(string? id, ChatEntry m)
    {
        var all = All();
        if (id is null || !all.TryGetValue(id, out var c))
        {
            id = NewId();
            c = new Conversation { Id = id, Created = m.At };
            all[id] = c;
        }
        c.Messages.Add(m);
        if (!c.Named && m.Who == ChatWho.Me && m.Kind == ChatLine.Text && c.Messages.Count(x => x.Who == ChatWho.Me) == 1)
            c.Title = Title(m.Text);
        if (c.Title.Length == 0 && m.Kind == ChatLine.Text) c.Title = Title(m.Text);
        Save(c, writeIndex: true);
        return id;
    }

    /// <summary>The last message of a conversation changed (an ask was answered, a draft copied).</summary>
    public void ReplaceLast(string id, Func<ChatEntry, ChatEntry> change)
    {
        if (!All().TryGetValue(id, out var c) || c.Messages.Count == 0) return;
        c.Messages[^1] = change(c.Messages[^1]);
        Save(c, writeIndex: false);
    }

    public void Rename(string id, string title)
    {
        if (!All().TryGetValue(id, out var c)) return;
        title = title.Trim();
        if (title.Length == 0) return;
        c.Title = title.Length > 60 ? title[..60] : title;
        c.Named = true;
        WriteIndex();
    }

    public void Delete(string id)
    {
        if (!All().Remove(id)) return;
        Try(() => box.Delete(FileName(id)));
        WriteIndex();
    }

    /// <summary>At start and once a day: drops the old messages and empty conversations from the box.</summary>
    public void Prune()
    {
        var cut = Cut;
        bool changed = false;
        foreach (var c in All().Values.ToList())
        {
            int removed = c.Messages.RemoveAll(m => m.At < cut);
            if (removed == 0) continue;
            changed = true;
            if (c.Messages.Count == 0)
            {
                _all!.Remove(c.Id);
                Try(() => box.Delete(FileName(c.Id)));
            }
            else Save(c, writeIndex: false);
        }
        if (changed) WriteIndex();
    }

    /// <summary>«انسي كل شي» shredded the box: the session copy goes too.</summary>
    public void Forget()
    {
        _all = [];
        Saving = true;
    }

    private static string Title(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var t = string.Join(' ', words.Take(TitleWords));
        return t.Length > 40 ? t[..40] + "…" : t;
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private void Save(Conversation c, bool writeIndex)
    {
        var cut = Cut;
        c.Messages.RemoveAll(m => m.At < cut);
        Try(() => box.Write(FileName(c.Id), JsonSerializer.SerializeToUtf8Bytes(c.Messages, ChatJson.Default.ListChatEntry)));
        if (writeIndex) WriteIndex();
    }

    private void WriteIndex()
    {
        var rows = All().Values.Select(c => new ChatIndexRow { Id = c.Id, Title = c.Title, Named = c.Named, Created = c.Created }).ToList();
        Try(() => box.Write(IndexName, JsonSerializer.SerializeToUtf8Bytes(rows, ChatJson.Default.ListChatIndexRow)));
    }

    private void Try(Action write)
    {
        if (!Saving) return;   // once refused, the session goes on in memory (no half-written history)
        try { write(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Saving = false; }
    }
}

internal sealed class ChatIndexRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Named { get; set; }
    public DateTimeOffset Created { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ChatEntry>))]
[JsonSerializable(typeof(List<ChatIndexRow>))]
internal sealed partial class ChatJson : JsonSerializerContext;
