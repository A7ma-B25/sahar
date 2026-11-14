using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sahar.Core.Activities;

public sealed record LinkResult(bool Ok, string? Json, string? Error);

/// <summary>
/// «اربطها / افصلها» (spec 0.13 §2 Install): Claude Code's <c>~/.claude/settings.json</c> gets one async hook per
/// event that runs <c>Sahar.Engine.exe hook</c> (exec form: no shell). Ours are the handlers whose args start with
/// <c>hook</c> and whose command is a <c>Sahar.Engine.exe</c>; they are repointed when Sahar moves; nothing else is
/// touched. A file that is not valid JSON is never written.
/// </summary>
public static class ClaudeLink
{
    public const string ExeName = "Sahar.Engine.exe";
    public const string Invalid = "invalid";

    public static IReadOnlyList<string> Events { get; } =
        ["UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "Notification", "PermissionRequest", "Stop", "SessionEnd"];

    public static LinkResult Install(string? json, string exePath) => Edit(json, hooks =>
    {
        foreach (var ev in Events)
        {
            var groups = hooks[ev] as JsonArray ?? [];
            hooks[ev] = groups;
            groups.Add((JsonNode)new JsonObject
            {
                ["matcher"] = "",
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command", ["command"] = exePath, ["args"] = new JsonArray("hook"), ["async"] = true, ["timeout"] = 10,
                }),
            });
        }
    });

    public static LinkResult Remove(string? json, string exePath) => Edit(json, _ => { });

    /// <summary>At her start (review #6): linked hooks that point at another Sahar.Engine.exe are repointed here;
    /// not linked, or already here, nothing to write (Ok false).</summary>
    public static LinkResult Follow(string? json, string exePath)
    {
        if (Root(json) is not { } root || root["hooks"] is not JsonObject hooks) return new LinkResult(false, null, "not-linked");
        bool ours = hooks.Any(p => p.Value is JsonArray g && g.OfType<JsonObject>().Any(x => x["hooks"] is JsonArray hs && hs.OfType<JsonObject>().Any(Ours)));
        if (!ours) return new LinkResult(false, null, "not-linked");
        return Linked(json, exePath) ? new LinkResult(false, null, "same") : Install(json, exePath);
    }

    public static bool Linked(string? json, string exePath)
    {
        if (Root(json) is not { } root || root["hooks"] is not JsonObject hooks) return false;
        return Events.All(ev => hooks[ev] is JsonArray groups && groups.OfType<JsonObject>().Any(g => g["hooks"] is JsonArray hs
            && hs.OfType<JsonObject>().Any(h => Ours(h) && string.Equals((string?)h["command"], exePath, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>Strips ours everywhere, lets <paramref name="add"/> add ours back, drops what became empty.</summary>
    private static LinkResult Edit(string? json, Action<JsonObject> add)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(json)) root = [];
        else if (Root(json) is { } r) root = r;
        else return new LinkResult(false, null, Invalid);
        if (root["hooks"] is not null and not JsonObject) return new LinkResult(false, null, Invalid);
        var hooks = root["hooks"] as JsonObject ?? [];
        if (hooks.Any(p => p.Value is not JsonArray)) return new LinkResult(false, null, Invalid);
        foreach (var (ev, value) in hooks.ToList())
        {
            var groups = (JsonArray)value!;
            foreach (var g in groups.OfType<JsonObject>().ToList())
            {
                if (g["hooks"] is not JsonArray hs) continue;
                foreach (var h in hs.OfType<JsonObject>().Where(Ours).ToList()) hs.Remove(h);
                if (hs.Count == 0) groups.Remove(g);
            }
            if (groups.Count == 0) hooks.Remove(ev);
        }
        add(hooks);
        if (hooks.Count == 0) root.Remove("hooks");
        else root["hooks"] = hooks;
        return new LinkResult(true, Write(root), null);
    }

    private static bool Ours(JsonObject h) =>
        h["args"] is JsonArray { Count: > 0 } args && args[0] is JsonValue v && v.TryGetValue<string>(out var a) && a == "hook"
        && h["command"] is JsonValue c && c.TryGetValue<string>(out var cmd)
        && cmd.Replace('/', '\\').Split('\\')[^1].Equals(ExeName, StringComparison.OrdinalIgnoreCase);

    private static JsonObject? Root(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static string Write(JsonObject root)
    {
        using var s = new MemoryStream();
        using (var w = new Utf8JsonWriter(s, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            root.WriteTo(w);
        return Encoding.UTF8.GetString(s.ToArray());
    }
}

/// <summary>The file side of <see cref="ClaudeLink"/>: <c>settings.json.before-sahar</c> once (the file as it was before
/// Sahar ever touched it), <c>settings.json.sahar-last</c> on every write (the file just before that write).</summary>
public static class ClaudeLinkFile
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    public static bool Linked(string path, string exePath)
    {
        try { return File.Exists(path) && ClaudeLink.Linked(File.ReadAllText(path), exePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static LinkResult Apply(string path, Func<string?, LinkResult> change)
    {
        try
        {
            string? before = File.Exists(path) ? File.ReadAllText(path) : null;
            var r = change(before);
            if (!r.Ok || r.Json is null) return r;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (before is not null)
            {
                if (!File.Exists(path + ".before-sahar")) File.WriteAllText(path + ".before-sahar", before);
                File.WriteAllText(path + ".sahar-last", before);
            }
            var tmp = path + ".sahar-tmp";
            File.WriteAllText(tmp, r.Json);
            File.Move(tmp, path, overwrite: true);
            return r;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LinkResult(false, null, "io");
        }
    }
}
