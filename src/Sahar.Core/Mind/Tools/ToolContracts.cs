using System.Text.Json;

namespace Sahar.Core.Mind.Tools;

/// <summary>The autonomy class of a tool (spec §3.7). Read tools only report; A is her own reversible world; B leaves it
/// or is harder to undo (always asks); C never decides alone (no such tool exists in 0.14; the class exists so a
/// proposal naming one is refused).</summary>
public enum ActionClass { Read, A, B, C }

public enum ArgType { Int, String, Bool, Enum, Time }

/// <summary>One argument's schema: the same rule builds the model's JSON schema (grammar) and re-validates in Core.</summary>
public sealed record ArgSpec(string Name, ArgType Type, bool Required = true, int? Min = null, int? Max = null,
    int? MaxLength = null, IReadOnlyList<string>? Values = null);

/// <summary>A tool she may propose (spec §3.4): <c>{name, description_ar, args, class, readOnly, undo}</c>.
/// <see cref="Class"/> can depend on the arguments (settings.set: A or B by key): see <see cref="IToolRegistry.ClassOf"/>.</summary>
public sealed record ToolSpec(string Name, string DescriptionAr, IReadOnlyList<ArgSpec> Args, ActionClass Class,
    bool ReadOnly = false, bool Undoable = false);

/// <summary>A proposed call, as the Mind sends it (args = the JSON object the grammar produced).</summary>
public sealed record ToolCall(string Tool, JsonElement Args, string? Why = null);

/// <summary>What the engine did. <see cref="Summary"/> is her truthful confirmation from Core templates (never the
/// model's words); <see cref="Data"/> is the read result as JSON text; <see cref="UndoToken"/> is set while undo
/// is meaningful.</summary>
public sealed record ToolOutcome(bool Ok, string Summary, string? Data = null, string? UndoToken = null, string? Error = null)
{
    public static ToolOutcome Fail(string error, string summary = "") => new(false, summary, Error: error);
}

/// <summary>The re-validation verdict: <see cref="Error"/> is invalid | unknown-tool | not-announced.</summary>
public sealed record ToolCheck(bool Ok, string? Error = null, string? Field = null)
{
    public static readonly ToolCheck Valid = new(true);
}

/// <summary>An ability the engine runs (stream C maps each to existing engine code). Runs on the UI thread.</summary>
public interface ITool
{
    ToolSpec Spec { get; }
    ToolOutcome Run(ToolCall call);
    /// <summary>Reverses a run while it still means something (a timer still running, a note still there). False when
    /// the inverse no longer applies.</summary>
    bool Undo(string undoToken);
}

/// <summary>The tool list (stream C). Announced in hello; the grammar allows only these names; every call is
/// re-validated against the same schema before anything runs.</summary>
public interface IToolRegistry
{
    IReadOnlyList<ToolSpec> Announced { get; }
    ToolCheck Validate(ToolCall call);
    /// <summary>The call's class (args may raise it: settings.set of a B key is B).</summary>
    ActionClass ClassOf(ToolCall call);
    /// <summary>The model's decision JSON schema (spec §3.5) for these tools, as JSON text.</summary>
    string DecisionSchema();
}
