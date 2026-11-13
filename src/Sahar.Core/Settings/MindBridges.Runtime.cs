using System.Security.Cryptography;
using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Settings;

// STREAM A owns this file: «عقلي» and «النماذج» — status, the model list, add/use/remove, download, bench.
public static partial class MindBridges
{
    public const string EngineOff = "engine-off";
    public const string BadQuery = "شو هاد؟ حطّ رابط Hugging Face، أو اسم متل unsloth/gemma-4-E4B-it-GGUF، أو مسار ملف ‎.gguf‎.";
    public const string NoFile = "ما لقيت هالملف.";

    /// <summary>The settings app sets these: a native "open .gguf" dialog, and the HTTP handler for the HF API.</summary>
    public static Func<Task<string?>>? PickGguf { get; set; }
    public static Func<HttpMessageHandler> Http { get; set; } = () => new SocketsHttpHandler();
    /// <summary>The models folder (tests point it elsewhere).</summary>
    public static Func<string> ModelsFolder { get; set; } = () => MindPaths.Models;

    static partial void AddRuntime(Dictionary<string, Handler> t, MindBridgeContext ctx)
    {
        t["mind-status"] = async _ => await Status(ctx);
        t["mind-models"] = _ => Task.FromResult(new MindBridgeReply(true, w => Models(w)));
        t["mind-model"] = async req =>
        {
            var op = Str(req, "op");
            if (op == "add") return await Add(ctx, Str(req, "query"));
            if (op is not ("use" or "remove") || Str(req, "model") is not { } id) return MindBridgeReply.Fail("bad-op");
            if (ctx.Engine.Running)
            {
                // The Mind owns the switch (it unloads the old model first); the reply comes back as the new list.
                await ctx.Engine.CommandAsync("mind-model", new() { ["op"] = op, ["model"] = id });
                await Task.Delay(300);
            }
            else if (op == "use") new ModelRegistry(ModelsFolder()).Use(id);
            else Remove(id);
            return new MindBridgeReply(true, w => Models(w));
        };
        t["mind-download"] = async req =>
        {
            var op = Str(req, "op") ?? "start";
            if (op is not ("check" or "start" or "pause" or "resume" or "cancel")) return MindBridgeReply.Fail("bad-op");
            if (!ctx.Engine.Running) return MindBridgeReply.Fail(EngineOff);
            var args = new Dictionary<string, string> { ["op"] = op };
            if (Str(req, "model") is { } m) args["model"] = m;
            var r = await ctx.Engine.CommandAsync("mind-download", args);
            return r is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
        };
        t["mind-bench"] = async req =>
        {
            if (!ctx.Engine.Running) return MindBridgeReply.Fail(EngineOff);
            var r = await ctx.Engine.CommandAsync("mind-bench", Str(req, "quick") == "true" ? new() { ["quick"] = "true" } : null);
            return r is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
        };
        t["mind-pick"] = async _ =>
        {
            if (PickGguf is null || await PickGguf() is not { } path) return MindBridgeReply.Fail("cancelled");
            return await Add(ctx, path);
        };
    }

    private static async Task<MindBridgeReply> Status(MindBridgeContext ctx)
    {
        var s = SettingsStore.Readable(ctx.SettingsPath) ? SettingsStore.Load(ctx.SettingsPath) : new SaharSettings();
        var kv = ctx.Engine.Running ? await ctx.Engine.CommandAsync("mind-status", null) : null;
        string? V(string k) => kv is not null && kv.TryGetValue(k, out var v) && v != "-" ? v : null;
        long L(string k) => long.TryParse(V(k), out var n) ? n : 0;
        return new MindBridgeReply(true, w =>
        {
            w.WriteBoolean("enabled", s.MindEnabled);
            w.WriteBoolean("engine", kv is not null);
            w.WriteString("phase", V("phase") ?? "none");
            if (V("dlState") is { } state)
            {
                w.WriteStartObject("download");
                w.WriteString("model", V("dlModel"));
                w.WriteString("state", state);
                w.WriteNumber("done", L("dlDone"));
                w.WriteNumber("total", L("dlTotal"));
                w.WriteNumber("bps", L("dlBps"));
                w.WriteString("error", V("dlError"));
                w.WriteEndObject();
            }
            Models(w);
        });
    }

    private static void Models(Utf8JsonWriter w)
    {
        var reg = new ModelRegistry(ModelsFolder());
        var list = reg.List();
        w.WritePropertyName("models");
        w.WriteRawValue(JsonSerializer.Serialize(new ModelManifest(list, reg.Active?.Id), MindJson.Default.ModelManifest));
        var cuda = RuntimePins.Load();
        w.WriteNumber("cudaBytes", (cuda.Asset("cuda")?.Bytes ?? 0) + (cuda.Asset("cudart")?.Bytes ?? 0));
        w.WriteBoolean("cudaInstalled", File.Exists(Path.Combine(MindPaths.Runtime, "cuda", "llama-server.exe")));
    }

    /// <summary>«زيدي نموذج»: a local .gguf (hashed here, kept where it is) or a Hugging Face link/name (pinned now).</summary>
    private static async Task<MindBridgeReply> Add(MindBridgeContext ctx, string? query)
    {
        var reg = new ModelRegistry(ModelsFolder());
        if (ModelRegistry.LocalGguf(query) is { } path)
        {
            if (!File.Exists(path)) return new MindBridgeReply(false, w => w.WriteString("reason", NoFile));
            // Off the page's thread: a 5 GB file takes seconds to hash.
            var sha = await Task.Run(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                return Convert.ToHexStringLower(SHA256.HashData(fs));
            });
            var e = reg.AddLocal(path, sha, new FileInfo(path).Length);
            return new MindBridgeReply(true, w => { w.WriteString("added", e.Id); Models(w); });
        }
        if (HfRef.Parse(query) is not { } hf) return new MindBridgeReply(false, w => w.WriteString("reason", BadQuery));
        var r = await new HfResolver(Http()).ResolveAsync(hf, CancellationToken.None);
        if (r.Reason is { } why) return new MindBridgeReply(false, w => w.WriteString("reason", why));
        var added = reg.AddHf(r.Owner!, r.Repo!, r.Commit!, r.File!, r.Bytes, r.Sha256!, r.License);
        return new MindBridgeReply(true, w => { w.WriteString("added", added.Id); Models(w); });
    }

    private static void Remove(string id)
    {
        var reg = new ModelRegistry(ModelsFolder());
        if (reg.Find(id) is not { } e) return;
        if (e.Origin != ModelOrigin.Local && e.LocalPath is { } p) { try { File.Delete(p); } catch (IOException) { } }
        if (e.Origin == ModelOrigin.Recommended) reg.MarkRemoved(id); else reg.Remove(id);
    }

    private static string? Str(JsonElement req, string key) =>
        req.ValueKind == JsonValueKind.Object && req.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
