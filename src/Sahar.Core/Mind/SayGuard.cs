using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Mind;

/// <summary>What happened to a say: kept, or replaced by the distress reply (<c>rewritten</c> is no longer produced:
/// the word filter was removed by the owner on 2026-11-27).</summary>
public sealed record SayResult(MindMsg Msg, string Kind)
{
    public string? Text => Msg.Text;
}

/// <summary>
/// SEAM (0.14, the last check on her words; the Mind host runs it on every say before it leaves): the model's
/// <c>risk: high</c> becomes B's fixed reviewed reply (<see cref="Safety.Distress.Reply"/>). The decision schema
/// (stream C) carries the optional <c>risk</c> field: none | high. 0.17: the label/diagnosis word filter is gone (owner).
/// </summary>
public static class SayGuard
{
    public const string RiskField = "risk";
    public static readonly IReadOnlyList<string> RiskValues = ["none", "high"];
    public const string Kept = "kept", Rewritten = "rewritten", Distress = "distress";

    public static SayResult Check(MindMsg say, bool english = false)
    {
        if (say.T != "say") return new(say, Kept);
        if (string.Equals(say.Risk, "high", StringComparison.OrdinalIgnoreCase))
        {
            var r = Safety.Distress.Reply(english);
            return new(say with { Text = r.Text + "\n" + string.Join("\n", r.Help), Mood = "calm" }, Distress);
        }
        // Owner decision 2026-11-27 («لا ما بدي هي الحماية الغيها»): the output word filter (labels/diagnosis words →
        // a listening line) is removed for both brains. The prompt's rules (she never diagnoses or labels him, no
        // medicines, not a therapist) and the fixed distress handling above stay.
        return new(say, Kept);
    }
}
