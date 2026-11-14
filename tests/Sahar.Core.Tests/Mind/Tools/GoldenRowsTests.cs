using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>The golden rows (spec §9.3; stream C: commands, policy, initiative) are well formed: every expected call
/// passes Core's re-validation, and every policy row's verdict is what the policy gives for a chat line.</summary>
public sealed class GoldenRowsTests
{
    internal static string Golden(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Sahar.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tests", "mind", "golden", file);
    }

    private static IEnumerable<JsonElement> Rows(string file) =>
        File.ReadLines(Golden(file)).Where(l => l.Trim().Length > 0).Select(l => JsonDocument.Parse(l).RootElement.Clone());

    [Fact]
    public void Commands_EveryExpectationValidates()
    {
        var reg = MindParts.ToolRegistry(ToolCatalog.All);
        var rows = Rows("commands.jsonl").ToList();
        Assert.True(rows.Count >= 60, $"{rows.Count} rows");
        Assert.Equal(rows.Count, rows.Select(r => r.GetProperty("id").GetString()).Distinct().Count());
        foreach (var r in rows)
        {
            var e = r.GetProperty("expect");
            var check = reg.Validate(new ToolCall(e.GetProperty("tool").GetString()!, e.GetProperty("args")));
            Assert.True(check.Ok, $"{r.GetProperty("id")}: {check.Error} {check.Field}");
        }
        // every tool of the core catalog has a row, so the bench covers it
        var tools = rows.Select(r => r.GetProperty("expect").GetProperty("tool").GetString()).ToHashSet();
        Assert.All(ToolCatalog.Core, t => Assert.Contains(t.Name, tools));
    }

    [Fact]
    public void Policy_ChatRowsMatchThePolicy()
    {
        var reg = MindParts.ToolRegistry(ToolCatalog.All);
        var policy = MindParts.Policy();
        foreach (var r in Rows("policy.jsonl"))
        {
            var id = r.GetProperty("id").GetString()!;
            var verdict = r.GetProperty("verdict").GetString();
            var e = r.GetProperty("expect");
            if (e.GetProperty("tool").ValueKind == JsonValueKind.Null)
            {
                Assert.Equal("refuse", verdict);   // no tool exists: nothing can run
                continue;
            }
            var call = new ToolCall(e.GetProperty("tool").GetString()!, e.GetProperty("args"));
            Assert.True(reg.Validate(call).Ok, id);
            bool self = r.GetProperty("text").GetString()!.StartsWith("[initiative]", StringComparison.Ordinal);
            var ctx = self ? new AutonomyContext(Away: id.EndsWith("01", StringComparison.Ordinal), Fullscreen: id.EndsWith("02", StringComparison.Ordinal)) : new AutonomyContext();
            var d = policy.Decide(reg.Announced.First(t => t.Name == call.Tool), call, reg.ClassOf(call), self ? ActSource.Self : ActSource.User, ctx);
            Assert.True(verdict == d.Verdict.ToString().ToLowerInvariant(), $"{id}: {d.Verdict} ({d.Reason})");
        }
    }

    [Fact]
    public void Initiative_RowsAreWellFormed()
    {
        var rows = Rows("initiative.jsonl").ToList();
        Assert.True(rows.Count >= 10);
        Assert.All(rows, r => Assert.Equal("initiative", r.GetProperty("source").GetString()));
        Assert.All(rows, r => Assert.True(r.GetProperty("situation").TryGetProperty("localTime", out _)));
    }
}
