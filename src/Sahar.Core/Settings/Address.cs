using System.Text.RegularExpressions;

namespace Sahar.Core.Settings;

/// <summary>Who she talks to: her words to you agree with it (Syrian Arabic, "اكتب" / "اكتبي").</summary>
public enum Gender { Unknown, Male, Female }

/// <summary>
/// Her text is written to a man (Arabic's default); to a woman the verbs and words that address you take their
/// feminine form. Only whole words change, so her words about herself and yours to her stay as they are.
/// </summary>
public static partial class Address
{
    private static readonly (string Male, string Female)[] Words =
    [
        ("ما وقّفها", "ما توقّفيها"), ("بتسحبها", "بتسحبيها"), ("سحبتها", "سحبتيها"), ("نسخته", "نسختيه"),
        ("تشغّل", "تشغّلي"), ("وقّف", "وقّفي"), ("جرّب", "جرّبي"), ("غيّر", "غيّري"), ("اختار", "اختاري"),
        ("اكتب", "اكتبي"), ("امسح", "امسحي"), ("افتح", "افتحي"), ("خود", "خدي"),
        ("رجعت", "رجعتي"), ("نسخت", "نسختي"), ("كبست", "كبستي"),
        ("مسكتني", "مسكتيني"), ("لقيتني", "لقيتيني"), ("الحقني", "الحقيني"), ("بتقدر", "بتقدري"), ("غمّض", "غمّضي"),
        ("اشلف", "اشلفي"),
    ];

    private static readonly Dictionary<string, string> Feminine = Words.ToDictionary(w => w.Male, w => w.Female);

    // Longest first, and only as whole words: a letter right before or after means another word.
    private static readonly Regex Pattern = new(
        $"(?<!\\p{{L}})({string.Join('|', Words.Select(w => w.Male).OrderByDescending(w => w.Length).Select(Regex.Escape))})(?!\\p{{L}})",
        RegexOptions.CultureInvariant);

    /// <summary>Who she is talking to now (set from settings at start and when it changes).</summary>
    public static Gender You { get; set; }

    /// <summary>Your name, if you told her.</summary>
    public static string? Name { get; set; }

    public static string For(Gender to, string text) =>
        to == Gender.Female && !string.IsNullOrEmpty(text) ? Pattern.Replace(text, m => Feminine[m.Value]) : text;

    /// <summary>The text for whoever she is talking to now.</summary>
    public static string ToYou(string text) => For(You, text);

    /// <summary>A typed name: trimmed, 20 characters at most, none if empty.</summary>
    public static string? CleanName(string typed)
    {
        var t = typed.Trim();
        return t.Length == 0 ? null : t.Length > 20 ? t[..20].TrimEnd() : t;
    }
}
