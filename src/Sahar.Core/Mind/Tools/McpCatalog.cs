using System.Text.Json;

namespace Sahar.Core.Mind.Tools;

/// <summary>One tool as an MCP server lists it (untrusted: name, description, schema and annotations are the server's).</summary>
public sealed record ListedTool(string Name, string? Description, JsonElement InputSchema, JsonElement? Annotations);
