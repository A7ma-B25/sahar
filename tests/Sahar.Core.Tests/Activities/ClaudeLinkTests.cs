using System.Text.Json.Nodes;
using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class ClaudeLinkTests
{
    private const string Exe = @"C:\Users\me\Apps\Sahar\Sahar.Engine.exe";

    private static JsonObject Root(string json) => JsonNode.Parse(json)!.AsObject();

    private static IEnumerable<JsonObject> Ours(JsonObject root) =>
        root["hooks"]!.AsObject().SelectMany(e => e.Value!.AsArray()).SelectMany(g => g!["hooks"]!.AsArray())
            .Select(h => h!.AsObject()).Where(h => (string?)h["args"]?[0] == "hook");

    [Fact]
    public void Install_FreshFile()
    {
        var r = ClaudeLink.Install(null, Exe);
        Assert.True(r.Ok);
        var root = Root(r.Json!);
        Assert.Equal(ClaudeLink.Events.Order(), root["hooks"]!.AsObject().Select(e => e.Key).Order());
        var h = Ours(root).First();
        Assert.Equal(("command", Exe, true), ((string?)h["type"], (string?)h["command"], (bool?)h["async"]));
        Assert.Equal(ClaudeLink.Events.Count, Ours(root).Count());
        Assert.True(ClaudeLink.Linked(r.Json, Exe));
    }

    [Fact]
    public void Install_KeepsOtherHooks()
    {
        const string mine = """
        {"model":"opus","hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"block-rm.sh"}]}],
         "SessionStart":[{"hooks":[{"type":"command","command":"hello.sh"}]}]}}
        """;
        var root = Root(ClaudeLink.Install(mine, Exe).Json!);
        Assert.Equal("opus", (string?)root["model"]);
        Assert.Equal("block-rm.sh", (string?)root["hooks"]!["PreToolUse"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal("hello.sh", (string?)root["hooks"]!["SessionStart"]![0]!["hooks"]![0]!["command"]);
        Assert.Equal(2, root["hooks"]!["PreToolUse"]!.AsArray().Count);
    }

    [Fact]
    public void Install_Twice_NoDuplicates()
    {
        var once = ClaudeLink.Install(null, Exe).Json!;
        var twice = ClaudeLink.Install(once, Exe).Json!;
        Assert.Equal(ClaudeLink.Events.Count, Ours(Root(twice)).Count());
    }

    [Fact]
    public void Install_Repoints_WhenMoved()
    {
        var old = ClaudeLink.Install(null, @"D:\Old\Sahar.Engine.exe").Json!;
        Assert.False(ClaudeLink.Linked(old, Exe));
        var moved = ClaudeLink.Install(old, Exe).Json!;
        Assert.All(Ours(Root(moved)), h => Assert.Equal(Exe, (string?)h["command"]));
        Assert.Equal(ClaudeLink.Events.Count, Ours(Root(moved)).Count());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1]")]
    [InlineData("""{"hooks":[1]}""")]
    [InlineData("""{"hooks":{"Stop":{"x":1}}}""")]
    public void Install_InvalidJson_Refused(string json)
    {
        var r = ClaudeLink.Install(json, Exe);
        Assert.False(r.Ok);
        Assert.Null(r.Json);
    }

    [Fact]
    public void Install_EmptyFile_IsFresh() => Assert.True(ClaudeLink.Install("  \n", Exe).Ok);

    [Fact]
    public void Remove_LeavesOthers_EmptyGroupsDropped()
    {
        const string mine = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"say.sh"}]}]},"theme":"dark"}""";
        var linked = ClaudeLink.Install(mine, Exe).Json!;
        var removed = Root(ClaudeLink.Remove(linked, Exe).Json!);
        Assert.Equal("dark", (string?)removed["theme"]);
        Assert.Equal(["Stop"], removed["hooks"]!.AsObject().Select(e => e.Key));
        Assert.Equal("say.sh", (string?)removed["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]);
        Assert.False(ClaudeLink.Linked(removed.ToJsonString(), Exe));
        Assert.Null(Root(ClaudeLink.Remove(ClaudeLink.Install(null, Exe).Json!, Exe).Json!)["hooks"]);
    }

    [Fact]
    public void NotOurs_SameNameOtherArgs_Untouched()
    {
        const string mine = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"C:\\x\\Sahar.Engine.exe","args":["--version"]}]}]}}""";
        var removed = Root(ClaudeLink.Remove(ClaudeLink.Install(mine, Exe).Json!, Exe).Json!);
        Assert.Equal("--version", (string?)removed["hooks"]!["Stop"]![0]!["hooks"]![0]!["args"]![0]);
    }

    private static string Dir() => Directory.CreateTempSubdirectory("claude").FullName;

    [Fact]
    public void File_BackupOnce_LastEveryWrite()
    {
        var path = Path.Combine(Dir(), "settings.json");
        File.WriteAllText(path, """{"theme":"dark"}""");
        Assert.True(ClaudeLinkFile.Apply(path, j => ClaudeLink.Install(j, Exe)).Ok);
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(path + ".before-sahar"));
        var afterFirst = File.ReadAllText(path);
        Assert.True(ClaudeLinkFile.Apply(path, j => ClaudeLink.Install(j, @"D:\New\Sahar.Engine.exe")).Ok);
        Assert.Equal("""{"theme":"dark"}""", File.ReadAllText(path + ".before-sahar"));   // once
        Assert.Equal(afterFirst, File.ReadAllText(path + ".sahar-last"));                  // every write
        Assert.True(ClaudeLink.Linked(File.ReadAllText(path), @"D:\New\Sahar.Engine.exe"));
    }

    [Fact]
    public void File_Missing_CreatedNoBackup()
    {
        var path = Path.Combine(Dir(), ".claude", "settings.json");
        Assert.True(ClaudeLinkFile.Apply(path, j => ClaudeLink.Install(j, Exe)).Ok);
        Assert.True(ClaudeLink.Linked(File.ReadAllText(path), Exe));
        Assert.False(File.Exists(path + ".before-sahar"));
        Assert.True(ClaudeLinkFile.Linked(path, Exe));
    }

    [Fact]
    public void File_Unreadable_NotWritten()
    {
        var path = Path.Combine(Dir(), "settings.json");
        File.WriteAllText(path, "{oops");
        Assert.False(ClaudeLinkFile.Apply(path, j => ClaudeLink.Install(j, Exe)).Ok);
        Assert.Equal("{oops", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".sahar-last"));
    }
}
