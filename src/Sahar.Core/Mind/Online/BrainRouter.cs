using Sahar.Core.Mind.Safety;

namespace Sahar.Core.Mind.Online;

/// <summary>Which brain answers a turn.</summary>
public enum BrainChoice { Local, Online }

/// <summary>What the router reads (spec 0.17 §5). <see cref="Toggle"/>: the chat window's «أونلاين»/«محلي» (online | local;
/// null = online). <see cref="AnyOnline"/>: a provider in the chain has a key and is not out for today.</summary>
public sealed record RouteInput(string Source, bool Window, string? Toggle, bool LocalOnly, bool Consent, string Line, bool AnyOnline, bool Distress = false);

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
        // Review I2: a distress cue gets the fixed reply from the local runner, which also keeps her quiet a day after.
        if (r.Distress) return (BrainChoice.Local, "distress");
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
    // Strong: asking her to DO a thing (her tool verbs, and the objects only a tool makes: a timer, a reminder, a note).
    private static readonly string[] ActionWords = Phrases.FoldAll([
        "مؤقت*", "موقت*", "تايمر", "منبه", "ذكريني*", "ذكرني*", "تذكير*", "نبهيني*", "فيقيني*", "صحيني*",
        "ملاحظه*", "سجلي*", "احفظي*", "دوني*", "سمعيني*",
        "العبي*", "نلعب", "غميضه", "الحقيني", "الحقني", "ارقصي", "نطي", "تمطي",
        "اسكتي", "اسكت", "نامي", "اختفي", "تخبي*", "علي الصوت", "وطي الصوت",
        "شغلي*", "شغليلي*", "وقفي*", "وقفيلي*", "طفي*", "طفيلي*", "سكري*", "سكريلي*", "افتحي*", "افتحيلي*", "حطيلي*", "حطلي*",
        "كمليلي", "رجعيلي", "الغي*", "امسحي*", "انسخي*",
        "timer", "remind*", "alarm", "take a note", "save a note", "let's play", "play with me", "play some", "dance", "hush",
        "be quiet", "go to sleep", "open the", "open my", "turn on", "turn off", "set a", "volume", "next song",
    ]);

    // Weak: a topic her tools also cover (the weather, music, prayer, the Quran…): an action unless he asks her to write
    // or explain about it (review: «اكتبلي شعر عن المطر» is words, not the weather card).
    private static readonly string[] TopicWords = Phrases.FoldAll([
        "موسيقى", "موسيقا", "غنيه", "اغنيه", "اغاني", "صوت", "صلاه", "اذان", "طقس", "مطر", "شتي", "حراره", "قرآن", "قران", "سوره",
        "تلاوه", "لعبه", "نسخ", "الحافظه", "الكليب بورد", "بطاريه",
        "music", "song", "prayer", "adhan", "weather", "quran", "surah", "clipboard", "battery",
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
        if (f.Length == 0) return false;
        if (Phrases.Any(f, ActionWords)) return true;
        return Phrases.Any(f, TopicWords) && !Phrases.Any(f, WritingWords);
    }

    public static bool Writing(string? line)
    {
        if (line is null) return false;
        if (line.Trim().Length >= LongLine) return true;
        var f = Phrases.Fold(line);
        return f.Length > 0 && Phrases.Any(f, WritingWords);
    }
}
