using System.Text.Json.Serialization;

namespace Sahar.Core.Mind.Tools;

/// <summary>One tool of a server as she keeps it (sealed in her box). <see cref="Hash"/> is the descriptor last listed;
/// <see cref="Approved"/> the one he approved (null = never). <see cref="Enabled"/> = he wants it on; it is announced
/// only while its state is ok (<see cref="McpCatalog.StateOf"/>).</summary>
public sealed record McpTool(string Name, string Description, string Schema, string Hash, string? Approved, bool Enabled,
    ToolGrant Grant, bool HisSensitive, DateTimeOffset? ApprovedAt);

/// <summary>A server he added on «الأدوات»: <see cref="Kind"/> url (Streamable HTTP) or exe (a local program over stdio).
/// <see cref="ExeSha256"/> pins the program he approved; a changed file pauses it. The secret itself is not here
/// (box entry <c>tools-secrets</c>); only its kind (header | env) and name.</summary>
public sealed record McpServer(string Id, string Name, string Slug, string Kind, string? Url, string? Exe, IReadOnlyList<string> Args,
    string? ExeSha256, string? SecretKind, string? SecretName, bool Paused, IReadOnlyList<McpTool> Tools);

/// <summary>The media tools: checked once (then announced) and his grant for each.</summary>
public sealed record MediaConfig(bool ImageChecked, bool VoiceChecked, ToolGrant ImageGrant, ToolGrant VoiceGrant);

public sealed record McpToolsFile(IReadOnlyList<McpServer> Servers, MediaConfig Media);

/// <summary>One line of tools-ledger.json: what was approved or changed, when, and its hash — never args or output.</summary>
public sealed record LedgerEntry(DateTimeOffset At, string Event, string Server, string? Tool, string? Hash);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpToolsFile))]
[JsonSerializable(typeof(List<LedgerEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class ToolsJson : JsonSerializerContext;
