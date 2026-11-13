using System.Text;
using System.Text.Json;

namespace Sahar.Core.Activities;

/// <summary>What Claude Code is doing, from its tool name (spec 0.13 §2 Claude Code).</summary>
public enum ClaudeStep { None, Editing, Reading, Running, Searching, Web }

/// <summary>One task of Claude's list (TodoWrite): its text and pending / in_progress / completed.</summary>
public sealed record ClaudeTodo(string Text, string Status);

/// <summary>One hook call, boiled down to what she shows: the event, the session, the project (the cwd's folder name),
/// the step and its file name (never a path, a command, a URL or a prompt), the task list, and whether Claude asks.</summary>
public sealed record ClaudeSignal(string Event, string Session, string Project, ClaudeStep Step, string? Target, IReadOnlyList<ClaudeTodo>? Todos,
    bool Asking);

/// <summary>
/// Claude Code's hook input (stdin JSON) → <see cref="ClaudeSignal"/>, and the signal ↔ the pipe's string args. The JSON
/// is read as a stream, so a cut-off input (the hook reads its first megabyte; a tool's response can be huge) still
/// gives the fields before the cut: the event, the session, the cwd, the tool and its input come first.
/// </summary>
public static class ClaudeHook
{
    public const int MaxTodos = 8, MaxText = 80, MaxTarget = 120;

    public static ClaudeSignal? Parse(string json) => Parse(Encoding.UTF8.GetBytes(json));

    public static ClaudeSignal? Parse(ReadOnlySpan<byte> utf8)
    {
        string? ev = null, session = null, cwd = null, tool = null, note = null;
        ClaudeStep step = ClaudeStep.None;
        string? target = null;
        List<ClaudeTodo>? todos = null;
        try
        {
            var r = new Utf8JsonReader(utf8, isFinalBlock: false, default);
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject) return null;
            while (r.Read() && r.TokenType == JsonTokenType.PropertyName)
            {
                var name = r.GetString();
                if (!r.Read()) break;
                switch (name)
                {
                    case "hook_event_name": ev = Str(ref r); break;
                    case "session_id": session = Str(ref r); break;
                    case "cwd": cwd = Str(ref r); break;
                    case "tool_name": tool = Str(ref r); break;
                    case "notification_type": note = Str(ref r); break;
                    case "tool_input":
                        if (r.TokenType != JsonTokenType.StartObject) { if (!r.TrySkip()) goto done; break; }
                        using (var doc = JsonDocument.ParseValue(ref r)) Input(doc.RootElement, ref target, ref todos);
                        break;
                    default:
                        if (!r.TrySkip()) goto done;
                        break;
                }
            }
        }
        catch (JsonException) { }
        done:
        if (string.IsNullOrEmpty(ev) || string.IsNullOrEmpty(session)) return null;
        step = Step(tool);
        if (step is not (ClaudeStep.Editing or ClaudeStep.Reading)) target = null;
        bool asking = ev == "PermissionRequest" || (ev == "Notification" && note == "permission_prompt");
        return new ClaudeSignal(ev, Cut(session, 64), Cut(Folder(cwd), MaxText), step, target, todos, asking);
    }

    private static string? Str(ref Utf8JsonReader r) => r.TokenType == JsonTokenType.String ? r.GetString() : null;

    private static void Input(JsonElement input, ref string? target, ref List<ClaudeTodo>? todos)
    {
        foreach (var key in new[] { "file_path", "notebook_path" })
            if (input.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String) target = Cut(Folder(p.GetString()), MaxTarget);
        if (input.TryGetProperty("todos", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            todos = [];
            foreach (var t in list.EnumerateArray())
            {
                if (todos.Count == MaxTodos) break;
                if (t.ValueKind == JsonValueKind.Object && t.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    todos.Add(new ClaudeTodo(Cut(c.GetString()!, MaxText),
                        t.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "pending"));
            }
        }
    }

    public static ClaudeStep Step(string? tool) => tool switch
    {
        "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => ClaudeStep.Editing,
        "Read" or "NotebookRead" => ClaudeStep.Reading,
        "Bash" or "BashOutput" or "PowerShell" => ClaudeStep.Running,
        "Grep" or "Glob" or "LS" => ClaudeStep.Searching,
        "WebFetch" or "WebSearch" => ClaudeStep.Web,
        _ => ClaudeStep.None,
    };

    /// <summary>The last part of a path (Windows or POSIX): a file or folder name, never the rest.</summary>
    private static string Folder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var parts = path.Replace('\\', '/').TrimEnd('/').Split('/');
        return parts[^1];
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    /// <summary>The pipe's args (short: text is cut, base64 where it is free text).</summary>
    public static IReadOnlyDictionary<string, string> ToArgs(ClaudeSignal s)
    {
        var a = new Dictionary<string, string>
        {
            ["event"] = s.Event,
            ["session"] = s.Session,
            ["project64"] = B64(s.Project),
            ["step"] = s.Step.ToString(),
            ["asking"] = s.Asking ? "true" : "false",
        };
        if (s.Target is { } t) a["target64"] = B64(t);
        if (s.Todos is { } todos) a["todos64"] = B64(string.Join('\n', todos.Select(x => $"{x.Status}|{x.Text.Replace('\n', ' ')}")));
        return a;
    }

    public static ClaudeSignal? FromArgs(IReadOnlyDictionary<string, string>? a)
    {
        if (a is null || !a.TryGetValue("event", out var ev) || !a.TryGetValue("session", out var session)) return null;
        try
        {
            string Text(string key) => a.TryGetValue(key, out var v) ? Encoding.UTF8.GetString(Convert.FromBase64String(v)) : "";
            var project = Text("project64");
            var target = a.ContainsKey("target64") ? Text("target64") : null;
            List<ClaudeTodo>? todos = null;
            if (a.ContainsKey("todos64"))
                todos = [.. Text("todos64").Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(MaxTodos)
                    .Select(l => l.Split('|', 2)).Select(p => new ClaudeTodo(Cut(p.Length > 1 ? p[1] : "", MaxText), p[0]))];
            var step = a.TryGetValue("step", out var st) && Enum.TryParse<ClaudeStep>(st, out var s) && Enum.IsDefined(s) ? s : ClaudeStep.None;
            return new ClaudeSignal(Cut(ev, 40), Cut(session, 64), Cut(project, MaxText), step, target is null ? null : Cut(target, MaxTarget), todos,
                a.TryGetValue("asking", out var ask) && ask == "true");
        }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// <c>Sahar.Engine.exe hook</c>: Claude Code runs it for every hook (async, exec form). It reads the first megabyte of
/// stdin, sends one short line to her pipe, and exits 0 — printing nothing, ever (owner ruling 3: notify only; Claude
/// Code's own prompt answers). No engine, garbage in, or any failure: still exit 0 and silent.
/// </summary>
public static class HookRun
{
    public const int MaxRead = 1024 * 1024;

    public static int Run(TextReader stdin, TextWriter stdout, Func<IReadOnlyDictionary<string, string>, bool> send)
    {
        _ = stdout;   // never written: a hook's stdout is read by Claude Code
        try
        {
            var buffer = new char[MaxRead];
            int n = 0, got;
            while (n < buffer.Length && (got = stdin.Read(buffer, n, buffer.Length - n)) > 0) n += got;
            if (ClaudeHook.Parse(new string(buffer, 0, n)) is { } signal) send(ClaudeHook.ToArgs(signal));
        }
        catch (Exception)
        {
            // The hook must never disturb Claude Code.
        }
        return 0;
    }
}
