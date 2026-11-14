using System.Text;
using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Log;

namespace Sahar.Core.Settings;

// STREAM C owns this file (feat/0.14-autonomy): «شو عملت اليوم» — mind-log, mind-undo, mind-feedback.
public static partial class MindBridges
{
    /// <summary>The log when she is not running: read from the encrypted box (nothing can be undone then).</summary>
    public static Func<IActionLog> AutonomyOfflineLog { get; set; } = () =>
        MindParts.ActionLog(MindParts.SecretBox(MindPaths.Personal), _ => null, () => DateTimeOffset.Now);

    static partial void AddAutonomy(Dictionary<string, Handler> t, MindBridgeContext ctx)
    {
        // {day: today|yesterday|all} → {engine, log:{entries, counts}}. She answers (she knows what can still be undone).
        t["mind-log"] = async req =>
        {
            var day = AutStr(req, "day") is "yesterday" or "all" ? AutStr(req, "day")! : "today";
            var kv = await ctx.Engine.CommandAsync("mind-log", new() { ["day"] = day });
            string? json = kv is not null && kv.TryGetValue("json64", out var b64) ? AutDecode(b64) : null;
            bool engine = json is not null;
            if (json is null)
            {
                var log = AutonomyOfflineLog();
                json = ActionLogView.Json(ActionLogView.Pick(log, day, ctx.Now()), _ => false);
            }
            return new MindBridgeReply(true, w =>
            {
                w.WriteBoolean("engine", engine);
                w.WritePropertyName("log");
                w.WriteRawValue(json);
            });
        };
        // {entry} → ok when the inverse ran (a timer still running, a note still there, the old setting back).
        t["mind-undo"] = async req =>
        {
            if (AutStr(req, "entry") is not { Length: > 0 } id) return MindBridgeReply.Fail("bad-request");
            var kv = await ctx.Engine.CommandAsync("mind-undo", new() { ["id"] = id });
            if (kv is null) return MindBridgeReply.Fail("engine-off");
            return kv.GetValueOrDefault("ok") == "true" ? new MindBridgeReply(true, _ => { }) : MindBridgeReply.Fail("too-late");
        };
        // {entry, value: 1 | -1 | 0} → 👍 / 👎 / none.
        t["mind-feedback"] = async req =>
        {
            if (AutStr(req, "entry") is not { Length: > 0 } id || !req.TryGetProperty("value", out var v) || v.ValueKind != JsonValueKind.Number
                || !v.TryGetInt32(out int value) || value is < -1 or > 1)
                return MindBridgeReply.Fail("bad-request");
            var kv = await ctx.Engine.CommandAsync("mind-feedback", new() { ["id"] = id, ["value"] = value.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            if (kv is null) AutonomyOfflineLog().Feedback(id, value);   // she is off: nobody else writes the log now
            return new MindBridgeReply(true, _ => { });
        };
    }

    private static string? AutStr(JsonElement req, string name) =>
        req.ValueKind == JsonValueKind.Object && req.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? AutDecode(string b64)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }
}
