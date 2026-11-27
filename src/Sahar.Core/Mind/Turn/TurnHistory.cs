using System.Text.Json;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Mind.Turn;

/// <summary>One kept exchange: his line, hers, which brain answered (local | online).</summary>
public sealed record HistoryTurn(DateTimeOffset At, string User, string Her, string Brain);

/// <summary>
/// The last turns, kept so she remembers them after a restart (spec 0.17 §4): encrypted in her SecretBox
/// (<c>turn-history</c>), at most <see cref="Max"/> pairs, older than MindEpisodeDays dropped, gone with «انسي كل شي»
/// (the box key is shredded). Each brain reads its own budget from it (local: 6 turns / 800 tokens as before; online:
/// up to the ~4 k prompt). A box that refuses keeps them in memory for this session. Never logs.
/// </summary>
public sealed class TurnHistory(ISecretBox box, Func<DateTimeOffset> now, Func<int> days)
{
    public const string File = "turn-history";
    public const int Max = 40;
    private readonly object _gate = new();
    private List<HistoryTurn>? _all;

    public void Add(string user, string her, string brain)
    {
        if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(her)) return;
        lock (_gate)
        {
            var all = Load();
            all.Add(new HistoryTurn(now(), user, her, brain));
            if (all.Count > Max) all.RemoveRange(0, all.Count - Max);
            Save(all);
        }
    }

    public IReadOnlyList<HistoryTurn> Recent()
    {
        lock (_gate)
        {
            var cut = now() - TimeSpan.FromDays(Math.Clamp(days(), 1, 3650));
            return Load().Where(t => t.At >= cut).ToList();
        }
    }

    private List<HistoryTurn> Load()
    {
        if (_all is not null) return _all;
        var list = new List<HistoryTurn>();
        try
        {
            if (box.Read(File) is { Length: > 0 } bytes)
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
        return _all = list;
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
