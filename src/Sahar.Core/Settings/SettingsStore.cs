using System.Text.Json;
using Sahar.Core.Json;

namespace Sahar.Core.Settings;

/// <summary>Loads and saves settings. Never throws on a bad file: it keeps a ".bad" copy and returns defaults.</summary>
public static class SettingsStore
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar", "settings.json");

    public static SaharSettings Load(string path)
    {
        if (!File.Exists(path)) return new SaharSettings();
        string text = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize(text, SettingsJson.Default.SaharSettings) ?? new SaharSettings();
        }
        catch (JsonException)
        {
            File.WriteAllText(path + ".bad", text);
            return new SaharSettings();
        }
    }

    /// <summary>Writes "path.tmp" then swaps it in, so a crash never leaves a half-written file.</summary>
    public static void Save(string path, SaharSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJson.Default.SaharSettings));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}
