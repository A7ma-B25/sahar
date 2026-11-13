using System.Text;
using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class ClaudeHookTests
{
    private static string J(string ev, string extra = "") =>
        $$"""{"session_id":"abc123","transcript_path":"C:\\Users\\me\\.claude\\x.jsonl","cwd":"C:\\Users\\me\\code\\sahar","hook_event_name":"{{ev}}"{{extra}}}""";

    private static string Tool(string ev, string tool, string input) => J(ev, $$""","tool_name":"{{tool}}","tool_input":{{input}}""");

    [Theory]
    [InlineData("Edit", """{"file_path":"C:\\Users\\me\\code\\sahar\\src\\OverlayPlan.cs","old_string":"a","new_string":"b"}""", ClaudeStep.Editing, "OverlayPlan.cs")]
    [InlineData("Write", """{"file_path":"/home/me/notes.md","content":"x"}""", ClaudeStep.Editing, "notes.md")]
    [InlineData("MultiEdit", """{"file_path":"a/b/c.txt","edits":[]}""", ClaudeStep.Editing, "c.txt")]
    [InlineData("Read", """{"file_path":"C:\\x\\y.json"}""", ClaudeStep.Reading, "y.json")]
    [InlineData("Bash", """{"command":"git push origin main","description":"Push"}""", ClaudeStep.Running, null)]
    [InlineData("Grep", """{"pattern":"secret"}""", ClaudeStep.Searching, null)]
    [InlineData("Glob", """{"pattern":"**/*.cs"}""", ClaudeStep.Searching, null)]
    [InlineData("WebFetch", """{"url":"https://example.com/a"}""", ClaudeStep.Web, null)]
    [InlineData("WebSearch", """{"query":"q"}""", ClaudeStep.Web, null)]
    [InlineData("Task", """{"prompt":"p"}""", ClaudeStep.None, null)]
    public void Parse_PreToolUse_StepAndTarget(string tool, string input, ClaudeStep step, string? target)
    {
        var s = ClaudeHook.Parse(Tool("PreToolUse", tool, input))!;
        Assert.Equal(("PreToolUse", "abc123", "sahar", step, target, false), (s.Event, s.Session, s.Project, s.Step, s.Target, s.Asking));
    }

    [Theory]
    [InlineData("UserPromptSubmit", ""","prompt":"do it" """, false)]
    [InlineData("Stop", ""","stop_hook_active":false""", false)]
    [InlineData("SessionEnd", ""","reason":"exit" """, false)]
    [InlineData("Notification", ""","message":"Claude needs your permission","notification_type":"permission_prompt" """, true)]
    [InlineData("Notification", ""","message":"Claude is waiting for your input","notification_type":"idle_prompt" """, false)]
    public void Parse_OtherEvents(string ev, string extra, bool asking)
    {
        var s = ClaudeHook.Parse(J(ev, extra))!;
        Assert.Equal((ev, asking, ClaudeStep.None, (string?)null), (s.Event, s.Asking, s.Step, s.Target));
    }

    [Fact]
    public void Parse_PermissionRequest_Asking()
    {
        var s = ClaudeHook.Parse(Tool("PermissionRequest", "Bash", """{"command":"rm -rf node_modules"}"""))!;
        Assert.True(s.Asking);
        Assert.Equal(ClaudeStep.Running, s.Step);
    }

    [Fact]
    public void Parse_PostToolUseAndFailure_KeepTheStep()
    {
        Assert.Equal(ClaudeStep.Editing, ClaudeHook.Parse(J("PostToolUse", ""","tool_name":"Edit","tool_input":{"file_path":"a.cs"},"tool_response":{"ok":true}"""))!.Step);
        Assert.Equal(ClaudeStep.Running, ClaudeHook.Parse(J("PostToolUseFailure", ""","tool_name":"Bash","tool_input":{"command":"x"},"error":"Exit 1" """))!.Step);
    }

    [Fact]
    public void Parse_TodoWrite_Todos()
    {
        var s = ClaudeHook.Parse(Tool("PreToolUse", "TodoWrite",
            """{"todos":[{"content":"افهم كيف بتكبر النافذة","status":"completed","activeForm":"x"},{"content":"OverlayPlan دلّ","status":"in_progress"},{"content":"شغّل الاختبارات","status":"pending"}]}"""))!;
        Assert.Equal(3, s.Todos!.Count);
        Assert.Equal(new ClaudeTodo("OverlayPlan دلّ", "in_progress"), s.Todos[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"session_id":"x"}""")]
    public void Parse_Garbage_Null(string text) => Assert.Null(ClaudeHook.Parse(text));

    [Fact]
    public void Signal_ArgsRoundTrip()
    {
        var s = ClaudeHook.Parse(Tool("PreToolUse", "TodoWrite", """{"todos":[{"content":"a|b","status":"pending"}]}"""))!;
        var back = ClaudeHook.FromArgs(ClaudeHook.ToArgs(s))!;
        Assert.Equal(s.Event, back.Event);
        Assert.Equal(s.Project, back.Project);
        Assert.Equal(s.Todos, back.Todos);
        var edit = ClaudeHook.Parse(Tool("PreToolUse", "Edit", """{"file_path":"C:\\a\\ملف.cs"}"""))!;
        Assert.Equal(edit with { Todos = null }, ClaudeHook.FromArgs(ClaudeHook.ToArgs(edit)) with { Todos = null });
    }

    [Fact]
    public void FromArgs_Missing_Null()
    {
        Assert.Null(ClaudeHook.FromArgs(null));
        Assert.Null(ClaudeHook.FromArgs(new Dictionary<string, string> { ["event"] = "Stop" }));
        Assert.Null(ClaudeHook.FromArgs(new Dictionary<string, string> { ["event"] = "Stop", ["session"] = "s", ["project64"] = "%%%" }));
    }

    [Fact]
    public void Signal_ArgsAreShort()
    {
        var huge = new string('x', 5 * 1024 * 1024);
        var json = J("PostToolUse", $$""","tool_name":"TodoWrite","tool_input":{"todos":[{{string.Join(',', Enumerable.Range(0, 30).Select(i => $$"""{"content":"{{new string('t', 500)}}","status":"pending"}"""))}}]},"tool_response":{"content":"{{huge}}"}""");
        var s = ClaudeHook.Parse(json)!;
        Assert.Equal(ClaudeHook.MaxTodos, s.Todos!.Count);
        var args = ClaudeHook.ToArgs(s);
        Assert.True(args.Sum(a => a.Key.Length + a.Value.Length) <= 4096);
    }

    [Fact]
    public void Parse_HugeAndCutOff_StillReadsTheHead()
    {
        // Only the first megabyte is read: the tool and its input come first, the huge response is cut.
        var json = J("PostToolUse", ""","tool_name":"Read","tool_input":{"file_path":"a/b.txt"},"tool_response":{"content":" """ + new string('y', 3 * 1024 * 1024));
        var s = ClaudeHook.Parse(json[..(1024 * 1024)])!;
        Assert.Equal((ClaudeStep.Reading, "b.txt"), (s.Step, s.Target));
    }

    [Fact]
    public void HookRun_Garbage_ExitsZeroSilently()
    {
        var output = new StringWriter();
        int sent = 0;
        Assert.Equal(0, HookRun.Run(new StringReader("garbage {{{"), output, _ => { sent++; return true; }));
        Assert.Equal("", output.ToString());
        Assert.Equal(0, sent);
    }

    [Fact]
    public void HookRun_NoEngine_ExitsZeroSilently()
    {
        var output = new StringWriter();
        Assert.Equal(0, HookRun.Run(new StringReader(J("Stop")), output, _ => throw new IOException("no pipe")));
        Assert.Equal("", output.ToString());
    }

    [Fact]
    public void HookRun_NeverPrintsADecision()
    {
        var output = new StringWriter();
        IReadOnlyDictionary<string, string>? got = null;
        Assert.Equal(0, HookRun.Run(new StringReader(Tool("PermissionRequest", "Bash", """{"command":"git push"}""")), output, a => { got = a; return true; }));
        Assert.Equal("", output.ToString());   // notify-only (owner ruling 3): Claude's own prompt answers
        Assert.Equal("true", got!["asking"]);
        Assert.DoesNotContain(got.Values, v => v.Contains("git push") || Encoding.UTF8.GetString(TryB64(v)).Contains("git push"));
    }

    private static byte[] TryB64(string v)
    {
        try { return Convert.FromBase64String(v); } catch (FormatException) { return []; }
    }
}
