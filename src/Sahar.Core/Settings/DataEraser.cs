namespace Sahar.Core.Settings;

/// <summary>What «امسح كل شي عني» did: how many entries went, and the names of those still there (in use).</summary>
public sealed record EraseReport(int Removed, IReadOnlyList<string> Left);

/// <summary>
/// «امسح كل شي عني» (plan 0.10c Ruling 9): deletes everything inside her data folder (settings, notes, clipboard, life,
/// and since 0.14 the Mind's <c>mind\</c>, crypto-shredded key first),
/// not the folder itself and not the entries asked to be kept. Something in use stays, is named in the report, and a
/// pending marker finishes the job at her next start (review fix 2026-11-13). Never throws.
/// </summary>
public static class DataEraser
{
    public const string PendingMarker = "erase-pending";

    /// <summary>The Mind's personal folder inside her data folder (spec 0.14 §7.8).</summary>
    public const string MindFolder = "mind";

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

    /// <summary>As above; each step of the Mind folder's shredding is named to <paramref name="deleting"/> (tests).</summary>
    public static int EraseAll(string folder, Action<string>? deleting, params string[] keep) => Erase(folder, keep, e => DeleteEntry(e, deleting)).Removed;

    private static void DeleteEntry(string entry) => DeleteEntry(entry, null);

    private static void DeleteEntry(string entry, Action<string>? deleting)
    {
        if (Directory.Exists(entry) && string.Equals(Path.GetFileName(entry), MindFolder, StringComparison.OrdinalIgnoreCase))
        {
            // The key goes first: whatever stays behind (a file in use) can never be opened again.
            Sahar.Core.Mind.Memory.SecretBox.Shred(entry, deleting);
            if (!Directory.Exists(entry)) return;
        }
        if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
        else File.Delete(entry);
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
