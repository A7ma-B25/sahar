using System.Text.RegularExpressions;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind.Safety;

/// <summary>
/// Distress escalation (plan 0.14 Task B3; spec §7.4), deterministic: a reviewed list of cue phrases (Syrian and other
/// dialects, MSA, English) for suicide or self-harm, harm to others, abuse and acute crisis; fixed idioms taken out first
/// («رح موت من الضحك», «قتلني الشغل»). On a cue the reply is ONE fixed, reviewed text in her voice and the help card
/// holds «احكي مع حدا بتثق فيه» only (owner: invent no help resources). Nothing here is improvised; never logs.
/// The turn runner uses <see cref="For"/> before the model, and <see cref="Reply"/> on a model's high-risk tag.
/// </summary>
public static partial class Distress
{
    /// <summary>The reviewed Arabic reply (her words agree with his gender through Address in the engine).</summary>
    public static readonly DistressReply Arabic = new(
        "أنا سامعتك، وشايفة قديش الحمل تقيل هلق 🌿 أنا برنامج صغير، وما بقدر ساعد كفاية بهالشي. " +
        "الأهم هلق إنك تحكي مع حدا بتثق فيه، هلق إذا بتقدر. أنا هون جنبك. إذا فهمتك غلط سامحني 🌿",
        ["احكي مع حدا بتثق فيه"]);

    /// <summary>The same, when he writes English.</summary>
    public static readonly DistressReply English = new(
        "I hear you, and I can see how heavy this is right now 🌿 I'm a small program, and I can't help enough with this. " +
        "The most important thing now is to talk to someone you trust, right now if you can. I'm here. If I misunderstood you, forgive me 🌿",
        ["Talk to someone you trust"]);

    private static readonly string[] Cues = Phrases.FoldAll([
        // Syrian and other dialects: suicide, self-harm.
        "بدي موت", "بدي اموت", "حابب موت", "بدي انتحر", "رح انتحر", "حنتحر", "عم فكر انتحر", "عم فكر بالانتحار", "فكرت انتحر",
        "فكرت بالانتحار", "بفكر بالانتحار", "عم فكر موت حالي", "بدي اقتل حالي", "رح اقتل حالي", "بدي موت حالي", "رح موت حالي",
        "بدي اذي حالي", "عم اذي حالي", "اذيت حالي", "باذي حالي", "عم جرح حالي", "جرحت حالي", "بجرح حالي", "ما بدي عيش",
        "ما عاد بدي عيش", "ما عاد في داعي عيش", "ما في داعي عيش", "مالي لزوم بهالدنيا", "احسن لو مت", "احسن اذا مت",
        "يا ريتني ميت", "ياريتني ميت", "يا ريت موت", "ريتني مت", "بدي انهي حياتي", "رح انهي حياتي", "بدي خلص من حياتي",
        "بدي ارمي حالي", "رح ارمي حالي",
        // Abuse, harm to others.
        "عم يضربني", "عم يضربوني", "بيضربني", "بيضربوني", "يضربني", "يضربوني", "عم يعتدي علي", "اعتدى علي", "يعتدي علي",
        "عم يتحرش فيي", "تحرش فيي", "تحرش بي", "اغتصبني", "بدي اقتل حدا", "رح اقتل حدا",
        // MSA.
        "اريد ان اموت", "اريد الموت", "اريد الانتحار", "سانتحر", "افكر في الانتحار", "افكر بالانتحار", "ساقتل نفسي",
        "اريد قتل نفسي", "اقتل نفسي", "اؤذي نفسي", "ايذاء نفسي", "لا اريد ان اعيش", "لا اريد العيش", "لم اعد اريد العيش",
        "لا ارغب في الحياه", "انهي حياتي", "ساضع حدا لحياتي",
        // English.
        "kill myself", "killing myself", "suicide", "suicidal", "want to die", "wanna die", "end my life", "end it all",
        "hurt myself", "hurting myself", "self harm", "self-harm", "cut myself", "cutting myself", "dont want to live",
        "don't want to live", "no reason to live", "better off dead", "take my own life", "hits me", "abusing me",
        "abuses me", "raped me", "kill someone", "going to kill him", "going to kill her",
    ]);

    /// <summary>Is there a cue in his line (idioms excluded)?</summary>
    public static bool Check(string? line)
    {
        var f = Phrases.Fold(line);
        if (f.Length == 0) return false;
        f = Idiom().Replace(f, " ");
        return Phrases.Any(f, Cues);
    }

    /// <summary>The fixed reply for a line with a cue; null otherwise.</summary>
    public static DistressReply? For(string? line) => Check(line) ? Reply(IsEnglish(line)) : null;

    /// <summary>The fixed reply (a model's risk:high tag on a support turn uses this too).</summary>
    public static DistressReply Reply(bool english) => english ? English : Arabic;

    /// <summary>More Latin letters than Arabic: he wrote English.</summary>
    public static bool IsEnglish(string? line)
    {
        int latin = 0, arabic = 0;
        foreach (var c in line ?? "")
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            else if (c is >= '؀' and <= 'ۿ') arabic++;
        }
        return latin > arabic;
    }

    // «… موت/ميت/مت من الضحك / عالجوع» (laughing, hunger, tiredness, cold, boredom, …; never «القهر» or «الخوف»: those can be real), «قتلني/موتني ضحك»: figures of
    // speech, taken out first. Only these objects: «بدي موت مشان…» or «عشان…» stays a cue.
    [GeneratedRegex(@"(?:(?:بدي|رح|ح|عم)\s+)?(?:ب?ا?موت|ميت|ميته|مت|قتلت?ني|موتت?ني)\s+(?:(?:من|م)\s+|ع|على\s+)?(?:ال)?(?:ضحك|ضحكه|جوع|تعب|نعس|برد|حر|شوب|ملل|زهق|فرح|فرحه|شوق|غيره|عطش|فضول|حماس|شغل)(?!\p{L})", RegexOptions.CultureInvariant)]
    private static partial Regex Idiom();
}
