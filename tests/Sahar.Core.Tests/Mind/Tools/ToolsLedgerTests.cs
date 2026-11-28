using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>0.19 spec §6: tools-ledger.json says when each server/tool was approved and its hash — never args or output.</summary>
public sealed class ToolsLedgerTests
{
    [Fact]
    public void Ledger_KeepsLast500_NoArgs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sahar-ledger-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "tools-ledger.json");
            var ledger = new ToolsLedger(path);
            var at = DateTimeOffset.UnixEpoch;
            for (int i = 0; i < 505; i++) ledger.Append(new LedgerEntry(at.AddSeconds(i), "approved", "files", "t" + i, "h" + i));
            var all = ledger.Read();
            Assert.Equal(500, all.Count);
            Assert.Equal("t5", all[0].Tool);
            Assert.Equal("t504", all[^1].Tool);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("args", text);
            Assert.DoesNotContain("output", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Ledger_BrokenFile_StartsFresh()
    {
        var path = Path.Combine(Path.GetTempPath(), "sahar-ledger-" + Guid.NewGuid().ToString("n") + ".json");
        File.WriteAllText(path, "{broken");
        try
        {
            new ToolsLedger(path).Append(new LedgerEntry(DateTimeOffset.UnixEpoch, "added", "files", null, null));
            Assert.Single(new ToolsLedger(path).Read());
        }
        finally { File.Delete(path); }
    }
}
