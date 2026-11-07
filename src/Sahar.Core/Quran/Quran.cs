namespace Sahar.Core.Quran;

/// <summary>A reciter on the islamic.network audio CDN.</summary>
public sealed record Reciter(string Id, string Name);

/// <summary>
/// 📖 The surahs and reciters the Quran player offers. Recitations stream from the islamic.network CDN
/// (alquran.cloud: free for personal and educational streaming).
/// </summary>
public static class Quran
{
    public const string CdnBase = "https://cdn.islamic.network/quran/audio-surah/128/";

    public static IReadOnlyList<string> Surahs { get; } =
    [
        "الفاتحة", "البقرة", "آل عمران", "النساء", "المائدة", "الأنعام", "الأعراف", "الأنفال", "التوبة", "يونس",
        "هود", "يوسف", "الرعد", "إبراهيم", "الحجر", "النحل", "الإسراء", "الكهف", "مريم", "طه",
        "الأنبياء", "الحج", "المؤمنون", "النور", "الفرقان", "الشعراء", "النمل", "القصص", "العنكبوت", "الروم",
        "لقمان", "السجدة", "الأحزاب", "سبأ", "فاطر", "يس", "الصافات", "ص", "الزمر", "غافر",
        "فصلت", "الشورى", "الزخرف", "الدخان", "الجاثية", "الأحقاف", "محمد", "الفتح", "الحجرات", "ق",
        "الذاريات", "الطور", "النجم", "القمر", "الرحمن", "الواقعة", "الحديد", "المجادلة", "الحشر", "الممتحنة",
        "الصف", "الجمعة", "المنافقون", "التغابن", "الطلاق", "التحريم", "الملك", "القلم", "الحاقة", "المعارج",
        "نوح", "الجن", "المزمل", "المدثر", "القيامة", "الإنسان", "المرسلات", "النبأ", "النازعات", "عبس",
        "التكوير", "الانفطار", "المطففين", "الانشقاق", "البروج", "الطارق", "الأعلى", "الغاشية", "الفجر", "البلد",
        "الشمس", "الليل", "الضحى", "الشرح", "التين", "العلق", "القدر", "البينة", "الزلزلة", "العاديات",
        "القارعة", "التكاثر", "العصر", "الهمزة", "الفيل", "قريش", "الماعون", "الكوثر", "الكافرون", "النصر",
        "المسد", "الإخلاص", "الفلق", "الناس",
    ];

    public static IReadOnlyList<Reciter> Reciters { get; } =
    [
        new("ar.alafasy", "مشاري العفاسي"),
        new("ar.abdulbasitmurattal", "عبد الباسط عبد الصمد"),
        new("ar.husary", "محمود خليل الحصري"),
        new("ar.minshawi", "محمد صديق المنشاوي"),
        new("ar.mahermuaiqly", "ماهر المعيقلي"),
        new("ar.abdurrahmaansudais", "عبد الرحمن السديس"),
    ];

    /// <summary>The surah's name, 1–114.</summary>
    public static string SurahName(int surah) =>
        surah is >= 1 and <= 114 ? Surahs[surah - 1] : throw new ArgumentOutOfRangeException(nameof(surah), surah, "1–114");

    /// <summary>A reciter's name; an unknown id (a bad setting) is the first reciter.</summary>
    public static string ReciterName(string id) => (Reciters.FirstOrDefault(r => r.Id == id) ?? Reciters[0]).Name;

    /// <summary>The surah's audio: {base}/{reciter}/{surah}.mp3 (the CDN unless a test gives another base).</summary>
    public static Uri Url(string reciter, int surah, string? baseUrl = null) =>
        new($"{(baseUrl ?? CdnBase).TrimEnd('/')}/{reciter}/{surah}.mp3");
}
