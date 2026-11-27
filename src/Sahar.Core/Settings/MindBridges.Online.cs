using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Online;

namespace Sahar.Core.Settings;

// 0.17: «العقل أونلاين» — the online brain's page. The key passes through here once, to the engine and on to the Mind
// (which seals it); this process never stores it, and no log line names it.
public static partial class MindBridges
{
    static partial void AddOnline(Dictionary<string, Handler> t, MindBridgeContext ctx)
    {
        // {refresh?} → {engine, seq, info (the Mind's online-status, or null), suggestions}
        t["mind-online-status"] = async req =>
        {
            if (Str(req, "refresh") == "true") await ctx.Engine.CommandAsync("mind-online", new() { ["op"] = "status" });
            var kv = await ctx.Engine.CommandAsync("mind-online-status", null);
            string? json = kv is not null && kv.TryGetValue("json64", out var b64) ? Encoding.UTF8.GetString(Convert.FromBase64String(b64)) : null;
            return new MindBridgeReply(true, w =>
            {
                w.WriteBoolean("engine", kv is not null);
                w.WriteString("seq", kv?.GetValueOrDefault("seq") ?? "0");
                w.WritePropertyName("info");
                w.WriteRawValue(json is { Length: > 0 } ? json : "null");
                w.WriteStartObject("models");
                Strings(w, ProviderProfiles.GroqId, ProviderProfiles.GroqModels);
                Strings(w, ProviderProfiles.OpenRouterId, ProviderProfiles.OpenRouterModels);
                w.WriteEndObject();
            });
        };
        // {provider, key} → the Mind seals it and tests it; the page then reads the status.
        t["mind-online-key"] = async req =>
        {
            if (Str(req, "provider") is not { } p || !ProviderProfiles.OnlineIds.Contains(p)) return MindBridgeReply.Fail("bad-provider");
            var key = Str(req, "key")?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > OnlineKeys.MaxKey || key.Any(c => char.IsControl(c) || c == ' ')) return MindBridgeReply.Fail("bad-key");
            var kv = await ctx.Engine.CommandAsync("mind-online", new() { ["op"] = "key", ["provider"] = p, ["key64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(key)) });
            return kv is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
        };
        t["mind-online-forget"] = req => Op(ctx, "forget", Str(req, "provider"));
        t["mind-online-test"] = req => Op(ctx, "test", Str(req, "provider"));
        t["mind-online-bench"] = _ => Op(ctx, "bench", null);
    }

    private static async Task<MindBridgeReply> Op(MindBridgeContext ctx, string op, string? provider)
    {
        if (op != "bench" && (provider is null || !ProviderProfiles.OnlineIds.Contains(provider))) return MindBridgeReply.Fail("bad-provider");
        var args = new Dictionary<string, string> { ["op"] = op };
        if (provider is not null) args["provider"] = provider;
        var kv = await ctx.Engine.CommandAsync("mind-online", args);
        return kv is null ? MindBridgeReply.Fail(EngineOff) : new MindBridgeReply(true, _ => { });
    }

    private static void Strings(Utf8JsonWriter w, string name, IReadOnlyList<string> list)
    {
        w.WriteStartArray(name);
        foreach (var s in list) w.WriteStringValue(s);
        w.WriteEndArray();
    }
}
