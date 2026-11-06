using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Life;

/// <summary>What she has learned: action weights and the 24-hour profile. Nothing about your apps (spec §14).</summary>
public sealed record LifeState(int Version, Dictionary<string, float> Weights, float[] Hours)
{
    public static LifeState Empty => new(1, [], new float[24]);
}

/// <summary>Loads and saves life.json. Never throws on a bad or locked file: defaults (and a ".bad" copy of bad JSON).</summary>
public static class LifeStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar", "life.json");

    public static LifeState Load(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return LifeState.Empty;
            text = File.ReadAllText(path);
        }
        catch (IOException) { return LifeState.Empty; }
        catch (UnauthorizedAccessException) { return LifeState.Empty; }

        try
        {
            var s = JsonSerializer.Deserialize(text, LifeJson.Default.LifeState);
            if (s is null) return LifeState.Empty;
            var hours = s.Hours is { Length: 24 } ? s.Hours : new float[24];
            return new LifeState(1, s.Weights ?? [], hours);
        }
        catch (JsonException)
        {
            try { File.WriteAllText(path + ".bad", text); } catch (IOException) { }
            return LifeState.Empty;
        }
    }

    /// <summary>Writes "path.tmp" then swaps it in.</summary>
    public static void Save(string path, LifeState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, LifeJson.Default.LifeState));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(LifeState))]
internal sealed partial class LifeJson : JsonSerializerContext;
