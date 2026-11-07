namespace Sahar.Core.Settings;

/// <summary>
/// The clean version (0.7): its first run removes everything older test builds left (settings, notes, clipboard,
/// life, log), then marks the data folder so later runs keep what this version saves. Never throws.
/// </summary>
public static class CleanStart
{
    /// <summary>Raise this only when old data must be wiped again.</summary>
    public const int DataVersion = 2;
    private const string Marker = "data-version";

    public static string RoamingFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar");

    /// <summary>True when old data was found and removed.</summary>
    public static bool Run(string roaming, string local)
    {
        var marker = Path.Combine(roaming, Marker);
        try
        {
            if (File.Exists(marker) && int.TryParse(File.ReadAllText(marker).Trim(), out int v) && v >= DataVersion) return false;
        }
        catch
        {
            return false;   // unreadable marker: never wipe on a guess
        }
        bool wiped = Empty(roaming) | Empty(local);
        try
        {
            Directory.CreateDirectory(roaming);
            File.WriteAllText(marker, DataVersion.ToString());
        }
        catch
        {
            // Next start tries again.
        }
        return wiped;
    }

    private static bool Empty(string folder)
    {
        if (!Directory.Exists(folder)) return false;
        bool any = false;
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder).ToList())
        {
            any = true;
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch
            {
                // A file in use stays; the rest goes.
            }
        }
        return any;
    }
}
