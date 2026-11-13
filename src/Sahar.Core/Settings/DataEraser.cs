namespace Sahar.Core.Settings;

/// <summary>«امسح كل شي عني» (plan 0.10c Ruling 9): deletes everything inside her data folder (settings, notes,
/// clipboard, life), not the folder itself. Files in use stay; never throws.</summary>
public static class DataEraser
{
    /// <summary>The number of files and folders found directly inside <paramref name="folder"/> and removed; entries
    /// named in <paramref name="keep"/> stay.</summary>
    public static int EraseAll(string folder, params string[] keep)
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
}
