namespace Sahar.Core.Settings;

/// <summary>What «امسح كل شي عني» did: how many entries went, and the names of those still there (in use).</summary>
public sealed record EraseReport(int Removed, IReadOnlyList<string> Left);

/// <summary>
/// «امسح كل شي عني» (plan 0.10c Ruling 9): deletes everything inside her data folder (settings, notes, clipboard, life),
/// not the folder itself and not the entries asked to be kept. Something in use stays, is named in the report, and a
/// pending marker finishes the job at her next start (review fix 2026-11-13). Never throws.
/// </summary>
public static class DataEraser
{
    public const string PendingMarker = "erase-pending";

    public static EraseReport Erase(string folder, IEnumerable<string> keep, Action<string>? delete = null)
    {
        var kept = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase) { PendingMarker };
        delete ??= DeleteEntry;
        List<string> entries;
        try
        {
            if (!Directory.Exists(folder)) return new EraseReport(0, []);
            entries = Directory.EnumerateFileSystemEntries(folder).ToList();
        }
        catch (Exception)
        {
            return new EraseReport(0, []);
        }
        int removed = 0;
        var left = new List<string>();
        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (kept.Contains(name)) continue;
            try
            {
                delete(entry);
                removed++;
            }
            catch (Exception)
            {
                left.Add(name);   // in use: the next start finishes it
            }
        }
        var marker = Path.Combine(folder, PendingMarker);
        try
        {
            if (left.Count > 0) File.WriteAllText(marker, string.Join('\n', left));
            else if (File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception) { /* the marker is a courtesy */ }
        return new EraseReport(removed, left);
    }

    /// <summary>Her start: an erase that could not finish last time finishes now. Null when none was pending.</summary>
    public static EraseReport? ResumeIfPending(string folder, IEnumerable<string> keep, Action<string>? delete = null)
    {
        try
        {
            if (!File.Exists(Path.Combine(folder, PendingMarker))) return null;
        }
        catch (Exception)
        {
            return null;
        }
        return Erase(folder, keep, delete);
    }

    /// <summary>The number of entries removed (kept for older callers).</summary>
    public static int EraseAll(string folder, params string[] keep) => Erase(folder, keep).Removed;

    private static void DeleteEntry(string entry)
    {
        if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
        else File.Delete(entry);
    }
}
