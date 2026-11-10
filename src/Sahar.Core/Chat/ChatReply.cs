using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Sahar.Core.Text;

namespace Sahar.Core.Chat;

/// <summary>What the reply needs to know: the next prayer (null = no city yet) and how many greetings came before.</summary>
public sealed record ChatContext((Sahar.Core.Prayer.Prayer Prayer, TimeSpan Left)? NextPrayer, int Turn);

/// <summary>Her reply to a chat line (spec §5.1), in her voice, Arabic-Indic digits, agreeing with who she talks to.</summary>
public static class ChatReply
{
    public const string Learning = "سمعتك! لسا عم اتعلّم الحكي، وقريباً بجاوبك على كل شي.";

    private static readonly string[] Greetings = ["منيحة! وإنت كيفك؟", "أهلين! اشتقتلك.", "هلا! أنا هون."];

    public static string For(ChatCommand c, ChatContext ctx, Gender to) => Address.For(to, Line(c, ctx));

    private static string Line(ChatCommand c, ChatContext ctx) => c.Kind switch
    {
        ChatKind.Empty => "",
        ChatKind.Timer when c.OutOfRange || c.Minutes is null => "بقدر شغّل مؤقت من دقيقة لـ ٦٠٠ دقيقة.",
        ChatKind.Timer => $"تمام! شغّلت مؤقت {Digits.ToArabicIndic(c.Minutes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))} دقيقة.",
        ChatKind.Note when c.Text is null => "اكتبلي ياها هون.",
        ChatKind.Note => "حفظتها.",
        ChatKind.Hush => "ماشي، رح اسكت…",
        ChatKind.Sleep => "تصبح على خير",
        ChatKind.Play or ChatKind.Game => "يلا!",
        ChatKind.Move => "هيك؟",
        ChatKind.Prayer when ctx.NextPrayer is { } p => Digits.ToArabicIndic($"{PrayerNames.Arabic(p.Prayer)} بعد {PrayerText.Duration(p.Left)}"),
        ChatKind.Prayer => "ما بعرف مدينتك لسا. خبّرني ياها.",
        ChatKind.Greeting => Greetings[((ctx.Turn % Greetings.Length) + Greetings.Length) % Greetings.Length],
        _ => Learning,
    };
}
