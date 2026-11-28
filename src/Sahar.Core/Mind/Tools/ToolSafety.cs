using System.Text;
using Sahar.Core.Chat;

namespace Sahar.Core.Mind.Tools;

/// <summary>
/// 0.19 D4: a tool whose name or description says it deletes, sends something in his name, or touches money is
/// sensitive — it asks every time and can never be "always allowed". English verbs are matched as words of the
/// snake/kebab/camel-case name and of the description (with -s/-es/-ed/-ing); Arabic stems inside the normalized text.
/// Over-cautious by design; he can also mark any tool himself. Pure.
/// </summary>
public static class ToolSafety
{
    private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
    {
        "delete", "remove", "erase", "destroy", "drop", "wipe", "purge", "send", "post", "publish", "reply", "forward", "share",
        "tweet", "pay", "buy", "purchase", "order", "transfer", "withdraw", "charge", "checkout", "refund",
    };

    private static readonly string[] Arabic = ["حذف", "مسح", "بعت", "ارسل", "ارسال", "انشر", "نشر", "دفع", "اشتري", "شراء", "تحويل", "حولي"];

    public static bool IsSensitive(string name, string? description)
    {
        foreach (var text in (string?[])[name, description])
        {
            if (string.IsNullOrEmpty(text)) continue;
            foreach (var word in Words(text))
                if (Verbs.Contains(word) || Stems(word).Any(Verbs.Contains)) return true;
            var n = ChatCommands.Normalize(text);
            foreach (var a in Arabic)
                if (n.Contains(a, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static IEnumerable<string> Stems(string w)
    {
        if (w.EndsWith("ing", StringComparison.Ordinal) && w.Length > 5) { yield return w[..^3]; yield return w[..^3] + "e"; }
        if (w.EndsWith("ed", StringComparison.Ordinal) && w.Length > 4) { yield return w[..^2]; yield return w[..^1]; }
        if (w.EndsWith("es", StringComparison.Ordinal) && w.Length > 4) yield return w[..^2];
        if (w.EndsWith('s') && w.Length > 3) yield return w[..^1];
    }

    /// <summary>ASCII words, split at non-letters and at a lower→upper case change (camelCase), lowercased.</summary>
    private static IEnumerable<string> Words(string text)
    {
        var b = new StringBuilder();
        char prev = ' ';
        foreach (var c in text)
        {
            bool letter = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
            if (!letter || (char.IsUpper(c) && char.IsLower(prev)))
            {
                if (b.Length > 0) { yield return b.ToString(); b.Clear(); }
            }
            if (letter) b.Append(char.ToLowerInvariant(c));
            prev = c;
        }
        if (b.Length > 0) yield return b.ToString();
    }
}
