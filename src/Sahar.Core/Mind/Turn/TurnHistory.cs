using System.Text.Json;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Mind.Turn;

/// <summary>One kept exchange: his line, hers, which brain answered (local | online).</summary>
public sealed record HistoryTurn(DateTimeOffset At, string User, string Her, string Brain);

/// <summary>
/// The last turns, kept so she remembers them after a restart (spec 0.17 §4): encrypted in her SecretBox
/// (<c>turn-history</c>), at most <see cref="Max"/> pairs, older than MindEpisodeDays dropped, gone with «انسي كل شي»
/// (the box key is shredded). Each brain reads its own budget from it (local: 6 short turns / 800 tokens as before;
/// online: only the online pairs — review I1 — up to its prompt cap). Read fresh each time (the engine clears the online
/// pairs on «حكي جديد» through the same box); a box that cannot be read this moment is never written over. Never logs.
/// </summary>
public sealed class TurnHistory(ISecretBox box, Func<DateTimeOffset> now, Func<int> days)
{
    public const string File = "turn-history";
    public const int Max = 40;
    private readonly object _gate = new();

    public void Add(string user, string her, string brain)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(her)) return;
        lock (_gate)
        {
            if (Load() is not { } all) return;
            all.Add(new HistoryTurn(now(), user, her, brain));
            if (all.Count > Max) all.RemoveRange(0, all.Count - Max);
            Save(all);
        }
    }

    /// <summary>Review I1: «حكي جديد» or a deleted conversation — the online pairs go (the local brain keeps its own).</summary>
    public void Clear(string brain)
    {
        lock (_gate)
        {
            if (Load() is not { } all || all.RemoveAll(t => t.Brain == brain) == 0) return;
            Save(all);
        }
    }

    public IReadOnlyList<HistoryTurn> Recent()
    {
        lock (_gate)
        {
            var cut = now() - TimeSpan.FromDays(Math.Clamp(days(), 1, 3650));
            return (Load() ?? []).Where(t => t.At >= cut).ToList();
        }
    }

    /// <summary>The kept turns, or null when the box cannot be read this moment.</summary>
    private List<HistoryTurn>? Load()
    {
        var list = new List<HistoryTurn>();
        try
        {
            if (!box.TryRead(File, out var bytes)) return null;
            if (bytes is { Length: > 0 })
            {
                using var doc = JsonDocument.Parse(bytes);
                foreach (var e in doc.RootElement.EnumerateArray())
                {
                    string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                    if (!DateTimeOffset.TryParse(S("at"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var at)) continue;
                    if (S("u").Length > 0 && S("h").Length > 0) list.Add(new HistoryTurn(at, S("u"), S("h"), S("b") is "online" ? "online" : "local"));
                }
            }
        }
        catch (JsonException) { list.Clear(); }
        catch (InvalidOperationException) { list.Clear(); }
        return list;
    }

    private void Save(List<HistoryTurn> all)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartArray();
            foreach (var t in all)
            {
                w.WriteStartObject();
                w.WriteString("at", t.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                w.WriteString("u", t.User);
                w.WriteString("h", t.Her);
                w.WriteString("b", t.Brain);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        try { box.Write(File, ms.ToArray()); }
        catch (IOException) { }
    }
}
