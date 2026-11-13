namespace Sahar.Core.Mind.Drafts;

/// <summary>A message he wants drafted in his style (owner answer: Self-mode drafts YES, phase 1: she drafts, he copies
/// with one tap; she NEVER sends). <see cref="To"/> is a free label he typed («لأحمد»), kept out of logs.</summary>
public sealed record DraftRequest(string Intent, string? To = null, string? ReplyTo = null);

/// <summary>A draft: always labelled «بصوتك» in the bubble, with a one-tap copy. There is deliberately no send.</summary>
public sealed record Draft(string Text, string Label = Draft.SelfLabel)
{
    public const string SelfLabel = "بصوتك";
}

/// <summary>Stream B: builds the draft from the style card and his nearest samples, through the runtime's LLM.</summary>
public interface IDrafter
{
    Task<Draft?> DraftAsync(DraftRequest request, CancellationToken ct);
}
