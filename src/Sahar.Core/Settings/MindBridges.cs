using System.Text.Json;

namespace Sahar.Core.Settings;

/// <summary>What a Mind page handler gets: the settings file, the engine, the clock. Folders come from
/// <see cref="Sahar.Core.Mind.MindPaths"/>.</summary>
public sealed record MindBridgeContext(string SettingsPath, IEngineLink Engine, Func<DateTimeOffset> Now);

/// <summary>A handler's answer: ok, and the body it writes into the reply object (beside id/type/ok).</summary>
public sealed record MindBridgeReply(bool Ok, Action<Utf8JsonWriter> Body)
{
    public static MindBridgeReply Fail(string error) => new(false, w => w.WriteString("error", error));
}

/// <summary>
/// SEAM (0.14): the settings app's <c>mind-*</c> requests. Each stream registers its own types in its own file:
/// <c>MindBridges.Runtime.cs</c> (A: mind-status, mind-models, mind-model-*, mind-download-*, mind-bench, mind-reach),
/// <c>MindBridges.Memory.cs</c> (B: mind-profile*, mind-memory*, mind-style*, mind-import*, mind-forget, mind-export),
/// <c>MindBridges.Autonomy.cs</c> (C: mind-log*, mind-undo, mind-feedback). A type registered twice throws at startup.
/// </summary>
public static partial class MindBridges
{
    public delegate Task<MindBridgeReply> Handler(JsonElement request);

    public static Dictionary<string, Handler> Table(MindBridgeContext ctx)
    {
        var t = new Dictionary<string, Handler>(StringComparer.Ordinal);
        AddRuntime(t, ctx);
        AddMemory(t, ctx);
        AddAutonomy(t, ctx);
        return t;
    }

    static partial void AddRuntime(Dictionary<string, Handler> t, MindBridgeContext ctx);
    static partial void AddMemory(Dictionary<string, Handler> t, MindBridgeContext ctx);
    static partial void AddAutonomy(Dictionary<string, Handler> t, MindBridgeContext ctx);
}
