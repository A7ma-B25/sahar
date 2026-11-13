using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Chat;

namespace Sahar.Core.Mind.Memory;

/// <summary>
/// The Mind's personal store (plan 0.14 Task B1; spec §7.2, §7.3, §7.8) over <see cref="SecretBox"/>: the profile with
/// its lifecycle (proposed → confirmed / edited, rejected kept as a hash only), the confirmed memories, his style
/// samples and what he listened to. Every call reads, changes and writes under the box's mutex, so the settings app and
/// the Mind can both write and nothing is cached across calls (a «انسي كل شي» in one is seen at once by the other).
/// Consent is read per call (<see cref="LearnConsent"/>, Ruling 3); confidence comes from <see cref="ConfidenceRule"/>,
/// never from the model. Never logs.
/// </summary>
public sealed class MindStore(SecretBox box, Func<DateTimeOffset> now, Func<LearnConsent> consent, Func<int>? episodeDays = null) : IMindStore
{
    public const int MaxMemories = 2000, MaxProfile = 500, MaxSamples = 1000, MaxListens = 2000;
    public const int MaxItemChars = 200, MaxSnippet = 80, MaxSampleChars = 400, MaxSources = 20;
    private const string StoreFile = "store", StyleFile = "style", MusicFile = "music";

    public SecretBox Box => box;

    // ---- the profile ----

    public IReadOnlyList<ProfileItem> Profile() =>
        Read(StoreFile, MemoryJson.Default.StoreData, () => new StoreData()).Profile.Select(r => r.Item).OrderByDescending(i => i.CreatedAt).ToList();

    public ProfileItem? Propose(string text, ProfileCategory category, ItemSource source, Sensitivity sensitive)
    {
        text = Clean(text, MaxItemChars);
        if (text.Length == 0 || !consent().Allows(source.Kind)) return null;
        var hash = Hash(text);
        return Change(StoreFile, MemoryJson.Default.StoreData, () => new StoreData(), d =>
        {
            if (d.Rejected.Contains(hash)) return null;
            var at = now();
            var src = source with { Snippet = sensitive == Sensitivity.None && source.Snippet is { } s ? Clip(Clean(s, MaxSnippet)) : null };
            var row = d.Profile.FirstOrDefault(r => r.Hashes.Contains(hash));
            if (row is null)
            {
                row = new ProfileRow(new ProfileItem(NewId(), text, category, [src], ConfidenceRule.Of([src], at), ItemStatus.Proposed, sensitive, at), [hash]);
                d.Profile.Add(row);
            }
            else
            {
                var sources = row.Item.Sources.Append(src).TakeLast(MaxSources).ToList();
                row.Item = row.Item with
                {
                    Sources = sources, Confidence = ConfidenceRule.Of(sources, at),
                    Sensitive = Stricter(row.Item.Sensitive, sensitive),
                };
            }
            PruneProfile(d);
            return row.Item;
        });
    }

    public bool Confirm(string id) => Update(id, (r, at) => r.Item = r.Item with { Status = ItemStatus.Confirmed, ConfirmedAt = at });

    public bool Edit(string id, string text)
    {
        text = Clean(text, MaxItemChars);
        if (text.Length == 0) return false;
        return Update(id, (r, at) =>
        {
            r.Item = r.Item with { Text = text, Status = ItemStatus.Edited, ConfirmedAt = at };
            var h = Hash(text);
            if (!r.Hashes.Contains(h)) r.Hashes.Add(h);
        });
    }

    /// <summary>«لا»: gone, and its words kept as a hash only so she never proposes them again.</summary>
    public bool Reject(string id) => Remove(id);

    /// <summary>Deleting an item he once kept: Ruling (B1) — the same as «لا», she does not propose it again.</summary>
    public bool Delete(string id) => Remove(id);

    public IReadOnlyList<ProfileItem> ForPrompt(string line, int maxTokens)
    {
        var shaped = Profile().Where(i => i.ShapesHer).ToList();
        var want = Words(line);
        var ranked = shaped
            .Select(i => (Item: i, Score: Overlap(want, Words(i.Text))))
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.ConfirmedAt ?? x.Item.CreatedAt);
        var picked = new List<ProfileItem>();
        int used = 0;
        foreach (var (item, _) in ranked)
        {
            int cost = Tokens.Estimate(item.Text);
            if (used + cost > maxTokens) continue;
            used += cost;
            picked.Add(item);
        }
        return picked;
    }

    // ---- memories ----

    public IReadOnlyList<MemoryItem> Memories(string? search = null)
    {
        var all = Read(StoreFile, MemoryJson.Default.StoreData, () => new StoreData()).Memories.OrderByDescending(m => m.At);
        if (string.IsNullOrWhiteSpace(search)) return all.ToList();
        var want = ChatCommands.Normalize(search);
        var words = Words(search);
        return all.Where(m => ChatCommands.Normalize(m.Text).Contains(want, StringComparison.Ordinal) || Overlap(words, Words(m.Text)) > 0).ToList();
    }

    public MemoryItem AddMemory(string text, MemoryKind kind, Sensitivity sensitive = Sensitivity.None)
    {
        var item = new MemoryItem(NewId(), Clean(text, MaxItemChars), kind, now(), sensitive);
        Change(StoreFile, MemoryJson.Default.StoreData, () => new StoreData(), d =>
        {
            d.Memories.Add(item);
            while (d.Memories.Count > MaxMemories) d.Memories.Remove(d.Memories.MinBy(m => m.At)!);
            return true;
        });
        return item;
    }

    public bool DeleteMemory(string id) =>
        Change(StoreFile, MemoryJson.Default.StoreData, () => new StoreData(), d => d.Memories.RemoveAll(m => m.Id == id) > 0);

    // ---- his style ----

    public StyleCard Style()
    {
        var samples = Read(StyleFile, MemoryJson.Default.StyleData, () => new StyleData()).Samples;
        return Card(samples);
    }

    public int AddSamples(LearnSource source, IEnumerable<string> lines)
    {
        if (!consent().Allows(source)) return 0;
        var clean = lines.Select(l => Clean(l, MaxSampleChars)).Where(l => l.Length >= 2).ToList();
        if (clean.Count == 0) return 0;
        return Change(StyleFile, MemoryJson.Default.StyleData, () => new StyleData(), d =>
        {
            var at = now();
            var seen = d.Samples.Select(s => s.Hash).ToHashSet();
            int added = 0;
            foreach (var line in clean)
            {
                var h = Hash(line);
                if (!seen.Add(h)) continue;
                d.Samples.Add(new SampleRow(source, line, at, h));
                added++;
            }
            if (d.Samples.Count > MaxSamples) d.Samples.RemoveRange(0, d.Samples.Count - MaxSamples);   // oldest first
            return added;
        });
    }

    public IReadOnlyList<string> Samples(string topic, int count)
    {
        var samples = Read(StyleFile, MemoryJson.Default.StyleData, () => new StyleData()).Samples;
        var want = Words(topic);
        return samples
            .Select((s, i) => (s.Text, Score: Overlap(want, Words(s.Text)), Order: i))
            .OrderByDescending(x => x.Score).ThenByDescending(x => x.Order)
            .Take(Math.Max(0, count)).Select(x => x.Text).ToList();
    }

    // ---- music (owner: titles + times, local only, opt-in) ----

    /// <summary>A title he played (already checked against consent by <see cref="MusicLearner"/>).</summary>
    public bool AddListen(string title, DateTimeOffset at)
    {
        if (!consent().Music) return false;
        title = Clean(title, 120);
        if (title.Length == 0) return false;
        return Change(MusicFile, MemoryJson.Default.MusicData, () => new MusicData(), d =>
        {
            d.Listens.Add(new ListenRow(title, at));
            if (d.Listens.Count > MaxListens) d.Listens.RemoveRange(0, d.Listens.Count - MaxListens);
            return true;
        });
    }

    public IReadOnlyList<ListenRow> Listens() => Read(MusicFile, MemoryJson.Default.MusicData, () => new MusicData()).Listens;

    // ---- forget, export ----

    public void ForgetAll() => box.ForgetAll();

    public string Export()
    {
        var d = Read(StoreFile, MemoryJson.Default.StoreData, () => new StoreData());
        var style = Read(StyleFile, MemoryJson.Default.StyleData, () => new StyleData());
        var music = Read(MusicFile, MemoryJson.Default.MusicData, () => new MusicData());
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("exportedAt", now());
            w.WriteString("note", "ذاكرة سحر عنك. هالملف مش مقفول: خليه بمكان أمين.");
            w.WriteStartArray("profile");
            foreach (var i in d.Profile.Select(r => r.Item))
            {
                w.WriteStartObject();
                w.WriteString("text", i.Text);
                w.WriteString("category", i.Category.ToString());
                w.WriteString("status", i.Status.ToString());
                w.WriteString("confidence", i.Confidence.ToString());
                w.WriteString("sensitive", i.Sensitive.ToString());
                w.WriteString("createdAt", i.CreatedAt);
                if (i.ConfirmedAt is { } c) w.WriteString("confirmedAt", c);
                w.WriteStartArray("sources");
                foreach (var s in i.Sources)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", s.Kind.ToString());
                    w.WriteString("at", s.At);
                    if (s.Snippet is { } sn) w.WriteString("snippet", sn);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("memories");
            foreach (var m in d.Memories)
            {
                w.WriteStartObject();
                w.WriteString("text", m.Text);
                w.WriteString("kind", m.Kind.ToString());
                w.WriteString("at", m.At);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("style", Card(style.Samples).SummaryAr);
            w.WriteStartArray("samples");
            foreach (var s in style.Samples)
            {
                w.WriteStartObject();
                w.WriteString("source", s.Source.ToString());
                w.WriteString("text", s.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("listened");
            foreach (var l in music.Listens)
            {
                w.WriteStartObject();
                w.WriteString("title", l.Title);
                w.WriteString("at", l.At);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ---- inside ----

    private bool Update(string id, Action<ProfileRow, DateTimeOffset> change) =>
        Change(StoreFile, MemoryJson.Default.StoreData, () => new StoreData(), d =>
        {
            var row = d.Profile.FirstOrDefault(r => r.Item.Id == id);
            if (row is null) return false;
            change(row, now());
            return true;
        });

    private bool Remove(string id) =>
        Change(StoreFile, MemoryJson.Default.StoreData, () => new StoreData(), d =>
        {
            var row = d.Profile.FirstOrDefault(r => r.Item.Id == id);
            if (row is null) return false;
            d.Profile.Remove(row);
            foreach (var h in row.Hashes) if (!d.Rejected.Contains(h)) d.Rejected.Add(h);
            if (d.Rejected.Count > 5000) d.Rejected.RemoveRange(0, d.Rejected.Count - 5000);
            return true;
        });

    /// <summary>Unconfirmed first, oldest first (spec §7.3).</summary>
    private static void PruneProfile(StoreData d)
    {
        while (d.Profile.Count > MaxProfile)
        {
            var victim = d.Profile.Where(r => r.Item.Status == ItemStatus.Proposed).MinBy(r => r.Item.CreatedAt)
                ?? d.Profile.MinBy(r => r.Item.CreatedAt)!;
            d.Profile.Remove(victim);
        }
    }

    private T Read<T>(string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Func<T> empty)
    {
        var data = Parse(box.Read(name), info) ?? empty();
        Retain(data!);   // what is past the days is never shown, even before the next write drops it
        return data;
    }

    private TResult Change<T, TResult>(string name, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, Func<T> empty, Func<T, TResult> change)
    {
        using var _ = box.Lock();
        var data = Parse(box.ReadLocked(name), info) ?? empty();
        var result = change(data);
        Retain(data!);
        box.WriteLocked(name, JsonSerializer.SerializeToUtf8Bytes(data, info));
        return result;
    }

    /// <summary>«خلّي حكينا» (MindEpisodeDays): the words of their chats and what he played go after the days; what he
    /// confirmed stays.</summary>
    private void Retain(object data)
    {
        int days = Math.Clamp(episodeDays?.Invoke() ?? 30, 1, 3650);
        var cut = now().AddDays(-days);
        switch (data)
        {
            case StoreData d:
                foreach (var r in d.Profile)
                    if (r.Item.Sources.Any(s => s.Snippet is not null && s.At < cut))
                        r.Item = r.Item with { Sources = r.Item.Sources.Select(s => s.At < cut ? s with { Snippet = null } : s).ToList() };
                break;
            case StyleData s:
                s.Samples.RemoveAll(x => x.Source == LearnSource.Chats && x.At < cut);
                break;
            case MusicData m:
                m.Listens.RemoveAll(x => x.At < cut);
                break;
        }
    }

    private static T? Parse<T>(byte[]? bytes, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
    {
        if (bytes is null) return default;
        try { return JsonSerializer.Deserialize(bytes, info); }
        catch (JsonException) { return default; }
    }

    internal static string Clean(string text, int max)
    {
        var s = string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length > max ? s[..max].TrimEnd() : s;
    }

    private static StyleCard Card(IReadOnlyList<SampleRow> samples) =>
        new("", samples.Count, samples.Count(s => s.Source == LearnSource.Imports), samples.Count(s => s.Source == LearnSource.Examples));

    /// <summary>The stricter of two: anything sensitive wins over None.</summary>
    private static Sensitivity Stricter(Sensitivity a, Sensitivity b) => a != Sensitivity.None ? a : b;

    private static string Clip(string s) => s.Length > MaxSnippet ? s[..MaxSnippet] : s;

    /// <summary>The words' hash: normalised (spacing, hamza, digits, end punctuation), so the same words match.</summary>
    internal static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ChatCommands.Normalize(text))), 0, 16);

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    /// <summary>Normalised word stems (light Arabic stemming: و/ال/بال/لل prefixes, ة → ه), two letters or more.</summary>
    internal static HashSet<string> Words(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (var raw in ChatCommands.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = raw.Trim('.', '،', ',', '!', '؟', '?', '«', '»', '"', ':', '(', ')');
            if (w.Length > 3 && w[0] == 'و') w = w[1..];
            foreach (var p in (string[])["بال", "لل", "ال"])
                if (w.Length > p.Length + 2 && w.StartsWith(p, StringComparison.Ordinal)) { w = w[p.Length..]; break; }
            if (w.EndsWith('ة')) w = w[..^1] + "ه";
            if (w.Length >= 2) set.Add(w);
        }
        return set;
    }

    internal static int Overlap(HashSet<string> a, HashSet<string> b) => a.Count == 0 || b.Count == 0 ? 0 : a.Count(b.Contains);
}

/// <summary>Spec §7.2: confidence from the number of independent recent sources, never from the model. Independent =
/// a different day or a different kind; recent = the last 60 days. High: five, or three from two kinds; Medium: three.</summary>
public static class ConfidenceRule
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(60);

    public static Confidence Of(IEnumerable<ItemSource> sources, DateTimeOffset now)
    {
        var independent = sources
            .Where(s => now - s.At <= Window && s.At <= now.AddDays(1))
            .Select(s => (s.Kind, Day: DateOnly.FromDateTime(s.At.Date)))
            .Distinct().ToList();
        int n = independent.Count, kinds = independent.Select(x => x.Kind).Distinct().Count();
        return n >= 5 || (n >= 3 && kinds >= 2) ? Confidence.High : n >= 3 ? Confidence.Medium : Confidence.Low;
    }
}

/// <summary>A rough token count for budgets (Arabic and English, about three characters a token).</summary>
public static class Tokens
{
    public static int Estimate(string? text) => string.IsNullOrEmpty(text) ? 0 : (text.Length + 2) / 3;
}

public sealed class ProfileRow(ProfileItem item, List<string> hashes)
{
    public ProfileItem Item { get; set; } = item;
    /// <summary>Hashes of every wording it had (proposed and edited), so none comes back as a new proposal.</summary>
    public List<string> Hashes { get; set; } = hashes;
}

public sealed class StoreData
{
    public List<ProfileRow> Profile { get; set; } = [];
    public List<string> Rejected { get; set; } = [];
    public List<MemoryItem> Memories { get; set; } = [];
}

public sealed record SampleRow(LearnSource Source, string Text, DateTimeOffset At, string Hash);

public sealed class StyleData
{
    public List<SampleRow> Samples { get; set; } = [];
}

public sealed record ListenRow(string Title, DateTimeOffset At);

public sealed class MusicData
{
    public List<ListenRow> Listens { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(StoreData))]
[JsonSerializable(typeof(StyleData))]
[JsonSerializable(typeof(MusicData))]
internal sealed partial class MemoryJson : JsonSerializerContext;
