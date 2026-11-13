namespace Sahar.Core.Mind.Runtime;

/// <summary>One chat message for the model. <see cref="Role"/>: system | user | assistant.</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>Sampling (spec §3.5): chat 0.7 / 0.9 / 40, ≤ 160 tokens; reflection may think, ≤ 2 K.</summary>
public sealed record Sampling(double Temperature = 0.7, double TopP = 0.9, int TopK = 40, int MaxTokens = 160)
{
    public static readonly Sampling Chat = new();
}

/// <summary>A completion request. <see cref="JsonSchema"/> non-null = the reply is grammar-constrained to it
/// (llama-server <c>response_format: json_schema</c>).</summary>
public sealed record LlmRequest(IReadOnlyList<ChatMessage> Messages, string? JsonSchema = null, Sampling? Sampling = null);

/// <summary>The model's whole reply plus timings (numbers only reach logs).</summary>
public sealed record LlmReply(string Text, int PromptTokens, int OutputTokens, TimeSpan FirstToken, TimeSpan Total);

/// <summary>Stream A: the running model (llama-server over 127.0.0.1 with a per-launch key). Loads on demand; throws
/// <see cref="MindRuntimeException"/> with a protocol error code (model-missing | load-failed | oom | timeout).
/// <paramref name="partial"/> gets streamed text chunks.</summary>
public interface ILlm
{
    Task<LlmReply> CompleteAsync(LlmRequest request, Action<string>? partial, CancellationToken ct);
}

public sealed class MindRuntimeException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>Hardware tiers (spec §4.3). None keeps today's command parser only.</summary>
public enum Tier { None, Light, StandardCpu, Gpu6, Gpu8 }

public enum Backend { Auto, Cpu, Vulkan, Cuda }

/// <summary>What a model can do, decided by the benchmark's tool-call check: Tools, or ChatOnly (commands stay with
/// the parser; the page shows «حكي بس»).</summary>
public enum ModelAbility { Unknown, Tools, ChatOnly }

/// <summary>Where a model comes from: recommended (pinned in the build), Hugging Face (added by link or name; pinned
/// by the commit and the LFS SHA-256 at add time), or a local .gguf he already has.</summary>
public enum ModelOrigin { Recommended, HuggingFace, Local }

/// <summary>One model Sahar knows: <see cref="Id"/> is stable (recommended: "gemma-4-e4b-q4km"; HF: "hf:owner/repo/file";
/// local: "file:" + SHA-256 prefix). Role: default | challenger | light | custom.</summary>
public sealed record ModelEntry(
    string Id, string Name, ModelOrigin Origin, string Role, long Bytes, string? Sha256, string? Repo = null,
    string? Commit = null, string? File = null, string? LocalPath = null, string Quant = "", string License = "",
    bool Installed = false, bool Active = false, ModelAbility Ability = ModelAbility.Unknown, BenchResult? Bench = null);

/// <summary>The models file (models.json under %LocalAppData%\Sahar\models; not personal).</summary>
public sealed record ModelManifest(IReadOnlyList<ModelEntry> Models, string? Active);

/// <summary>The runtime is up: the model, its tier and backend, the load time and memory (numbers only).</summary>
public sealed record RuntimeReady(string Model, string Quant, Tier Tier, Backend Backend, int LoadMs, int VramMB, int RamMB);

/// <summary>Download progress (spec §6). <see cref="State"/>: checking | blocked | asking | downloading | paused |
/// verifying | done | failed | no-disk. <see cref="Source"/>: hf | mirror | github.</summary>
public sealed record DownloadProgress(string Model, string File, long Done, long Total, long Bps, string State, string? Source = null, string? Error = null);

/// <summary>The resumable state kept next to a .part file: what is being fetched, how far, the validator (ETag) and
/// the pinned hash. Survives restarts.</summary>
public sealed record DownloadState(string Url, string Sha256, long Total, long Offset, string? ETag, string State, DateTimeOffset Updated);

/// <summary>The in-app benchmark («جرّبيني»): speed, memory, an Arabic/dialect check and a tool-call check.</summary>
public sealed record BenchResult(
    string Model, int LoadMs, int FirstTokenMs, double TokPerSec, double PrefillTokPerSec, int VramMB, int RamMB,
    int DialectPass, int DialectTotal, int ToolPass, int ToolTotal, DateTimeOffset At)
{
    /// <summary>The tool-call check gate: ≥ 80 % of the tool rows, else the model is chat-only.</summary>
    public ModelAbility Ability => ToolTotal > 0 && ToolPass * 5 >= ToolTotal * 4 ? ModelAbility.Tools : ModelAbility.ChatOnly;
}

/// <summary>Stream A: the Mind's runtime supervisor (llama-server child process, idle unload, health).</summary>
public interface IMindRuntime
{
    /// <summary>Starts llama-server for the active model if needed; returns once healthy.</summary>
    Task<RuntimeReady> WarmAsync(CancellationToken ct);
    /// <summary>Stops llama-server (graceful, then hard after 3 s): RAM and VRAM back to 0.</summary>
    Task UnloadAsync();
    bool Loaded { get; }
    ILlm Llm { get; }
}
