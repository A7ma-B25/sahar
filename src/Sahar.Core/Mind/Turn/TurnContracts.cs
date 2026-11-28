using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Core.Mind.Turn;

/// <summary>The Mind's side of one turn's conversation with the engine: send a line, wait for the engine's answer to
/// a call (tool-result or answer). Implemented by the Mind process over the pipe (stream A), faked in tests.</summary>
public interface ITurnIO
{
    void Send(MindMsg msg);
    /// <summary>The engine's tool-result / answer for <paramref name="call"/> of turn <paramref name="turnId"/>.</summary>
    Task<MindMsg> ReplyAsync(string turnId, int call, CancellationToken ct);
}

/// <summary>Stream C: the structured step loop (spec §3.5: ≤ 3 steps, ≤ 3 actions per step, a 20 s chat budget).
/// Sends partial/call/say/propose/error; never parses free-form tool calls.</summary>
public interface ITurnRunner
{
    Task RunAsync(MindMsg turn, MindMsg hello, ITurnIO io, CancellationToken ct);
}

/// <summary>0.19 spec §4: the tools the Mind itself runs (MCP servers he added, image.make, voice.make). The engine
/// still validates and decides each call; after its yes the runner calls <see cref="RunAsync"/>, which applies its own
/// timeout (30 s, media 90 s), sanitizes the output and never throws for a tool's failure.</summary>
public interface IMindTools
{
    /// <summary>What may be announced now (enabled, approved, fitting; media once checked). Only for his own chat/voice turns.</summary>
    IReadOnlyList<Tools.ToolSpec> Announced { get; }
    Task<MindToolResult> RunAsync(Tools.ToolCall call, CancellationToken ct);
}

/// <summary>One Mind-side run. <see cref="Kind"/>: text | image | voice | none; <see cref="Say"/> her template line
/// (never the tool's words); <see cref="Text"/> the sanitized output (text only); <see cref="File"/> the picture or clip's
/// file name; <see cref="Error"/> a code (timeout | changed | failed | wait | too-long | quota | offline…).</summary>
public sealed record MindToolResult(bool Ok, string Kind, string Say, string? Text = null, string? File = null, string? Error = null);

/// <summary>A deterministic distress result (spec §7.4): the fixed, reviewed reply and help lines. Never improvised.</summary>
public sealed record DistressReply(string Text, IReadOnlyList<string> Help);

/// <summary>Stream B: the prompt's personal parts and the guardrails. Pure; never logs.</summary>
public interface IPromptParts
{
    /// <summary>Persona and rules for a voice (Syrian Arabic; supportive techniques only; no diagnoses or labels).</summary>
    string Persona(MindVoice voice, MindAddress address);
    /// <summary>The confirmed profile and retrieved memories for this line, as quoted data blocks within budget.</summary>
    string PersonalBlock(string line, int maxTokens);
    /// <summary>A cue phrase or a high-risk tag → the fixed reply; null otherwise (idioms excluded).</summary>
    DistressReply? Distress(string line);
    /// <summary>The stricter of the model's tag and the Core keyword check.</summary>
    Sensitivity Sensitive(string text, Sensitivity modelTag);
}

/// <summary>0.15.0: where one turn's time went — the model's steps, waiting for the engine (a tool, or his «إي»), the
/// first word he could read — and its tokens (prompt/output). Numbers only; the Mind logs <see cref="ToString"/>.</summary>
public sealed record TurnTiming(int ModelMs, int WaitMs, int FirstWordMs, int Steps, int PromptTokens, int OutTokens)
{
    public override string ToString() => System.FormattableString.Invariant(
        $"model {ModelMs}ms, wait {WaitMs}ms, first word {FirstWordMs}ms, steps {Steps}, tokens {PromptTokens}/{OutTokens}");
}
