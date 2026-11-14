using System.Text.Json;
using System.Text.Json.Serialization;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Mind.Log;

/// <summary>
/// «شو عملت اليوم» (spec §3.7): every action she ran, asked or refused, through the encrypted box (never a plain
/// file), kept 30 days. Each operation reads the box, changes it and writes it back, so the engine and a fallback
/// reader in the settings app see the same lines. Undo runs the tool's inverse while it still means something; then
/// the line says «رجّعتها» and has no undo any more. Never logs.
/// </summary>
public sealed class ActionLog(ISecretBox box, Func<string, ITool?> tool, Func<DateTimeOffset> now) : IActionLog
{
    public const string File = "actions.json";
    public static readonly TimeSpan Keep = TimeSpan.FromDays(30);
    private readonly object _gate = new();

    public ActionEntry Append(string tool, string summary, ActSource source, string? why, ActionOutcome outcome, string? undoToken)
    {
        lock (_gate)
        {
            var list = Load();
            var e = new ActionEntry(Guid.NewGuid().ToString("n"), now(), tool, summary, source, Clip(why, 80), outcome,
                outcome == ActionOutcome.Done || outcome == ActionOutcome.AskedYes ? undoToken : null);
            list.Add(e);
            Save(list);
            return e;
        }
    }

    public IReadOnlyList<ActionEntry> Day(DateOnly day)
    {
        lock (_gate) return Load().Where(e => DateOnly.FromDateTime(e.At.ToLocalTime().DateTime) == day).OrderByDescending(e => e.At).ToList();
    }

    public IReadOnlyList<ActionEntry> All()
    {
        lock (_gate) return Load().OrderByDescending(e => e.At).ToList();
    }

    public bool Undo(string id)
    {
        lock (_gate)
        {
            var list = Load();
            int i = list.FindIndex(e => e.Id == id);
            if (i < 0 || list[i].UndoToken is not { } token || list[i].Outcome is not (ActionOutcome.Done or ActionOutcome.AskedYes)) return false;
            if (tool(list[i].Tool) is not { } t || !t.Undo(token)) return false;
            list[i] = list[i] with { Outcome = ActionOutcome.Undone, UndoToken = null };
            Save(list);
            return true;
        }
    }

    public void Feedback(string id, int value)
    {
        lock (_gate)
        {
            var list = Load();
            int i = list.FindIndex(e => e.Id == id);
            if (i < 0) return;
            list[i] = list[i] with { Feedback = Math.Sign(value) };
            Save(list);
        }
    }

    private static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];

    private List<ActionEntry> Load()
    {
        byte[]? bytes;
        try { bytes = box.Read(File); }
        catch (Exception) { return []; }   // unreadable (a forgotten key): start empty; never throws
        if (bytes is null || bytes.Length == 0) return [];
        List<ActionEntry>? list;
        try { list = JsonSerializer.Deserialize(bytes, ActionLogJson.Default.ListActionEntry); }
        catch (JsonException) { return []; }
        var cut = now() - Keep;
        return list?.Where(e => e is not null && e.At >= cut).ToList() ?? [];
    }

    private void Save(List<ActionEntry> list)
    {
        var cut = now() - Keep;
        list.RemoveAll(e => e.At < cut);
        box.Write(File, JsonSerializer.SerializeToUtf8Bytes(list, ActionLogJson.Default.ListActionEntry));
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ActionEntry>))]
public sealed partial class ActionLogJson : JsonSerializerContext;
