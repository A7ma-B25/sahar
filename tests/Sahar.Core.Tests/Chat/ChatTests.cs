using Sahar.Core.Chat;
using Sahar.Core.Games;
using Sahar.Core.Motion;
using Sahar.Core.Prayer;
using Sahar.Core.Settings;
using Sahar.Core.Text;

namespace Sahar.Core.Tests.Chat;

public class ChatTests
{
    [Theory]
    [InlineData("مؤقت ٥", 5)]
    [InlineData("timer 5", 5)]
    [InlineData("TIMER 15", 15)]
    [InlineData("مؤقت", 25)]
    [InlineData("مؤقت ٢٥ دقيقة", 25)]
    [InlineData("موقت ۱۰", 10)]
    [InlineData("مُؤَقّـت ٣", 3)]
    [InlineData("  مؤقت   ٧  ", 7)]
    public void Timer(string text, int minutes)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal((ChatKind.Timer, minutes, false), (c.Kind, c.Minutes, c.OutOfRange));
    }

    [Theory]
    [InlineData("مؤقت ٠")]
    [InlineData("مؤقت 601")]
    [InlineData("مؤقت ابو")]
    public void Timer_OutOfRange(string text)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal((ChatKind.Timer, true, (int?)null), (c.Kind, c.OutOfRange, c.Minutes));
    }

    [Theory]
    [InlineData("ملاحظة اشتري خبز", "اشتري خبز")]
    [InlineData("ملاحظة اتصل ب ٥٥٥", "اتصل ب ٥٥٥")]
    [InlineData("note call mom", "call mom")]
    [InlineData("Note  Call Mom!", "Call Mom!")]
    [InlineData("ملاحظه  مُهمّة جداً ", "مُهمّة جداً")]
    public void Note_KeepsTheTextAsTyped(string text, string note)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal((ChatKind.Note, note), (c.Kind, c.Text));
    }

    [Fact]
    public void Note_Alone_HasNoText() => Assert.Equal((ChatKind.Note, (string?)null), (ChatCommands.Parse("ملاحظة").Kind, ChatCommands.Parse("ملاحظة").Text));

    [Theory]
    [InlineData("اسكتي", -1)]
    [InlineData("اسكتي ساعة", 60)]
    [InlineData("اسكتي ساعتين", 120)]
    [InlineData("اسكتي نص ساعة", 30)]
    [InlineData("اسكتي ١٠ دقايق", 10)]
    [InlineData("hush", -1)]
    [InlineData("be quiet", -1)]
    [InlineData("hush 2 hours", 120)]
    [InlineData("hush half an hour", 30)]
    public void Hush(string text, int minutes)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal(ChatKind.Hush, c.Kind);
        Assert.Equal(minutes < 0 ? null : TimeSpan.FromMinutes(minutes), c.HushFor);
    }

    [Theory]
    [InlineData("نامي", ChatKind.Sleep)]
    [InlineData("sleep", ChatKind.Sleep)]
    [InlineData("نلعب", ChatKind.Play)]
    [InlineData("يلا نلعب", ChatKind.Play)]
    [InlineData("play", ChatKind.Play)]
    [InlineData("الصلاة", ChatKind.Prayer)]
    [InlineData("prayer", ChatKind.Prayer)]
    [InlineData("كيفك", ChatKind.Greeting)]
    [InlineData("شلونك؟", ChatKind.Greeting)]
    [InlineData("hi", ChatKind.Greeting)]
    [InlineData("Hello!", ChatKind.Greeting)]
    [InlineData("شو اسمك", ChatKind.Unknown)]
    [InlineData("   ", ChatKind.Empty)]
    [InlineData("", ChatKind.Empty)]
    public void Kinds(string text, ChatKind kind) => Assert.Equal(kind, ChatCommands.Parse(text).Kind);

    [Fact]
    public void Long_IsUnknown_NoException() => Assert.Equal(ChatKind.Unknown, ChatCommands.Parse(new string('ا', 500)).Kind);

    [Theory]
    [InlineData("نلعب الحقني", GameKind.Chase)]
    [InlineData("نلعب غمّضلي", GameKind.HideSeek)]
    [InlineData("نلعب الطابة", GameKind.Ball)]
    [InlineData("نلعب النجوم", GameKind.Stars)]
    [InlineData("يلا نلعب الطابة", GameKind.Ball)]
    public void Games(string text, GameKind game)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal((ChatKind.Game, (GameKind?)game), (c.Kind, c.Game));
    }

    [Theory]
    [InlineData("ارقصي", MotionKind.Dance)]
    [InlineData("تمطّي", MotionKind.Stretch)]
    public void Moves(string text, MotionKind move)
    {
        var c = ChatCommands.Parse(text);
        Assert.Equal((ChatKind.Move, (MotionKind?)move), (c.Kind, c.Move));
    }

    [Fact]
    public void Hop_IsAMoveWithoutAMotion()
    {
        var c = ChatCommands.Parse("نطّي");
        Assert.Equal((ChatKind.Move, (MotionKind?)null), (c.Kind, c.Move));
    }

    private static readonly ChatContext NoPrayer = new(null, 0);
    private static string Reply(string text, ChatContext? ctx = null, Gender to = Gender.Male) =>
        ChatReply.For(ChatCommands.Parse(text), ctx ?? NoPrayer, to);

    [Fact]
    public void Replies()
    {
        Assert.Equal("تمام! شغّلت مؤقت ٥ دقيقة.", Reply("مؤقت 5"));
        Assert.Equal("بقدر شغّل مؤقت من دقيقة لـ ٦٠٠ دقيقة.", Reply("مؤقت 601"));
        Assert.Equal("حفظتها.", Reply("ملاحظة خبز"));
        Assert.Equal("اكتبلي ياها هون.", Reply("ملاحظة"));
        Assert.Equal("ماشي، رح اسكت…", Reply("اسكتي"));
        Assert.Equal("تصبح على خير", Reply("نامي"));
        Assert.Equal("تصبحي على خير", Reply("نامي", to: Gender.Female));
        Assert.Equal("يلا!", Reply("نلعب"));
        Assert.Equal("يلا!", Reply("نلعب النجوم"));
        Assert.Equal("هيك؟", Reply("تمطّي"));
        Assert.Equal("هيك؟", Reply("ارقصي"));
        Assert.Equal("هيك؟", Reply("نطّي"));
        Assert.Equal("العصر بعد ٤٢ دقيقة", Reply("الصلاة", new ChatContext((Sahar.Core.Prayer.Prayer.Asr, TimeSpan.FromMinutes(42)), 0)));
        Assert.Equal("الضهر بعد ساعة و٥ دقايق", Reply("الصلاة", new ChatContext((Sahar.Core.Prayer.Prayer.Dhuhr, TimeSpan.FromMinutes(65)), 0)));
        Assert.Equal("ما بعرف مدينتك لسا. خبّرني ياها.", Reply("الصلاة"));
        Assert.Equal("ما بعرف مدينتك لسا. خبّريني ياها.", Reply("الصلاة", to: Gender.Female));
        Assert.Equal("منيحة! وإنت كيفك؟", Reply("كيفك", new ChatContext(null, 0)));
        Assert.Equal("أهلين! اشتقتلك.", Reply("كيفك", new ChatContext(null, 1)));
        Assert.Equal("هلا! أنا هون.", Reply("كيفك", new ChatContext(null, 2)));
        Assert.Equal("منيحة! وإنت كيفك؟", Reply("hi", new ChatContext(null, 3)));
        Assert.Equal("منيحة! وإنتي كيفك؟", Reply("كيفك", new ChatContext(null, 0), Gender.Female));
        Assert.Equal("سمعتك! لسا عم اتعلّم الحكي، وقريباً بجاوبك على كل شي.", Reply("شو اسمك"));
        Assert.Equal("", Reply("  "));
    }

    [Fact]
    public void Digits_RoundTrip()
    {
        Assert.Equal("١٢:٠٥", Digits.ToArabicIndic("12:05"));
        Assert.Equal("25 و3", Digits.ToLatin("۲۵ و٣"));
    }

    [Fact]
    public void PrayerNames_UseTheMockupSpellings()
    {
        Assert.Equal("الضهر", PrayerNames.Arabic(Sahar.Core.Prayer.Prayer.Dhuhr));
        Assert.Equal("العشا", PrayerNames.Arabic(Sahar.Core.Prayer.Prayer.Isha));
    }
}
