using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Mind.Protocol;

/// <summary>
/// One line on the private Mind pipe (spec 0.14 §3.2; SEAM shared by all three streams): JSON lines, <c>{"v":1,"t":…}</c>.
/// One flat envelope keeps the AOT JSON simple; each type uses the fields its table row names, the rest stay null.
/// Text fields (<see cref="Text"/>) carry his words or hers and are NEVER logged (spec §3.10.9): log <see cref="T"/>,
/// <see cref="Id"/> and sizes only.
/// </summary>
public sealed record MindMsg
{
    public int V { get; init; } = 1;
    public string T { get; init; } = "";
    /// <summary>The turn id: ties the messages of one turn together.</summary>
    public string? Id { get; init; }

    // ---- engine → mind ----
    /// <summary>hello: the protocol version the engine speaks and her language (ar|en).</summary>
    public string? Version { get; init; }
    public string? Lang { get; init; }
    public MindAddress? Address { get; init; }
    /// <summary>hello: only the tools this engine has now (0.13 activities appear when they exist).</summary>
    public IReadOnlyList<Tools.ToolSpec>? Tools { get; init; }
    public MindCaps? Caps { get; init; }
    /// <summary>turn: chat | voice | initiative | routine | reflect | draft.</summary>
    public string? Source { get; init; }
    /// <summary>turn: his line; say/partial: hers; draft: the drafted text. Never logged.</summary>
    public string? Text { get; init; }
    public Situation? Situation { get; init; }
    /// <summary>tool-result / answer / call: which call of the turn.</summary>
    public int? Call { get; init; }
    public bool? Ok { get; init; }
    /// <summary>tool-result data (JSON text, already free of anything private) or call args (JSON object text).</summary>
    public string? Data { get; init; }
    /// <summary>tool-result error: refused | asked-no | invalid | unavailable; error: model-missing | load-failed | oom | timeout | bad-output | busy | blocked.</summary>
    public string? Error { get; init; }
    public bool? Yes { get; init; }
    /// <summary>download: start | pause | resume | cancel | check (reachability); model: use | add | remove | list.</summary>
    public string? Op { get; init; }
    /// <summary>download / model / bench: the model id (<see cref="Runtime.ModelEntry.Id"/>), or an "add" query (HF link, repo name or local path).</summary>
    public string? Model { get; init; }

    // ---- mind → engine ----
    public string? Tool { get; init; }
    /// <summary>call: the model's reason (≤ 80 chars). Shown in «شو عملت اليوم»; never logged.</summary>
    public string? Why { get; init; }
    /// <summary>state: loading | thinking | speaking | idle | unloaded.</summary>
    public string? Phase { get; init; }
    public string? Mood { get; init; }
    public bool? Final { get; init; }
    /// <summary>propose: profile | routine | goal | lesson | memory.</summary>
    public string? Kind { get; init; }
    public Memory.ProfileItem? Item { get; init; }
    public Runtime.RuntimeReady? Ready { get; init; }
    public Runtime.DownloadProgress? Progress { get; init; }
    public Runtime.BenchResult? Bench { get; init; }
    public IReadOnlyList<Runtime.ModelEntry>? Models { get; init; }
}

/// <summary>How she addresses him (from settings; the Mind never stores it).</summary>
public sealed record MindAddress(Settings.Gender Gender, string? Name);

/// <summary>What the Mind may do this session (settings, read by the engine).</summary>
public sealed record MindCaps(bool Initiative, bool Learn, Memory.MindVoice Voice);

/// <summary>The type names (spec §3.2 tables, plus 0.14's model and draft rows).</summary>
public static class MindTypes
{
    public static readonly IReadOnlySet<string> ToMind = new HashSet<string>
    {
        "hello", "turn", "tool-result", "answer", "cancel", "warm", "unload", "quit", "store-changed", "download", "bench", "model",
    };
    public static readonly IReadOnlySet<string> ToEngine = new HashSet<string>
    {
        "ready", "state", "partial", "call", "say", "propose", "progress", "bench-result", "error", "models", "draft",
    };
}

/// <summary>Encodes and decodes pipe lines. Unknown <c>t</c>, a wrong <c>v</c>, bad JSON or a line over
/// <see cref="MaxLine"/> bytes decode to null (the reader drops it and logs the kind only).</summary>
public static class MindWire
{
    public const int MaxLine = 256 * 1024;

    public static string Encode(MindMsg m) => JsonSerializer.Serialize(m, MindJson.Default.MindMsg);

    public static MindMsg? Decode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || Encoding.UTF8.GetByteCount(line) > MaxLine) return null;
        MindMsg? m;
        try { m = JsonSerializer.Deserialize(line, MindJson.Default.MindMsg); }
        catch (JsonException) { return null; }
        if (m is null || m.V != 1) return null;
        return MindTypes.ToMind.Contains(m.T) || MindTypes.ToEngine.Contains(m.T) ? m : null;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MindMsg))]
[JsonSerializable(typeof(Runtime.ModelManifest))]
[JsonSerializable(typeof(Runtime.DownloadState))]
[JsonSerializable(typeof(Memory.ProfileItem))]
public sealed partial class MindJson : JsonSerializerContext;
