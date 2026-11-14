using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Mind;

/// <summary>What happened to a say: kept, rewritten (a label or diagnosis word), or replaced by the distress reply.</summary>
public sealed record SayResult(MindMsg Msg, string Kind)
{
    public string? Text => Msg.Text;
}

/// <summary>
/// SEAM (0.14, the last check on her words; the Mind host runs it on every say before it leaves): the model's
/// <c>risk: high</c> becomes B's fixed reviewed reply (<see cref="Safety.Distress.Reply"/>), and a say that names a
/// diagnosis or a label about him (spec §7.4 "she never") is rewritten to a plain listening line. The decision schema
/// (stream C) carries the optional <c>risk</c> field: none | high.
/// </summary>
public static class SayGuard
{
    public const string RiskField = "risk";
    public static readonly IReadOnlyList<string> RiskValues = ["none", "high"];
    public const string Kept = "kept", Rewritten = "rewritten", Distress = "distress";

    public const string SafeArabic = "سامعتك، وأنا هون معك. بدك تحكيلي أكتر شو عم تحس؟";
    public const string SafeEnglish = "I hear you, and I'm here with you. Do you want to tell me more about how you feel?";

    /// <summary>Labels and diagnoses she never puts on him (folded with B's Phrases; * = a stem).</summary>
    private static readonly string[] Labels = Phrases.FoldAll([
        "اكتئاب", "مكتئب*", "عندك قلق", "اضطراب*", "ثنائي القطب", "وسواس قهري", "فصام", "صدمه نفسيه", "مرض نفسي", "مريض نفسي",
        "تشخيص*", "بتحتاج دوا", "مضاد اكتئاب", "adhd", "ocd", "ptsd", "bipolar", "depress*", "anxiety disorder", "disorder*",
        "diagnos*", "trauma", "schizo*", "antidepressant*", "you need medication",
    ]);

    public static SayResult Check(MindMsg say, bool english = false)
    {
        if (say.T != "say") return new(say, Kept);
        if (string.Equals(say.Risk, "high", StringComparison.OrdinalIgnoreCase))
        {
            var r = Safety.Distress.Reply(english);
            return new(say with { Text = r.Text + "\n" + string.Join("\n", r.Help), Mood = "calm" }, Distress);
        }
        var folded = Phrases.Fold(say.Text);
        if (folded.Length > 0 && Phrases.Any(folded, Labels))
            return new(say with { Text = english ? SafeEnglish : SafeArabic }, Rewritten);
        return new(say, Kept);
    }
}
