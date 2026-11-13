using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Sahar.Core.Mind.Tools;

/// <summary>
/// The announced tools (spec §3.4–3.5): the same <see cref="ArgSpec"/>s build the model's grammar and re-validate
/// every call in Core before anything runs. A call must be a JSON object with exactly the declared arguments (JSON
/// null = left out), each of the right type and range. Rules the schema cannot say are checked here too:
/// <c>reminder.set</c> takes exactly one of <c>inMinutes</c> / <c>at</c>; <c>settings.set</c>'s value is one scalar;
/// <c>goal.add</c>'s <c>until</c> is a yyyy-MM-dd date. Pure; never logs.
/// </summary>
public sealed class ToolRegistry(IReadOnlyList<ToolSpec> announced) : IToolRegistry
{
    public const string Invalid = "invalid", UnknownTool = "unknown-tool", NotAnnounced = "not-announced";
    public const string TimePattern = "^([01][0-9]|2[0-3]):[0-5][0-9]$";
    public static IReadOnlyList<string> Moods { get; } = ["calm", "happy", "focus", "excited", "sleepy", "mystic"];
    public static IReadOnlyList<string> Sensitivities { get; } = ["none", "health", "religion", "politics", "sexuality", "money", "family", "other"];

    private readonly Dictionary<string, ToolSpec> _byName = announced.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public IReadOnlyList<ToolSpec> Announced => announced;

    public ToolSpec? Spec(string name) => _byName.GetValueOrDefault(name);

    public ToolCheck Validate(ToolCall call)
    {
        if (!_byName.TryGetValue(call.Tool, out var spec))
            return new(false, ToolCatalog.Get(call.Tool) is null ? UnknownTool : NotAnnounced);
        var a = call.Args;
        if (a.ValueKind != JsonValueKind.Object) return Bad(null);
        var names = spec.Args.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var p in a.EnumerateObject())
            if (!names.Contains(p.Name)) return Bad(p.Name);
        foreach (var arg in spec.Args)
        {
            bool present = a.TryGetProperty(arg.Name, out var v) && v.ValueKind != JsonValueKind.Null;
            if (!present)
            {
                if (arg.Required) return Bad(arg.Name);
                continue;
            }
            if (spec.Name == "settings.set" && arg.Name == "value")
            {
                if (v.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)) return Bad("value");
                continue;
            }
            if (!Fits(arg, v)) return Bad(arg.Name);
        }
        return spec.Name switch
        {
            "reminder.set" => Has(a, "inMinutes") == Has(a, "at") ? Bad("at") : ToolCheck.Valid,
            "goal.add" when Has(a, "until") && !DateOnly.TryParseExact(a.GetProperty("until").GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => Bad("until"),
            _ => ToolCheck.Valid,
        };
    }

    public ActionClass ClassOf(ToolCall call)
    {
        if (!_byName.TryGetValue(call.Tool, out var spec)) return ActionClass.C;
        if (spec.Name != "settings.set") return spec.Class;
        var key = call.Args.ValueKind == JsonValueKind.Object && call.Args.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String
            ? k.GetString() : null;
        return key is not null && ToolCatalog.SettingsA.Contains(key) ? ActionClass.A
            : key is not null && ToolCatalog.SettingsB.Contains(key) ? ActionClass.B
            : ActionClass.C;
    }

    /// <summary>"HH:mm", 00:00–23:59 (the grammar's <see cref="TimePattern"/>).</summary>
    public static bool IsTime(string t) =>
        t.Length == 5 && t[2] == ':' && char.IsAsciiDigit(t[0]) && char.IsAsciiDigit(t[1]) && char.IsAsciiDigit(t[3]) && char.IsAsciiDigit(t[4])
        && (t[0] - '0') * 10 + (t[1] - '0') < 24 && t[3] < '6';

    private static ToolCheck Bad(string? field) => new(false, Invalid, field);

    private static bool Has(JsonElement a, string name) => a.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null;

    private static bool Fits(ArgSpec arg, JsonElement v)
    {
        switch (arg.Type)
        {
            case ArgType.Int:
                if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out int n)) return false;
                return (arg.Min is not { } min || n >= min) && (arg.Max is not { } max || n <= max);
            case ArgType.Bool:
                return v.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case ArgType.Enum:
                return v.ValueKind == JsonValueKind.String && arg.Values is { } values && values.Contains(v.GetString()!);
            case ArgType.Time:
                return v.ValueKind == JsonValueKind.String && IsTime(v.GetString()!);
            default:
                if (v.ValueKind != JsonValueKind.String) return false;
                var s = v.GetString()!;
                if (arg.Required && s.Trim().Length == 0) return false;
                return arg.MaxLength is not { } m || s.Length <= m;
        }
    }

    // ---- The decision schema (spec §3.5) ----

    /// <summary>The grammar: actions first (≤ 3, each one of the announced tools with its own args), then next, say,
    /// mood and remember. Every property is required so the order is fixed: she says her words only after deciding
    /// what she does, and the Mind streams them knowing whether an action is pending (truthful confirmations).</summary>
    public string DecisionSchema()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("type", "object");
            w.WriteStartObject("properties");

            w.WriteStartObject("actions");
            w.WriteString("type", "array");
            w.WriteNumber("maxItems", announced.Count == 0 ? 0 : 3);
            w.WriteStartObject("items");
            w.WriteStartArray("anyOf");
            foreach (var t in announced) Action(w, t);
            if (announced.Count == 0) { w.WriteStartObject(); w.WriteString("type", "object"); w.WriteEndObject(); }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("next");
            Enum(w, ["done", "need-results"]);
            w.WriteEndObject();

            w.WriteStartObject("say");
            w.WriteString("type", "string");
            w.WriteNumber("maxLength", 220);
            w.WriteEndObject();

            w.WriteStartObject("mood");
            w.WriteStartArray("enum");
            foreach (var m in Moods) w.WriteStringValue(m);
            w.WriteNullValue();
            w.WriteEndArray();
            w.WriteEndObject();

            w.WriteStartObject("remember");
            w.WriteString("type", "array");
            w.WriteNumber("maxItems", 3);
            w.WriteStartObject("items");
            w.WriteString("type", "object");
            w.WriteStartObject("properties");
            w.WriteStartObject("text"); w.WriteString("type", "string"); w.WriteNumber("minLength", 1); w.WriteNumber("maxLength", 200); w.WriteEndObject();
            w.WriteStartObject("kind"); Enum(w, ["fact", "preference", "goal", "event"]); w.WriteEndObject();
            w.WriteStartObject("sensitive"); Enum(w, Sensitivities); w.WriteEndObject();
            w.WriteEndObject();
            Required(w, ["text", "kind", "sensitive"]);
            w.WriteBoolean("additionalProperties", false);
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteEndObject();
            Required(w, ["actions", "next", "say", "mood", "remember"]);
            w.WriteBoolean("additionalProperties", false);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void Action(Utf8JsonWriter w, ToolSpec t)
    {
        w.WriteStartObject();
        w.WriteString("type", "object");
        w.WriteStartObject("properties");
        w.WriteStartObject("tool"); w.WriteString("const", t.Name); w.WriteEndObject();
        w.WriteStartObject("args");
        w.WriteString("type", "object");
        w.WriteStartObject("properties");
        foreach (var a in t.Args)
        {
            w.WriteStartObject(a.Name);
            if (t.Name == "settings.set" && a.Name == "value")
            {
                w.WriteStartArray("anyOf");
                w.WriteStartObject(); w.WriteString("type", "string"); w.WriteNumber("maxLength", 40); w.WriteEndObject();
                w.WriteStartObject(); w.WriteString("type", "integer"); w.WriteEndObject();
                w.WriteStartObject(); w.WriteString("type", "boolean"); w.WriteEndObject();
                w.WriteEndArray();
            }
            else Arg(w, a);
            w.WriteEndObject();
        }
        w.WriteEndObject();
        Required(w, t.Args.Where(a => a.Required).Select(a => a.Name));
        w.WriteBoolean("additionalProperties", false);
        w.WriteEndObject();
        w.WriteStartObject("why"); w.WriteString("type", "string"); w.WriteNumber("maxLength", 80); w.WriteEndObject();
        w.WriteEndObject();
        Required(w, ["tool", "args", "why"]);
        w.WriteBoolean("additionalProperties", false);
        w.WriteEndObject();
    }

    private static void Arg(Utf8JsonWriter w, ArgSpec a)
    {
        switch (a.Type)
        {
            case ArgType.Int:
                w.WriteString("type", "integer");
                if (a.Min is { } min) w.WriteNumber("minimum", min);
                if (a.Max is { } max) w.WriteNumber("maximum", max);
                break;
            case ArgType.Bool:
                w.WriteString("type", "boolean");
                break;
            case ArgType.Enum:
                Enum(w, a.Values ?? []);
                break;
            case ArgType.Time:
                w.WriteString("type", "string");
                w.WriteString("pattern", TimePattern);
                break;
            default:
                w.WriteString("type", "string");
                if (a.Required) w.WriteNumber("minLength", 1);
                if (a.MaxLength is { } m) w.WriteNumber("maxLength", m);
                break;
        }
    }

    private static void Enum(Utf8JsonWriter w, IEnumerable<string> values)
    {
        w.WriteString("type", "string");
        w.WriteStartArray("enum");
        foreach (var v in values) w.WriteStringValue(v);
        w.WriteEndArray();
    }

    private static void Required(Utf8JsonWriter w, IEnumerable<string> names)
    {
        w.WriteStartArray("required");
        foreach (var n in names) w.WriteStringValue(n);
        w.WriteEndArray();
    }
}
