using System.Globalization;
using Sahar.Core.Text;

namespace Sahar.Core.Mind.Runtime;

/// <summary>Her lines about her brain (bubble and header), in her voice, Arabic-Indic digits. Words to him go through
/// the engine's <c>Address</c> rule.</summary>
public static class RuntimeText
{
    public const string Yes = "إي، نزّليه";
    public const string Later = "بعدين";
    public const string No = "لا";
    public const string Checking = "عم شوف إذا Hugging Face بيفتح عندك…";
    public const string Blocked = "ما قدرت وصل لـ Hugging Face من نتّك. إذا عندك VPN شغّله، وارجع قلّي «كمّلي» من «النماذج». ما نزّلت شي.";
    public const string Started = "بلّشت نزّل عقلي… بخبّرك وين صرت.";
    public const string Paused = "وقّفت التنزيل. بكمّل من نفس المطرح.";
    public const string Verifying = "خلص التنزيل، عم اتأكّد منه…";
    public const string Done = "هلق صرت فكّر لحالي ✨";
    public const string Mismatch = "الملف وصل خربان، فمسحته. جرّب كمان مرة؟";
    public const string Failed = "ما زبط التنزيل هلق. بكمّل من نفس المطرح لما تقلي.";

    /// <summary>4,977,171,584 B → «٤٫٩٨» (GB, two decimals, trailing zeros dropped).</summary>
    public static string Gb(long bytes)
    {
        var s = (bytes / 1e9).ToString("0.##", CultureInfo.InvariantCulture);
        return Digits.ToArabicIndic(s).Replace('.', '٫');
    }

    public static string Ask(long bytes) =>
        $"صار فيني فكّر وإحكي معك من جوّا جهازك، بلا نت وبلا ما يطلع شي لبرّا. بدي نزّل عقلي: حجمه تقريباً {Gb(bytes)} غيغا. بنزّله هلق؟";

    public static string Progress(int percent) => $"نزّلت {Digits.ToArabicIndic(percent.ToString(CultureInfo.InvariantCulture))}٪ من عقلي.";

    public static string NoDisk(long missing) => $"ما في مكان كفاية عالجهاز: لازمني كمان {Gb(missing)} غيغا.";

    /// <summary>The quarter (25/50/75) just passed, once each; null otherwise.</summary>
    public static int? Milestone(long done, long total, int lastSaid)
    {
        if (total <= 0) return null;
        int q = (int)(done * 4 / total) * 25;
        return q is >= 25 and < 100 && q > lastSaid ? q : null;
    }
}
