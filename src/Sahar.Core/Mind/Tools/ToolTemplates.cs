using System.Globalization;
using System.Text.Json;
using Sahar.Core.Text;

namespace Sahar.Core.Mind.Tools;

/// <summary>
/// Her truthful lines about what really happened (spec §3.5 "confirmations from Core templates", as ChatReply does):
/// the engine fills <see cref="ToolOutcome.Summary"/> from these, the action log shows them, and the turn loop uses
/// them whenever the model's words would claim something that did not happen. Arabic-Indic digits; words to him
/// agree at render (Address.ToYou).
/// </summary>
public static class ToolTemplates
{
    public const string BadOutput = "ما فهمت عليك منيح… فيك تقولها بطريقة تانية؟";
    public const string Timeout = "عم فكّر كتير… جرّب كمان مرة؟";
    public const string Thinking = "عم فكّر…";
    public const string Failed = "ما زبطت معي هلق.";
    public const string AskedNo = "ما غيّرت شي.";
    public const string NothingDone = "ما عملت شي لسا.";
    public const string Unavailable = "هاد مو موجود عندي هلق.";
    public const string NoAnswer = "ما جاوبتني، فما غيّرت شي.";

    private static readonly Dictionary<string, string> Games = new(StringComparer.Ordinal)
    {
        ["chase"] = "الحقني", ["hide"] = "غمّضلي", ["ball"] = "الطابة", ["stars"] = "لقط النجوم",
    };

    /// <summary>What a settings key is called in her words (the settings app's own names).</summary>
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["activity"] = "نشاطي", ["size"] = "حجمي", ["innerStars"] = "النجوم اللي جواتي", ["autoMood"] = "مزاجي لحالي",
        ["playsAlone"] = "لعبي لحالي", ["appearance"] = "الفاتح والغامق", ["menuHeader"] = "راس القائمة", ["menuPlace"] = "مكان القائمة",
        ["awarenessEnabled"] = "تعليقي على شغلك", ["weatherEnabled"] = "الطقس", ["language"] = "لغتي", ["menuShortcut"] = "اختصار القائمة",
        ["menuOpen"] = "كيف بتفتح القائمة", ["menuItems"] = "شو بالقائمة", ["prayerMethod"] = "طريقة حساب الصلاة",
        ["prayerHanafiAsr"] = "العصر الحنفي", ["prayerReminderOn"] = "تذكير الصلاة", ["prayerReminderMinutes"] = "وقت تذكير الصلاة",
        ["quranReciter"] = "القارئ", ["userName"] = "اسمك", ["userGender"] = "كيف بحكي معك",
    };

    public static string Num(int n) => Digits.ToArabicIndic(n.ToString(CultureInfo.InvariantCulture));

    /// <summary>The line for a call that ran (data = a read tool's result, as the engine reports it).</summary>
    public static string Done(ToolCall call, string? data = null)
    {
        var a = call.Args;
        string? S(string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        int? I(string k) => a.ValueKind == JsonValueKind.Object && a.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
        return call.Tool switch
        {
            "timer.start" => $"شغّلت مؤقت {Num(I("minutes") ?? 0)} دقيقة",
            "timer.cancel" => "وقّفت المؤقت",
            "reminder.set" when I("inMinutes") is { } m => $"رح ذكّرك بعد {Num(m)} دقيقة",
            "reminder.set" => $"رح ذكّرك الساعة {Digits.ToArabicIndic(S("at") ?? "")}",
            "note.save" => "حفظت ملاحظة",
            "note.open" => "فتحتلك الملاحظات",
            "hush.on" when I("minutes") is { } m => $"رح اسكت {Num(m)} دقيقة",
            "hush.on" => "سكتّ لحد ما تقلّي",
            "hush.off" => "رجعت احكي",
            "sleep.hide" => $"رح غيب {Num(I("minutes") ?? 60)} دقيقة",
            "game.start" => $"بلّشنا {Games.GetValueOrDefault(S("game") ?? "", "لعبة")}",
            "games.open" => "فتحت الألعاب",
            "move.play" => S("move") switch { "dance" => "رقصتلك", "stretch" => "تمطّيت", _ => "نطّيت" },
            "prayer.open" => "فتحت أوقات الصلاة",
            "music.control" => S("op") switch
            {
                "pause" => "وقّفت الموسيقى",
                "next" => "حطّيت الغنية الجاية",
                "prev" => "رجعت للغنية اللي قبل",
                _ => "كبست تشغيل/إيقاف",
            },
            "quran.open" => "فتحت القرآن",
            "quran.stop" => "وقّفت القرآن",
            "settings.set" => Setting(S("key"), a),
            "memory.remember" => "سجّلتها لتأكّدها إنت بـ«عنك»",
            "goal.add" => "حطّيت الهدف لتأكّده إنت بـ«عنك»",
            "shelf.open" => "فتحت رفّي",
            "shelf.tidy" => "رتّبت رفّي",
            "shelf.clear" => "فضّيت رفّي",
            "prayer.next" or "music.now" or "weather.now" or "claude.status" => data is null ? "شفت" : Digits.ToArabicIndic(data),
            _ => "عملتها",
        };
    }

    /// <summary>Her Ask question («إي» / «لا»), e.g. «شغّل الطقس؟».</summary>
    public static string Question(ToolCall call)
    {
        var a = call.Args;
        if (call.Tool == "settings.set" && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
        {
            var key = k.GetString()!;
            bool? on = a.TryGetProperty("value", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            var name = Keys.GetValueOrDefault(key, key);
            return on switch { true => $"شغّل {name}؟", false => $"طفي {name}؟", _ => $"غيّر {name}؟" };
        }
        var line = Done(call);
        return call.Tool switch
        {
            "shelf.clear" => "فضّي رفّي؟",
            "hush.on" => "بسكت شوي؟",
            "sleep.hide" => "بغيب شوي؟",
            "quran.open" => "بفتحلك القرآن؟",
            "note.open" => "بفتحلك الملاحظات؟",
            _ => $"بعمل هيك؟ ({line})",
        };
    }

    private static string Setting(string? key, JsonElement a)
    {
        var v = a.ValueKind == JsonValueKind.Object && a.TryGetProperty("value", out var x) ? x : default;
        if (key == "size" && v.ValueKind == JsonValueKind.String)
            return v.GetString()?.ToLowerInvariant() switch { "large" => "كبّرت حجمي", "small" => "صغّرت حجمي", _ => "رجّعت حجمي الوسط" };
        var name = key is null ? "إعداد" : Keys.GetValueOrDefault(key, key);
        return v.ValueKind switch
        {
            JsonValueKind.True => $"شغّلت {name}",
            JsonValueKind.False => $"طفيت {name}",
            _ => $"غيّرت {name}",
        };
    }
}
