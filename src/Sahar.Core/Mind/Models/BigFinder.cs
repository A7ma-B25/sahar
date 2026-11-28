using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Models;

/// <summary>0.18 (owner: "he only downloads the file"): a pinned model he downloaded with his browser, found by its
/// exact size and its name (the browser's «(1)» copies count; unfinished .crdownload/.part files never do). The SHA-256
/// check comes after, in the Mind. Pure apart from listing the folders.</summary>
public static class BigFinder
{
    public sealed record Hit(ModelEntry Pin, string Path);

    /// <summary>The recommended pin this file is, or null.</summary>
    public static ModelEntry? Match(string fileName, long size, IEnumerable<ModelEntry>? pins = null)
    {
        if (!fileName.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var pin in pins ?? ModelCatalog.Recommended)
        {
            if (pin.File is not { } file || pin.Bytes != size) continue;
            var stem = System.IO.Path.GetFileNameWithoutExtension(file);
            if (fileName.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) return pin;
        }
        return null;
    }

    /// <summary>Top-level .gguf files in these folders that are one of <paramref name="pins"/> (missing or unreadable
    /// folders are skipped).</summary>
    public static IReadOnlyList<Hit> Scan(IEnumerable<string> folders, IEnumerable<ModelEntry> pins)
    {
        var list = pins.ToList();
        var hits = new List<Hit>();
        foreach (var dir in folders)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.gguf", SearchOption.TopDirectoryOnly))
                    if (Match(f.Name, f.Length, list) is { } pin) hits.Add(new Hit(pin, f.FullName));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return hits;
    }
}
