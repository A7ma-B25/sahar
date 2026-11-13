namespace Sahar.Core.Settings;

/// <summary>«امسح كل شي عني» (plan 0.10c Ruling 9): deletes everything inside her data folder (settings, notes,
/// clipboard, life, and since 0.14 the Mind's <c>mind\</c>), not the folder itself. Files in use stay; never throws.</summary>
public static class DataEraser
{
    /// <summary>The Mind's personal folder inside her data folder (spec 0.14 §7.8).</summary>
    public const string MindFolder = "mind";

    /// <summary>The number of files and folders found directly inside <paramref name="folder"/> and removed; entries
    /// named in <paramref name="keep"/> stay.</summary>
    public static int EraseAll(string folder, params string[] keep) => EraseAll(folder, null, keep);

    /// <summary>As above; <c>mind\</c> is crypto-shredded first (its key, then its files), each step named to
    /// <paramref name="deleting"/> (tests).</summary>
    public static int EraseAll(string folder, Action<string>? deleting, params string[] keep)
    {
        List<string> entries;
        try
        {
            if (!Directory.Exists(folder)) return 0;
            entries = Directory.EnumerateFileSystemEntries(folder).ToList();
        }
        catch (Exception)
        {
            return 0;
        }
        int removed = 0;
        foreach (var entry in entries)
        {
            if (keep.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                if (Directory.Exists(entry) && string.Equals(Path.GetFileName(entry), MindFolder, StringComparison.OrdinalIgnoreCase))
                {
                    // The key goes first: whatever stays behind (a file in use) can never be opened again.
                    Sahar.Core.Mind.Memory.SecretBox.Shred(entry, deleting);
                    if (!Directory.Exists(entry)) { removed++; continue; }
                }
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
                removed++;
            }
            catch (Exception)
            {
                // A file in use stays; the rest goes.
            }
        }
        return removed;
    }

    /// <summary>The activities' local data (0.13): screen time (usage), the shelf's saved images and list, the lyrics
    /// cache; in <c>%LOCALAPPDATA%\Sahar</c>, next to the log, which stays. Returns how many entries went.</summary>
    public static int EraseActivityData(string localFolder)
    {
        int removed = 0;
        foreach (var name in ActivityEntries)
        {
            var path = Path.Combine(localFolder, name);
            try
            {
                if (Directory.Exists(path)) { Directory.Delete(path, recursive: true); removed++; }
                else if (File.Exists(path)) { File.Delete(path); removed++; }
            }
            catch (Exception)
            {
                // In use: it stays.
            }
        }
        return removed;
    }

    public static IReadOnlyList<string> ActivityEntries { get; } = ["usage", "Shelf", "lyrics", "shelf.json"];
}
