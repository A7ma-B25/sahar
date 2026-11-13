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
