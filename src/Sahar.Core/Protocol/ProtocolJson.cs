using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Protocol;

/// <summary>JSON-lines encoding of pipe messages (AOT-safe, source generated).</summary>
public static class ProtocolJson
{
    public static PipeRequest ParseRequest(string line)
    {
        PipeRequest? r;
        try { r = JsonSerializer.Deserialize(line, ProtocolContext.Default.PipeRequest); }
        catch (JsonException e) { throw new FormatException($"bad request: {e.Message}", e); }
        if (r is null || string.IsNullOrEmpty(r.Name) || r.Type is not ("event" or "cmd"))
            throw new FormatException("request needs type \"event\" or \"cmd\" and a name");
        return r;
    }

    public static PipeReply ParseReply(string line)
    {
        try { return JsonSerializer.Deserialize(line, ProtocolContext.Default.PipeReply) ?? throw new FormatException("empty reply"); }
        catch (JsonException e) { throw new FormatException($"bad reply: {e.Message}", e); }
    }

    public static string Write(PipeReply reply) => JsonSerializer.Serialize(reply, ProtocolContext.Default.PipeReply);

    public static string Write(PipeRequest request) => JsonSerializer.Serialize(request, ProtocolContext.Default.PipeRequest);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PipeRequest))]
[JsonSerializable(typeof(PipeReply))]
internal sealed partial class ProtocolContext : JsonSerializerContext;
