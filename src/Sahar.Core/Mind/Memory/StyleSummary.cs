using System.Globalization;
using Sahar.Core.Chat;

namespace Sahar.Core.Mind.Memory;

/// <summary>
/// «طريقتك بالحكي» (spec §7.1): his style card in plain words, worked out in Core from his samples — sentence length,
/// dialect, the words he keeps using, emoji. The words come from his own samples only; nothing is guessed by the model.
/// </summary>
public static class StyleSummary
{
    private static readonly HashSet<string> Levantine = new(StringComparer.Ordinal)
    {
        "شو", "هلق", "هيك", "كتير", "منيح", "منيحة", "بدي", "بدك", "لسا", "يلا", "تمام", "شلون", "كيفك", "ليش", "هاد", "هاي",
        "عم", "مو", "مش", "هلأ", "شي", "قديش", "وين", "خلص", "ماشي", "حبيبي", "بكرا", "منشان", "كمان", "بس",
    };

    private static readonly HashSet<string> Msa = new(StringComparer.Ordinal)
    {
        "سوف", "لماذا", "ذلك", "هذا", "هذه", "الذي", "التي", "ماذا", "اريد", "جدا", "لكن", "ايضا", "حسنا", "كيف", "ليس",
    };

    // Too common to say anything about him.
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "من", "على", "في", "انا", "انت", "انتي", "ما", "لا", "و", "يا", "هو", "هي", "عن", "مع", "ان", "او", "اذا", "كان",
        "شو", "عم", "بس", "شي", "هاد", "هاي", "مو", "مش", "the", "a", "to", "and", "i", "you", "is", "it", "of", "in",
    };

    public static StyleCard Card(IReadOnlyList<SampleRow> samples)
    {
        int n = samples.Count;
        int imports = samples.Count(s => s.Source == LearnSource.Imports), examples = samples.Count(s => s.Source == LearnSource.Examples);
        if (n == 0) return new StyleCard("", 0, imports, examples);

        var words = samples.Select(s => ChatCommands.Normalize(s.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', '،', ',', '!', '؟', '?', '«', '»', '"', ':')).Where(w => w.Length > 0).ToList()).ToList();
        double avg = words.Average(w => w.Count);
        var parts = new List<string> { avg <= 6 ? "جملك قصيرة" : avg <= 14 ? "جملك متوسطة" : "جملك طويلة" };

        int arabic = 0, latin = 0;
        foreach (var s in samples)
            foreach (var c in s.Text)
            {
                if (c is >= '؀' and <= 'ۿ') arabic++;
                else if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            }
        if (latin > arabic * 2)
            parts.Add(samples.Any(s => s.Text.Any(c => c is '3' or '7' or '2')) ? "بتكتب عربي بأحرف إنكليزية" : "بتكتب إنكليزي");
        else
        {
            int lev = words.Sum(w => w.Count(Levantine.Contains)), msa = words.Sum(w => w.Count(Msa.Contains));
            parts.Add(lev >= msa ? "شامي" : "أقرب للفصحى");
        }

        // Words he keeps using: in at least three samples (and a tenth of them), most used first, two at most.
        int need = Math.Max(3, (int)Math.Ceiling(n * 0.1));
        var flat = words.SelectMany(w => w.Distinct()).Where(w => w.Length >= 2 && !Stop.Contains(w)).ToList();
        var favourites = flat.GroupBy(w => w).Where(g => g.Count() >= need)
            .OrderByDescending(g => g.Count()).ThenBy(g => flat.IndexOf(g.Key)).Take(2).Select(g => $"«{g.Key}»").ToList();
        if (favourites.Count > 0) parts.Add("بتحب " + string.Join(" و", favourites));

        int withEmoji = samples.Count(s => HasEmoji(s.Text));
        parts.Add(withEmoji == 0 ? "وبلا إيموجي" : withEmoji * 10 < n * 3 ? "وإيموجي قليل" : "وإيموجي كتير");
        return new StyleCard(string.Join("، ", parts) + ".", n, imports, examples);
    }

    private static bool HasEmoji(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            int cp = char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s, i) : s[i];
            if (cp is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF) return true;
            if (char.IsSurrogatePair(s, i)) i++;
        }
        return false;
    }
}
