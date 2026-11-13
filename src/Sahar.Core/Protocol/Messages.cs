namespace Sahar.Core.Protocol;

/// <summary>
/// One line sent to the engine on the "sahar" pipe. Type is "event" (something happened) or "cmd" (do something).
/// Commands: state, expression {key}, motion {kind, vx?, vy?}, pose {x, y}, capturable {on}, debug-device-lost, quit.
/// The settings app's (0.12): settings-reload {hello?: "ask"} → applied=…,… hotkey=ok|taken|invalid|same;
/// city-search {text64} → ok=true name64 lat lon | ok=false reason64; shortcut-try {value} → ok=true | ok=false reason64;
/// clipboard-clear → ok=true; erase-all → ok=true, then her data folder is emptied and the engine quits.
/// </summary>
public sealed record PipeRequest(string Type, string Name, IReadOnlyDictionary<string, string>? Args = null);

/// <summary>The engine's one-line answer.</summary>
public sealed record PipeReply(bool Ok, string? Error = null, EngineState? State = null);

/// <summary>What Sahar is doing right now (positions and sizes in DIPs).</summary>
public sealed record EngineState(
    string Expression, string Motion, bool MenuOpen, bool Hidden, bool Quiet,
    float X, float Y, float Width, float Height, long WorkingSetBytes);
