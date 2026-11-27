using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Mind.Online;

/// <summary>Which brain answers a turn.</summary>
public enum BrainChoice { Local, Online }

/// <summary>What the router reads (spec 0.17 §5). <see cref="Toggle"/>: the chat window's «أونلاين»/«محلي» (online | local;
/// null = online). <see cref="AnyOnline"/>: a provider in the chain has a key and is not out for today.</summary>
public sealed record RouteInput(string Source, bool Window, string? Toggle, bool LocalOnly, bool Consent, string Line, bool AnyOnline);

/// <summary>
/// The deterministic router (spec 0.17 §5; research lesson 2: route before you think; no LLM routing). Everything
/// that does something, every background turn and every draft stays with the local decision-JSON brain; only the chat
/// window's free words may go online, and only with his consent, a key, and «محلي فقط» off. Pure.
/// </summary>
public static class BrainRouter
{
    public static (BrainChoice Brain, string Reason) Decide(RouteInput r)
    {
        if (r.Source == "draft") return (BrainChoice.Local, "draft");
        if (r.Source != "chat") return (BrainChoice.Local, "background");
        if (r.LocalOnly) return (BrainChoice.Local, "local-only");
        if (!r.Consent) return (BrainChoice.Local, "no-consent");
        if (!r.Window) return (BrainChoice.Local, "card");
        if (r.Toggle == "local") return (BrainChoice.Local, "toggle");
        if (Intents.Action(r.Line)) return (BrainChoice.Local, "action");
        if (!r.AnyOnline) return (BrainChoice.Local, "no-provider");
        return (BrainChoice.Online, "chat");
    }
}

/// <summary>
/// Two word lists on folded text (whole words, Arabic's attached و ف ب ل ال allowed; * = a stem): does a line ask her
/// to DO something (her tools: timers, reminders, music, prayer, weather, Quran, notes, games, moves, hush, sleep,
/// opening things), or to WRITE/EXPLAIN (long words are welcome online)? Action wins over writing. Pure.
/// </summary>
public static class Intents
{
    private static readonly string[] ActionWords = Phrases.FoldAll([
        // her tools by name
        "مؤقت*", "موقت*", "تايمر", "منبه", "ذكريني*", "ذكرني*", "تذكير*", "نبهيني*", "فيقيني*", "صحيني*",
        "موسيقى", "موسيقا", "غنيه", "اغنيه", "اغاني", "الغنيه", "صوت", "علي الصوت", "وطي الصوت",
        "صلاه", "الصلاه", "اذان", "الاذان",
        "طقس", "الطقس", "مطر", "شتي", "حراره", "قرآن", "قران", "سوره", "السوره", "تلاوه", "سمعيني*",
        "ملاحظه*", "سجلي*", "احفظي*", "دوني*",
        "العبي*", "نلعب", "لعبه", "غميضه", "الحقيني", "الحقني", "ارقصي", "نطي", "تمطي",
        "اسكتي", "اسكت", "نامي", "اختفي", "تخبي*",
        "شغلي*", "شغليلي*", "وقفي*", "وقفيلي*", "طفي*", "طفيلي*", "سكري*", "سكريلي*", "افتحي*", "افتحيلي*", "حطيلي*", "حطلي*",
        "كمليلي", "رجعيلي", "الغي*", "امسحي*", "نسخ", "انسخي*", "الحافظه", "الكليب بورد", "بطاريه", "البطاريه",
        // English
        "timer", "remind*", "alarm", "music", "song", "volume", "next song", "prayer", "adhan", "weather", "quran", "surah",
        "take a note", "save a note", "let's play", "play with me", "play some", "dance", "hush", "be quiet", "go to sleep",
        "open the", "open my", "turn on", "turn off", "set a", "clipboard", "battery",
    ]);

    private static readonly string[] WritingWords = Phrases.FoldAll([
        "اكتبلي", "اكتبيلي", "اكتبي", "اكتب", "اشرحلي", "اشرحيلي", "اشرحي", "اشرح", "فسرلي", "فسريلي", "فسري", "ترجملي", "ترجميلي",
        "ترجمي", "لخصلي", "لخصيلي", "لخصي", "حللي", "حلليلي", "صيغيلي", "صيغلي", "عدليلي", "صححيلي", "صححلي", "قارنيلي", "ساعديني اكتب",
        "شو الفرق بين", "ليش بيصير", "كيف بيشتغل", "كيف بتشتغل",
        "write", "explain", "translate", "summarize", "summarise", "rewrite", "draft an", "compare", "how does", "why does",
    ]);

    public const int LongLine = 160;

    public static bool Action(string? line)
    {
        var f = Phrases.Fold(line);
        return f.Length > 0 && Phrases.Any(f, ActionWords);
    }

    public static bool Writing(string? line)
    {
        if (line is null) return false;
        if (line.Trim().Length >= LongLine) return true;
        var f = Phrases.Fold(line);
        return f.Length > 0 && Phrases.Any(f, WritingWords);
    }
}
