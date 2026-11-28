using System.Text.Json;
using Sahar.Core.Mind.Protocol;

namespace Sahar.Core.Mind.Tools;

/// <summary>One tool as an MCP server lists it (untrusted: name, description, schema and annotations are the server's).</summary>
public sealed record ListedTool(string Name, string? Description, JsonElement InputSchema, JsonElement? Annotations);

/// <summary>
/// 0.19 spec §5–§6: her tool config as pure functions. <see cref="Merge"/> is the rug-pull guard: every listing updates
/// what the server says, and a tool whose descriptor hash differs from the one he approved is «بدها موافقة جديدة» —
/// never announced until he approves it again (<see cref="Enable"/>). At most <see cref="MaxEnabled"/> tools on.
/// </summary>
public static class McpCatalog
{
    public const int MaxEnabled = 12, DescriptionMax = 300;
    public const string Ok = "ok", New = "new", Changed = "changed", Unfit = "unfit";

    public static McpToolsFile Empty { get; } = new([], new MediaConfig(false, false, ToolGrant.Ask, ToolGrant.Ask));

    public static string StateOf(McpTool t, string slug)
    {
        if (McpSchema.Qualified(slug, t.Name) is null || Args(t) is null) return Unfit;
        return t.Approved is null ? New : t.Approved == t.Hash ? Ok : Changed;
    }

    public static IReadOnlyList<ArgSpec>? Args(McpTool t)
    {
        try { using var d = JsonDocument.Parse(t.Schema); return McpSchema.ToArgs(d.RootElement); }
        catch (JsonException) { return null; }
    }

    /// <summary>The server's latest listing over what she kept: same names keep his choices; new names come in off; gone names go.</summary>
    public static McpServer Merge(McpServer server, IReadOnlyList<ListedTool> listed)
    {
        var old = server.Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var tools = new List<McpTool>();
        foreach (var l in listed.GroupBy(l => l.Name, StringComparer.Ordinal).Select(g => g.First()))
        {
            var hash = McpSchema.Hash(l.Name, l.Description, l.InputSchema, l.Annotations);
            var desc = l.Description ?? "";
            var schema = l.InputSchema.ValueKind == JsonValueKind.Undefined ? "{}" : l.InputSchema.GetRawText();
            tools.Add(old.TryGetValue(l.Name, out var t)
                ? t with { Description = desc, Schema = schema, Hash = hash }
                : new McpTool(l.Name, desc, schema, hash, null, false, ToolGrant.Ask, false, null));
        }
        return server with { Tools = tools };
    }

    /// <summary>On = his approval of what he sees now (the hash is pinned). Null when it cannot be on (unfit, unknown, the 13th).</summary>
    public static McpToolsFile? Enable(McpToolsFile f, string serverId, string tool, bool on, DateTimeOffset now)
    {
        if (Find(f, serverId, tool) is not var (s, t)) return null;
        if (!on) return Replace(f, s, t with { Enabled = false });
        if (StateOf(t, s.Slug) == Unfit) return null;
        int others = f.Servers.Sum(x => x.Tools.Count(y => y.Enabled && !(x.Id == s.Id && y.Name == t.Name)));
        if (others >= MaxEnabled) return null;
        return Replace(f, s, t with { Enabled = true, Approved = t.Hash, ApprovedAt = now });
    }

    public static McpToolsFile? SetGrant(McpToolsFile f, string serverId, string tool, ToolGrant grant) =>
        Find(f, serverId, tool) is var (s, t) ? Replace(f, s, t with { Grant = grant }) : null;

    public static McpToolsFile? SetSensitive(McpToolsFile f, string serverId, string tool, bool on) =>
        Find(f, serverId, tool) is var (s, t) ? Replace(f, s, t with { HisSensitive = on }) : null;

    private static (McpServer, McpTool)? Find(McpToolsFile f, string serverId, string tool) =>
        f.Servers.FirstOrDefault(s => s.Id == serverId) is { } s && s.Tools.FirstOrDefault(t => t.Name == tool) is { } t ? (s, t) : null;

    private static McpToolsFile Replace(McpToolsFile f, McpServer s, McpTool t) =>
        f with { Servers = f.Servers.Select(x => x.Id != s.Id ? x : x with { Tools = x.Tools.Select(y => y.Name == t.Name ? t : y).ToList() }).ToList() };

    public static bool Sensitive(McpTool t) => t.HisSensitive || ToolSafety.IsSensitive(t.Name, t.Description);

    /// <summary>What the decision loop may propose: enabled, approved, fitting tools of unpaused servers, and the media tools
    /// once checked (voice only with a Groq key).</summary>
    public static IReadOnlyList<ToolSpec> Announced(McpToolsFile f, bool groqKey)
    {
        var list = new List<ToolSpec>();
        foreach (var s in f.Servers.Where(s => !s.Paused))
            foreach (var t in s.Tools.Where(t => t.Enabled && StateOf(t, s.Slug) == Ok))
            {
                bool sensitive = Sensitive(t);
                var grant = ToolGrants.Effective(t.Grant, sensitive);
                var desc = ToolOutput.Sanitize(t.Description).Replace('\n', ' ').Trim();
                if (desc.Length > DescriptionMax) desc = desc[..(DescriptionMax - 1)] + "…";
                list.Add(new ToolSpec(McpSchema.Qualified(s.Slug, t.Name)!, desc.Length > 0 ? desc : t.Name, Args(t)!,
                    grant == ToolGrant.Read ? ActionClass.Read : ActionClass.B, ReadOnly: grant == ToolGrant.Read,
                    Source: ToolSource.Mcp, ServerId: s.Id, DescriptorHash: t.Hash, Sensitive: sensitive, Grant: grant));
            }
        if (f.Media.ImageChecked) list.Add(MediaTools.Image with { Grant = f.Media.ImageGrant == ToolGrant.Always ? ToolGrant.Always : ToolGrant.Ask });
        if (f.Media.VoiceChecked && groqKey) list.Add(MediaTools.Voice with { Grant = f.Media.VoiceGrant == ToolGrant.Always ? ToolGrant.Always : ToolGrant.Ask });
        return list;
    }

    /// <summary>The page's and the engine's view (never a secret): servers with every tool's full description and state.</summary>
    public static ToolsInfo Info(McpToolsFile f, bool groqKey, IReadOnlyDictionary<string, string> serverStates, string? imageTest, string? voiceTest,
        string? result = null, string? resultServer = null)
    {
        var servers = f.Servers.Select(s => new ToolsServerInfo(s.Id, s.Name, s.Slug, s.Kind, s.Kind == "url" ? s.Url ?? "" : s.Exe ?? "", s.Args,
            s.Paused ? "paused" : serverStates.GetValueOrDefault(s.Id, "unknown"), s.SecretKind is not null,
            s.Tools.Select(t => new ToolsToolInfo(t.Name, McpSchema.Qualified(s.Slug, t.Name) ?? "", t.Description, Args(t), t.Enabled, t.Grant,
                Sensitive(t), ToolSafety.IsSensitive(t.Name, t.Description), StateOf(t, s.Slug))).ToList())).ToList();
        var m = f.Media;
        return new ToolsInfo(servers, Announced(f, groqKey), new ToolsMediaInfo(m.ImageChecked, m.VoiceChecked, groqKey, m.ImageGrant, m.VoiceGrant, imageTest, voiceTest),
            result, resultServer);
    }

    public static string Serialize(McpToolsFile f) => JsonSerializer.Serialize(f, ToolsJson.Default.McpToolsFile);

    public static McpToolsFile Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try { return JsonSerializer.Deserialize(json, ToolsJson.Default.McpToolsFile) is { Servers: not null, Media: not null } f ? f : Empty; }
        catch (JsonException) { return Empty; }
    }
}
