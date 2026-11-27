using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Persona;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Turn;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Online;

/// <summary>0.17 Task 3: the router (spec §5), what never leaves the laptop (§6), the online prompt and one persona.</summary>
public sealed class RouterPolicyPromptTests
{
    private static RouteInput In(string line = "شو رأيك بالقهوة؟", string source = "chat", bool window = true, string? toggle = "online",
        bool localOnly = false, bool consent = true, bool any = true) => new(source, window, toggle, localOnly, consent, line, any);

    [Theory]
    [InlineData("initiative", "background")]
    [InlineData("routine", "background")]
    [InlineData("reflect", "background")]
    [InlineData("voice", "background")]
    [InlineData("draft", "draft")]
    public void BrainRouter_BackgroundAndDraft_AreLocal(string source, string reason) =>
        Assert.Equal((BrainChoice.Local, reason), BrainRouter.Decide(In(source: source)));

    [Fact]
    public void BrainRouter_Table()
    {
        Assert.Equal((BrainChoice.Online, "chat"), BrainRouter.Decide(In()));
        Assert.Equal((BrainChoice.Local, "local-only"), BrainRouter.Decide(In(localOnly: true)));
        Assert.Equal((BrainChoice.Local, "no-consent"), BrainRouter.Decide(In(consent: false)));
        Assert.Equal((BrainChoice.Local, "card"), BrainRouter.Decide(In(window: false)));
        Assert.Equal((BrainChoice.Local, "toggle"), BrainRouter.Decide(In(toggle: "local")));
        Assert.Equal((BrainChoice.Local, "action"), BrainRouter.Decide(In("حطيلي مؤقت عشر دقايق بعد ما خلص")));
        Assert.Equal((BrainChoice.Local, "no-provider"), BrainRouter.Decide(In(any: false)));
        Assert.Equal((BrainChoice.Online, "chat"), BrainRouter.Decide(In(toggle: null)));
    }

    [Theory]
    [InlineData("اكتبلي ملاحظة إنو بكرا عندي اجتماع")]
    [InlineData("حطيلي مؤقت واشرحلي ليش")]
    [InlineData("شغليلي موسيقى واكتبيلي قصيدة")]
    public void BrainRouter_ActionBeatsWriting(string line)
    {
        Assert.True(Intents.Writing(line));
        Assert.Equal(BrainChoice.Local, BrainRouter.Decide(In(line)).Brain);
    }

    [Theory]
    [InlineData("ذكريني بالدوا الساعة ٩")]
    [InlineData("وقفي الغنية")]
    [InlineData("إيمتى الصلاة الجاية؟")]
    [InlineData("كيف الطقس بكرا")]
    [InlineData("العبي معي غميضة")]
    [InlineData("سمعيني سورة الكهف")]
    [InlineData("remind me in 5 minutes")]
    [InlineData("play some music")]
    [InlineData("set a timer for 10 minutes")]
    [InlineData("اسكتي شوي")]
    [InlineData("افتحيلي الإعدادات")]
    public void Intents_Action(string line) => Assert.True(Intents.Action(line), line);

    [Theory]
    [InlineData("شو رأيك بالقهوة؟")]
    [InlineData("احكيلي نكتة")]
    [InlineData("اشرحلي شو هو الذكاء الاصطناعي")]
    [InlineData("what is a black hole")]
    [InlineData("كيفك اليوم")]
    public void Intents_NotAction(string line) => Assert.False(Intents.Action(line), line);

    [Theory]
    [InlineData("اكتبلي إيميل لمديري", true)]
    [InlineData("اشرحيلي كيف بيشتغل الإنترنت", true)]
    [InlineData("ترجملي هالجملة للإنكليزي", true)]
    [InlineData("لخصيلي هالمقال", true)]
    [InlineData("write me a poem about the moon", true)]
    [InlineData("explain recursion", true)]
    [InlineData("كيفك", false)]
    public void Intents_Writing(string line, bool yes) => Assert.Equal(yes, Intents.Writing(line));

    [Fact]
    public void Intents_LongLineIsWriting() => Assert.True(Intents.Writing(new string('ك', 160)));

    private static readonly DateTimeOffset T = new(2026, 11, 27, 21, 0, 0, TimeSpan.FromHours(3));
    private static ProfileItem P(string text, Sensitivity s = Sensitivity.None, LearnSource src = LearnSource.Chats) =>
        new("p" + text.Length, text, ProfileCategory.Likes, [new ItemSource(src, T)], Confidence.High, ItemStatus.Confirmed, s, T);

    [Fact]
    public void OnlinePolicy_Profile_DropsSensitiveImportsAndMusic()
    {
        var kept = OnlinePolicy.Profile([
            P("بيحب القهوة"), P("عندو سكري", Sensitivity.Health), P("بيحكي مع أهلو كل جمعة", src: LearnSource.Imports),
            P("بيسمع فيروز الصبح", src: LearnSource.Music), P("بيزور المستشفى كل شهر"),   // keyword-sensitive though tagged None
        ]).Select(p => p.Text).ToList();
        Assert.Equal(["بيحب القهوة"], kept);
    }

    [Fact]
    public void OnlinePolicy_Memories_DropSensitive()
    {
        var kept = OnlinePolicy.Memories([new MemoryItem("1", "بيحب الشاي", MemoryKind.Preference, T), new MemoryItem("2", "عندو ديون", MemoryKind.Fact, T),
            new MemoryItem("3", "x", MemoryKind.Fact, T, Sensitivity.Family)]).Select(m => m.Text).ToList();
        Assert.Equal(["بيحب الشاي"], kept);
    }

    [Fact]
    public void OnlinePolicy_Moment_TimeAndDayOnly()
    {
        var s = new Situation("21:05", "الجمعة", "العشا", 12, 5, true, "Code.exe", 30, true, "فيروز - نسم علينا الهوا", 2, 1, "مطر بحلب", "low", "happy", 3);
        var m = OnlinePolicy.Moment(s);
        Assert.Contains("21:05", m);
        Assert.Contains("الجمعة", m);
        foreach (var never in new[] { "Code", "فيروز", "حلب", "العشا", "low" }) Assert.DoesNotContain(never, m);
        Assert.Equal("", OnlinePolicy.Moment(null));
    }

    [Fact]
    public void OnlinePolicy_Pair_SkipsSensitiveAndDistress()
    {
        Assert.True(OnlinePolicy.Pair("شو منطبخ اليوم", "شو رأيك بمجدرة؟"));
        Assert.False(OnlinePolicy.Pair("عندي ديون كتير", "يا عمري"));
        Assert.False(OnlinePolicy.Pair("بدي موت", "سامعتك"));
    }

    [Fact]
    public void OnlinePrompt_CappedNewestKept_NoShortRule()
    {
        var history = Enumerable.Range(0, 40).Select(i => new HistoryTurn(T, "سؤال " + i + " " + new string('ا', 400), "جواب " + i + " " + new string('ب', 400), "local")).ToList();
        var msgs = OnlinePrompt.Build("PERSONA", history, "", null, "سؤالي هلق");
        int tokens = msgs.Sum(m => PromptBuilder.Tokens(m.Content));
        Assert.True(tokens <= 4000, $"{tokens} tokens");
        Assert.Equal("system", msgs[0].Role);
        Assert.StartsWith("PERSONA", msgs[0].Content);
        Assert.Contains("سؤالي هلق", msgs[^1].Content);
        Assert.Contains("سؤال 39", msgs[^3].Content);
        Assert.DoesNotContain("سؤال 0 ", string.Join("\n", msgs.Select(m => m.Content)));
        Assert.DoesNotContain("٢٢٠", msgs[0].Content);
        Assert.DoesNotContain("JSON", msgs[0].Content);
        Assert.Equal(["system", "user", "assistant"], msgs.Take(3).Select(m => m.Role));
    }

    [Fact]
    public void OnlinePrompt_DataIsQuoted()
    {
        var msgs = OnlinePrompt.Build("P", [], "<<x>>", null, "مرحبا <<بيانات>>");
        Assert.DoesNotContain("<<بيانات>> ", msgs[^1].Content.Replace(PromptBuilder.DataOpen + " ", ""));
    }

    [Fact]
    public void Persona_OneForBothBrains_NotSmall_NotHuman()
    {
        var store = new FakeStore();
        var p = new PromptParts(store, new PersonaFile(Path.Combine(Path.GetTempPath(), "sahar-p-" + Guid.NewGuid().ToString("n"), "persona.md")))
            .Persona(MindVoice.Complementary, new MindAddress(Gender.Male, null));
        Assert.DoesNotContain("برنامج صغير", p);
        Assert.Contains("مش إنسانة", p);
        Assert.Contains("تكتبي أو تشرحي", p);
    }

    private sealed class FakeStore : IMindStore
    {
        public IReadOnlyList<ProfileItem> Profile() => [];
        public ProfileItem? Propose(string text, ProfileCategory category, ItemSource source, Sensitivity sensitive) => null;
        public bool Confirm(string id) => false;
        public bool Edit(string id, string text) => false;
        public bool Reject(string id) => false;
        public bool Delete(string id) => false;
        public IReadOnlyList<ProfileItem> ForPrompt(string line, int maxTokens) => [];
        public IReadOnlyList<MemoryItem> Memories(string? search = null) => [];
        public MemoryItem AddMemory(string text, MemoryKind kind, Sensitivity sensitive = Sensitivity.None) => throw new NotSupportedException();
        public bool DeleteMemory(string id) => false;
        public StyleCard Style() => new("", 0, 0, 0);
        public int AddSamples(LearnSource source, IEnumerable<string> lines) => 0;
        public IReadOnlyList<string> Samples(string topic, int count) => [];
        public void ForgetAll() { }
        public string Export() => "{}";
    }
}
