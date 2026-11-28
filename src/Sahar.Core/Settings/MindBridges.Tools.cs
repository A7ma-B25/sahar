using System.Text;
using System.Text.Json;

namespace Sahar.Core.Settings;

// 0.19 «الأدوات»: the page's tool ops. A secret passes here once (base64, to the engine and on to the Mind, which seals
// it); this process never stores it, never echoes it, and no log line names it. Paths and links pass to the Mind,
// which checks them again before anything runs.
public static partial class MindBridges
{
    /// <summary>The settings app sets this: Windows' open-file dialog for an .exe (null = cancelled).</summary>
    public static Func<Task<string?>>? PickExe { get; set; }

    private static readonly HashSet<string> ToolOps = new(StringComparer.Ordinal)
    {
        "status", "remove", "enable", "disable", "grant", "sensitive", "approve", "refresh", "media-grant", "check-image", "check-voice",
    };

    static partial void AddTools(Dictionary<string, Handler> t, MindBridgeContext ctx)
    {
        // {refresh?} → {engine, seq, info (the Mind's tools-status, or null)}
        t["mind-tools-status"] = async req =>
        {
            if (Str(req, "refresh") == "true") await ctx.Engine.CommandAsync("mind-tools", new() { ["op"] = "status" });
            var kv = await ctx.Engine.CommandAsync("mind-tools-status", null);
            string? json = kv is not null && kv.TryGetValue("json64", out var b64) ? Encoding.UTF8.GetString(Convert.FromBase64String(b64)) : null;
            return new MindBridgeReply(true, w =>
            {
                w.WriteBoolean("engine", kv is not null);
                w.WriteString("seq", kv?.GetValueOrDefault("seq") ?? "0");
                w.WritePropertyName("info");
                w.WriteRawValue(json is { Length: > 0 } ? json : "null");
            });
        };
        // {name, kind: url|exe, url?, exe?, args?, secretKind?, secretName?, secret?} → the Mind adds, connects and lists.
        t["mind-tools-add"] = async req =>
        {
            var kind = Str(req, "kind");
            if (kind is not ("url" or "exe")) return MindBridgeReply.Fail("bad-kind");
            var name = Str(req, "name")?.Trim();
            if (name is not { Length: > 0 and <= 40 }) return MindBridgeReply.Fail("bad-name");
            var url = Str(req, "url")?.Trim() ?? "";
            var exe = Str(req, "exe")?.Trim() ?? "";
            if (kind == "url" && !(Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback))))
                return MindBridgeReply.Fail("bad-url");
            if (kind == "exe" && (exe.Length is 0 or > 1024 || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) return MindBridgeReply.Fail("bad-exe");
            var secretKind = Str(req, "secretKind") is "header" or "env" ? Str(req, "secretKind") : null;
            var secret = secretKind is null ? null : Str(req, "secret");
            if (secretKind is not null && (secret is not { Length: > 0 and <= 4096 } || Str(req, "secretName") is not { Length: > 0 and <= 64 })) return MindBridgeReply.Fail("bad-secret");
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("name", name);
                w.WriteString("kind", kind);
                if (kind == "url") w.WriteString("url", url);
                else { w.WriteString("exe", exe); w.WriteString("args", (Str(req, "args") ?? "").Trim()); }
                if (secretKind is not null) { w.WriteString("secretKind", secretKind); w.WriteString("secretName", Str(req, "secretName")!.Trim()); }
                w.WriteEndObject();
            }
            var args = new Dictionary<string, string> { ["op"] = "add", ["data64"] = Convert.ToBase64String(ms.ToArray()) };
            if (secret is not null) args["value64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(secret.Trim()));
            var kv = await ctx.Engine.CommandAsync("mind-tools", args);
            return kv is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
        };
        // {op, server?, tool?, value?}
        t["mind-tools-op"] = async req =>
        {
            var op = Str(req, "op");
            if (op is null || !ToolOps.Contains(op)) return MindBridgeReply.Fail("bad-op");
            var server = Str(req, "server");
            var tool = Str(req, "tool");
            bool needsServer = op is "remove" or "enable" or "disable" or "grant" or "sensitive" or "approve" or "refresh" or "media-grant";
            bool needsTool = op is "enable" or "disable" or "grant" or "sensitive";
            if ((needsServer && server is not { Length: > 0 and <= 16 }) || (needsTool && tool is not { Length: > 0 and <= 128 })) return MindBridgeReply.Fail("bad-request");
            var args = new Dictionary<string, string> { ["op"] = op };
            if (server is not null) args["server"] = server;
            if (tool is not null) args["tool64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(tool));
            if (Str(req, "value") is { Length: > 0 and <= 16 } value) args["value64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
            var kv = await ctx.Engine.CommandAsync("mind-tools", args);
            return kv is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
        };
        // → {path} (Windows' own dialog; nothing is read here)
        t["mind-tools-pick"] = async _ =>
        {
            if (PickExe is null || await PickExe() is not { } path) return MindBridgeReply.Fail("cancelled");
            return new MindBridgeReply(true, w => w.WriteString("path", path));
        };
    }
}
