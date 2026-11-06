using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sahar.Core.Art;
using Sahar.Core.Expressions;

namespace Sahar.Core.Json;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(Dictionary<string, MoodColorsDto>))]
[JsonSerializable(typeof(Rig))]
[JsonSerializable(typeof(List<ExpressionDto>))]
internal sealed partial class CoreJson : JsonSerializerContext;

/// <summary>Reads JSON assets embedded in Sahar.Core (logical name "Sahar.(logical name "Sahar.&lt;path with dots&gt;")lt;file name(logical name "Sahar.&lt;path with dots&gt;")gt;"; file names are unique).</summary>
internal static class Assets
{
    public static T Read<T>(string name, JsonTypeInfo<T> type)
    {
        using var s = typeof(Assets).Assembly.GetManifestResourceStream("Sahar." + name)
            ?? throw new InvalidOperationException($"missing embedded asset '{name}'");
        return JsonSerializer.Deserialize(s, type) ?? throw new InvalidOperationException($"empty asset '{name}'");
    }
}
