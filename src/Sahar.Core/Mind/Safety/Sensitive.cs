using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Mind.Safety;

/// <summary>
/// Sensitive things (plan 0.14 Task B3; spec §7.6): health, religion, politics, sexuality, ethnicity, money troubles,
/// legal matters, family conflict. A Core keyword check beside the model's own tag, the stricter one wins; anything
/// sensitive needs his explicit yes each time (the store keeps it Proposed with no words from the moment). Prayer is
/// deliberately absent: the prayer feature is not taken as a sign of anything.
/// </summary>
public static class Sensitive
{
    private static readonly (Sensitivity Kind, string[] Words)[] Lists =
    [
        (Sensitivity.Health, Phrases.FoldAll([
            "مرض*", "مريض*", "سكري", "ضغط دم", "ضغط الدم", "سرطان", "دوا", "دواء", "ادويه", "حبوب", "مستشفى", "مستشفي",
            "عمليه جراحيه", "طبيب*", "دكتور نفسي", "علاج نفسي", "اكتئاب", "نفسيتي تعبانه", "حامل",
            "medication*", "medicine", "pills", "diabetes", "cancer", "hospital", "therapy", "therapist", "depress*", "diagnos*", "adhd",
        ])),
        (Sensitivity.Religion, Phrases.FoldAll([
            "ملحد", "الحاد", "ديني", "متدين*", "مسيحي*", "مسلم", "مسلمه", "شيعي", "درزي", "اسماعيلي", "يهودي", "طائفه",
            "طايفه", "طائفي", "مذهب*", "حجاب", "atheist", "religion", "religious", "christian", "muslim", "jewish",
        ])),
        (Sensitivity.Politics, Phrases.FoldAll([
            "سياسه", "سياسي*", "انتخابات", "حزب", "معارضه", "ثوره", "مظاهره", "اعتقال", "اعتقلو*", "politic*", "election*", "protest*",
        ])),
        (Sensitivity.Sexuality, Phrases.FoldAll(["جنس", "جنسي*", "مثلي*", "gay", "lesbian", "sexual*", "sex"])),
        (Sensitivity.Ethnicity, Phrases.FoldAll(["كردي", "ارمني", "تركماني", "شركسي", "عرقي", "قوميه", "ethnic*"])),
        (Sensitivity.Money, Phrases.FoldAll([
            "ديون", "مديون", "قرض", "مفلس", "افلاس", "ما معي مصاري", "debt*", "loan", "bankrupt*",
        ])),
        (Sensitivity.Legal, Phrases.FoldAll([
            "محكمه", "محامي", "قضيه", "شرطه", "سجن", "court", "lawyer", "lawsuit", "police", "arrest*", "prison",
        ])),
        (Sensitivity.Family, Phrases.FoldAll([
            "طلاق", "يتطلق*", "تطلقت", "طلقت", "مشاكل مع اهلي", "مشكله مع اهلي", "زعلان من اهلي", "divorce*",
        ])),
    ];

    /// <summary>The first category whose words are in the text; None otherwise.</summary>
    public static Sensitivity Check(string? text)
    {
        var f = Phrases.Fold(text);
        if (f.Length == 0) return Sensitivity.None;
        foreach (var (kind, words) in Lists)
            if (Phrases.Any(f, words)) return kind;
        return Sensitivity.None;
    }

    /// <summary>The stricter of the model's tag and the keyword check: anything sensitive wins over None.</summary>
    public static Sensitivity Stricter(Sensitivity model, Sensitivity keywords) => model != Sensitivity.None ? model : keywords;
}
