using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Tools;

/// <summary>
/// 0.19 spec §5: an MCP tool as Sahar can announce it. Its <c>inputSchema</c> becomes the existing <see cref="ArgSpec"/>
/// subset (integer with min/max, string with maxLength or a string enum, boolean) or the tool is not announced at all;
/// its name becomes <c>mcp.&lt;slug&gt;.&lt;tool&gt;</c> (the slug unique per server); its descriptor hash pins what he
/// approved (rug-pull guard). Pure.
/// </summary>
public static partial class McpSchema
{
    public const int MaxArgs = 8, StringCap = 500, SlugMax = 20;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")] private static partial Regex ArgName();
    [GeneratedRegex("^[A-Za-z0-9_-]{1,48}$")] private static partial Regex ToolName();

    /// <summary>The tool's arguments, or null when its schema does not fit (number, array, object, $ref, oneOf…).</summary>
    public static IReadOnlyList<ArgSpec>? ToArgs(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object")
            return null;
        foreach (var bad in (string[])["$ref", "oneOf", "anyOf", "allOf", "not"])
            if (schema.TryGetProperty(bad, out _)) return null;
        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var req))
        {
            if (req.ValueKind != JsonValueKind.Array) return null;
            foreach (var r in req.EnumerateArray()) if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString()!);
        }
        var args = new List<ArgSpec>();
        if (!schema.TryGetProperty("properties", out var props)) return args;
        if (props.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in props.EnumerateObject())
        {
            if (args.Count == MaxArgs || !ArgName().IsMatch(p.Name) || Arg(p.Name, p.Value, required.Contains(p.Name)) is not { } a) return null;
            args.Add(a);
        }
        return args;
    }

    private static ArgSpec? Arg(string name, JsonElement s, bool required)
    {
        if (s.ValueKind != JsonValueKind.Object || !s.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) return null;
        foreach (var bad in (string[])["$ref", "oneOf", "anyOf", "allOf", "not"])
            if (s.TryGetProperty(bad, out _)) return null;
        switch (t.GetString())
        {
            case "integer":
                if (s.TryGetProperty("enum", out _)) return null;
                int? min = Int(s, "minimum"), max = Int(s, "maximum");
                if ((s.TryGetProperty("minimum", out _) && min is null) || (s.TryGetProperty("maximum", out _) && max is null)) return null;
                return new ArgSpec(name, ArgType.Int, required, min, max);
            case "boolean":
                return new ArgSpec(name, ArgType.Bool, required);
            case "string":
                if (s.TryGetProperty("enum", out var e))
                {
                    if (e.ValueKind != JsonValueKind.Array) return null;
                    var values = new List<string>();
                    foreach (var v in e.EnumerateArray())
                    {
                        if (v.ValueKind != JsonValueKind.String) return null;
                        values.Add(v.GetString()!);
                    }
                    return values.Count == 0 ? null : new ArgSpec(name, ArgType.Enum, required, Values: values);
                }
                int cap = Int(s, "maxLength") is { } m && m > 0 ? Math.Min(m, StringCap) : StringCap;
                return new ArgSpec(name, ArgType.String, required, MaxLength: cap);
            default:
                return null;
        }
    }

    private static int? Int(JsonElement s, string key) =>
        s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;

    /// <summary>A slug from his server name: lowercase ASCII letters/digits and <c>_</c>, at most 20, unique among <paramref name="taken"/>.</summary>
    public static string Slug(string name, IReadOnlyCollection<string> taken)
    {
        var b = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') b.Append(c);
            else if (b.Length > 0 && b[^1] != '_') b.Append('_');
        }
        var slug = b.ToString().Trim('_');
        if (slug.Length > SlugMax) slug = slug[..SlugMax].TrimEnd('_');
        if (slug.Length == 0) slug = "server";
        if (!taken.Contains(slug)) return slug;
        for (int i = 2; ; i++)
        {
            var suffix = "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var c = (slug.Length + suffix.Length > SlugMax ? slug[..(SlugMax - suffix.Length)] : slug) + suffix;
            if (!taken.Contains(c)) return c;
        }
    }

    /// <summary><c>mcp.&lt;slug&gt;.&lt;tool&gt;</c>, or null for a tool name Sahar does not announce (dots, spaces, too long).</summary>
    public static string? Qualified(string slug, string tool) => ToolName().IsMatch(tool) ? $"mcp.{slug}.{tool}" : null;

    /// <summary>SHA-256 (lower hex) of the canonical JSON of name, description, inputSchema and annotations.</summary>
    public static string Hash(string name, string? description, JsonElement inputSchema, JsonElement? annotations)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WritePropertyName("annotations");
            if (annotations is { } a) Canonical(w, a); else w.WriteNullValue();
            w.WritePropertyName("description");
            if (description is null) w.WriteNullValue(); else w.WriteStringValue(description);
            w.WritePropertyName("inputSchema");
            Canonical(w, inputSchema);
            w.WriteString("name", name);
            w.WriteEndObject();
        }
        return Convert.ToHexStringLower(SHA256.HashData(ms.ToArray()));
    }

    private static void Canonical(Utf8JsonWriter w, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    w.WritePropertyName(p.Name);
                    Canonical(w, p.Value);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var x in e.EnumerateArray()) Canonical(w, x);
                w.WriteEndArray();
                break;
            case JsonValueKind.Undefined:
                w.WriteNullValue();
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }
}
