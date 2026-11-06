namespace Sahar.Core.Protocol;

/// <summary>
/// One line sent to the engine on the "sahar" pipe. Type is "event" (something happened) or "cmd" (do something).
/// Commands: state, expression {key}, motion {kind, vx?, vy?}, pose {x, y}, capturable {on}, debug-device-lost, quit.
/// </summary>
public sealed record PipeRequest(string Type, string Name, IReadOnlyDictionary<string, string>? Args = null);

/// <summary>The engine's one-line answer.</summary>
public sealed record PipeReply(bool Ok, string? Error = null, EngineState? State = null);

/// <summary>What Sahar is doing right now (positions and sizes in DIPs).</summary>
public sealed record EngineState(
    string Expression, string Motion, bool MenuOpen, bool Hidden, bool Quiet,
    float X, float Y, float Width, float Height, long WorkingSetBytes);
