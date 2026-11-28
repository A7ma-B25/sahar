using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Sahar.Core.Mind.Tools;

namespace Sahar.Mind.Tools;

/// <summary>What one <c>tools/call</c> returned: the text blocks joined (not yet sanitized) and the images (shown to
/// him, never fed to the model). Other content (audio, resources, links) is dropped.</summary>
internal sealed record McpCallOutput(bool IsError, string Text, IReadOnlyList<(byte[] Data, string Mime)> Images);

/// <summary>
/// 0.19 spec §4: the thin wrapper over the official C# SDK's client (<c>ModelContextProtocol.Core</c> 2.4.0). It speaks
/// 2026-07-28 first (<c>server/discover</c>) and falls back to the 2025-11-25 handshake by itself. Stdio servers are
/// started by <see cref="JobProcess"/> (never cmd.exe), so this side only sees their two streams; HTTP servers go
/// through the given handler (no redirects, the system proxy). Nothing here logs.
/// </summary>
internal sealed class McpLink : IAsyncDisposable
{
    private readonly McpClient _client;
    private McpLink(McpClient client) => _client = client;

    private static McpClientOptions Options() => new()
    {
        ClientInfo = new Implementation { Name = "sahar", Version = Sahar.Core.CoreInfo.Version },
        InitializationTimeout = TimeSpan.FromSeconds(15),
    };

    public static async Task<McpLink> HttpAsync(Uri endpoint, IReadOnlyDictionary<string, string> headers, HttpMessageHandler handler, CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp, Name = "mcp",
            AdditionalHeaders = headers.ToDictionary(h => h.Key, h => h.Value), ConnectionTimeout = TimeSpan.FromSeconds(15),
        };
        var http = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        var transport = new HttpClientTransport(options, http, null, ownsHttpClient: true);
        return new McpLink(await McpClient.CreateAsync(transport, Options(), null, ct));
    }

    public static async Task<McpLink> StreamAsync(Stream toServer, Stream fromServer, CancellationToken ct)
    {
        var transport = new StreamClientTransport(toServer, fromServer, null);
        return new McpLink(await McpClient.CreateAsync(transport, Options(), null, ct));
    }

    public async Task<IReadOnlyList<ListedTool>> ListAsync(CancellationToken ct)
    {
        var tools = await _client.ListToolsAsync((ModelContextProtocol.RequestOptions?)null, ct);
        return tools.Select(t => t.ProtocolTool).Select(p => new ListedTool(p.Name, p.Description, p.InputSchema.Clone(),
            p.Annotations is { } a ? JsonSerializer.SerializeToElement(a, ModelContextProtocol.McpJsonUtilities.DefaultOptions.GetTypeInfo(a.GetType())) : null)).ToList();
    }

    public async Task<McpCallOutput> CallAsync(string tool, JsonElement args, CancellationToken ct)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (args.ValueKind == JsonValueKind.Object)
            foreach (var p in args.EnumerateObject())
                if (p.Value.ValueKind != JsonValueKind.Null) arguments[p.Name] = p.Value.Clone();
        var r = await _client.CallToolAsync(new CallToolRequestParams { Name = tool, Arguments = arguments }, ct);
        var text = new StringBuilder();
        var images = new List<(byte[], string)>();
        foreach (var block in r.Content)
        {
            if (block is TextContentBlock t) { if (text.Length > 0) text.Append('\n'); text.Append(t.Text); }
            else if (block is ImageContentBlock i && i.MimeType is "image/png" or "image/jpeg" or "image/webp") images.Add((i.DecodedData.ToArray(), i.MimeType));
        }
        return new McpCallOutput(r.IsError == true, text.ToString(), images);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
