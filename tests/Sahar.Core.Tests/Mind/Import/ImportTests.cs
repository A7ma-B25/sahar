using Sahar.Core.Mind.Import;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Tests.Mind.Import;

/// <summary>Plan 0.14 Task B2: chats he exports (only his own lines, scrubbed), examples he writes. Synthetic data only.</summary>
public sealed class ImportTests
{
    private const string Lrm = "‎", Rlm = "‏", Nnbsp = " ";

    [Fact]
    public void WhatsApp_KeepsOnlyHisLines_MultiLine()
    {
        // Android export on an Arabic phone: Arabic-Indic digits, RTL marks, «،», a multi-line message, a system line,
        // media and deleted placeholders, other people named like him, a phone number and a link in his own lines.
        var txt = string.Join("\n",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤١ م - الرسائل والمكالمات مشفرة تمامًا بين الطرفين.",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤١ م - أحمد: شو وينك؟",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٢ م - سامر: هلق جاي",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٣ م - أحمد: يلا تمام",
            "وجيب معك الشاحن",
            "ورقمي الجديد ٠٩٤٤١٢٣٤٥٦",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٤ م - أحمد علي: أنا مش هو",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٥ م - أحمد: <الوسائط غير مضمنة>",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٦ م - أحمد: شوف هاد https://example.com/x?y=1 حلو",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٧ م - ~{Nnbsp}أحمد: نسخة تانية",
            $"{Rlm}١٢{Rlm}/١١{Rlm}/٢٠٢٦، ٩:٤٨ م - أحمد: تم حذف هذه الرسالة");

        var chat = ChatImport.Parse(txt, "WhatsApp Chat with سامر.txt")!;
        Assert.Equal("whatsapp", chat.Format);
        var senders = chat.Senders.Select(s => s.Name).ToList();
        Assert.Contains("أحمد", senders);
        Assert.Contains("سامر", senders);
        Assert.Contains("أحمد علي", senders);
        Assert.DoesNotContain(senders, s => s.Contains("مشفرة"));

        var his = chat.His("أحمد");
        Assert.Equal(
            ["شو وينك؟", "يلا تمام\nوجيب معك الشاحن\nورقمي الجديد [رقم]", "شوف هاد [رابط] حلو"],
            his);
        Assert.DoesNotContain(his, l => l.Contains("هلق جاي") || l.Contains("مش هو") || l.Contains("نسخة تانية"));
    }

    [Fact]
    public void WhatsApp_IosAndEnglish_Formats()
    {
        var txt = string.Join("\r\n",
            $"[13/11/2026, 21:41:05] Sami: hi",
            $"{Lrm}[13/11/2026, 21:41:30] Ahmad: kifak",
            $"[13/11/2026, 21:42:00] Ahmad: {Lrm}image omitted",
            "11/13/26, 9:43" + Nnbsp + "PM - Ahmad: yalla bye",
            "11/13/26, 9:44 PM - Ahmad: This message was deleted");
        var chat = ChatImport.Parse(txt, "_chat.txt")!;
        Assert.Equal(["kifak", "yalla bye"], chat.His("Ahmad"));
        Assert.Equal(2, chat.Senders.Single(s => s.Name == "Ahmad").Count);   // placeholders are not messages
    }

    [Fact]
    public void Telegram_KeepsOnlyHisLines()
    {
        const string single = """
        {"name":"Sami","type":"personal_chat","id":5,"messages":[
          {"id":1,"type":"service","date":"2026-11-12T10:00:00","actor":"Ahmad","action":"phone_call"},
          {"id":2,"type":"message","date":"2026-11-12T10:01:00","from":"Ahmad","from_id":"user111","text":"صباح الخير"},
          {"id":3,"type":"message","date":"2026-11-12T10:02:00","from":"Sami","from_id":"user222","text":"أهلين"},
          {"id":4,"type":"message","date":"2026-11-12T10:03:00","from":"Ahmad","from_id":"user111","text":["شوف ",{"type":"link","text":"t.me/x"}," و ",{"type":"mention","text":"@sami_99"}]},
          {"id":5,"type":"message","date":"2026-11-12T10:04:00","from":"Ahmad","from_id":"user111","text":"","photo":"photos/1.jpg"},
          {"id":6,"type":"message","date":"2026-11-12T10:05:00","from":"Ahmad Ali","from_id":"user333","text":"مش أنا"}
        ]}
        """;
        var chat = ChatImport.Parse(single, "result.json")!;
        Assert.Equal("telegram", chat.Format);
        Assert.Equal(["صباح الخير", "شوف [رابط] و [حساب]"], chat.His("Ahmad"));

        const string full = """
        {"personal_information":{"user_id":111,"first_name":"Ahmad","last_name":""},
         "chats":{"list":[
           {"name":"Sami","type":"personal_chat","messages":[
             {"type":"message","from":"Ahmad","from_id":"user111","text":"هلا"},
             {"type":"message","from":"Sami","from_id":"user222","text":"كيفك"}]},
           {"name":"Group","type":"private_group","messages":[
             {"type":"message","from":"Ahmad","from_id":"user111","text":"منيح"}]}]}}
        """;
        var all = ChatImport.Parse(full, "result.json")!;
        Assert.Equal("Ahmad", all.Me);   // known from his own export
        Assert.Equal(["هلا", "منيح"], all.His(all.Me!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some words, no chat")]
    [InlineData("{ not json")]
    [InlineData("{\"x\":1}")]
    public void NotAChat_IsNull(string content) => Assert.Null(ChatImport.Parse(content, "x.txt"));

    [Fact]
    public void Scrub_PhonesEmailsUrls()
    {
        Assert.Equal("اتصل على [رقم] أو [رقم]", Scrub.Clean("اتصل على +963 944 123 456 أو ٠٩٤٤-١٢٣-٤٥٦"));
        Assert.Equal("ابعتلي على [إيميل]", Scrub.Clean("ابعتلي على ahmad.s+x@mail.example.com"));
        Assert.Equal("شوف [رابط] و [رابط] و [رابط]", Scrub.Clean("شوف https://a.b/c?d=1 و www.site.org و youtube.com/watch"));
        Assert.Equal("حوّل على [حساب]", Scrub.Clean("حوّل على SY12 3456 7890 1234 5678 9012"));
        Assert.Equal("الكرت [رقم]", Scrub.Clean("الكرت 4111 1111 1111 1111"));
        Assert.Equal("تابع [حساب] هونيك", Scrub.Clean("تابع @sahar_moon هونيك"));
        // Times, small numbers, a year and a price stay.
        Assert.Equal("منلتقى الساعة 10:30 بعد 5 دقايق سنة 2026 بـ 15000", Scrub.Clean("منلتقى الساعة 10:30 بعد 5 دقايق سنة 2026 بـ 15000"));
    }

    [Fact]
    public void Examples_OneLineEach()
    {
        Assert.Equal(["يلا منحكي بعدين", "تمام حبيبي"], Examples.Lines("  يلا منحكي بعدين \r\n\r\n تمام حبيبي\n "));
        Assert.Equal(Examples.MaxLines, Examples.Lines(string.Join('\n', Enumerable.Range(0, 80).Select(i => $"سطر {i}"))).Count);
    }

    [Fact]
    public void Style_CardInPlainWords()
    {
        var at = DateTimeOffset.UnixEpoch;
        SampleRow S(string t, LearnSource k = LearnSource.Imports) => new(k, t, at, t);
        var card = StyleSummary.Card([
            S("يلا تمام"), S("شو عم تعمل هلق؟ يلا"), S("تمام منيح"), S("يلا بشوفك 😄"), S("كتير حلو تمام"), S("هلق جاي", LearnSource.Examples),
        ]);
        Assert.Equal(6, card.Samples);
        Assert.Equal(5, card.FromImports);
        Assert.Equal(1, card.FromExamples);
        Assert.Equal("جملك قصيرة، شامي، بتحب «يلا» و«تمام»، وإيموجي قليل.", card.SummaryAr);
        Assert.Equal("", StyleSummary.Card([]).SummaryAr);
    }
}
