using System.Text.Json;

namespace Sahar.Core.Mind.Tools;

/// <summary>0.19 spec §6: <c>tools-ledger.json</c> beside her box — when each server and tool was added, approved, changed
/// or paused, with its hash. Never arguments or output. The last <see cref="Keep"/> entries.</summary>
public sealed class ToolsLedger(string path)
{
    public const int Keep = 500;
    private static readonly object Gate = new();

    public IReadOnlyList<LedgerEntry> Read()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), ToolsJson.Default.ListLedgerEntry) ?? [] : []; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    public void Append(LedgerEntry e)
    {
        lock (Gate)
        {
            var all = Read().ToList();
            all.Add(e);
            if (all.Count > Keep) all.RemoveRange(0, all.Count - Keep);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(all, ToolsJson.Default.ListLedgerEntry));
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
        }
    }
}
