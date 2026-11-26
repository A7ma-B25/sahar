using Sahar.Core.Quran;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Quran;

/// <summary>0.16 Task 8 (spec §10, owner rulings 5 and 7): the Quran bar's rules — seek, surah steps, RTL, keys, resume.</summary>
public sealed class QuranBarTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void Seek_ClampsAtZeroAndTheEnd()
    {
        Assert.Equal(S(0), QuranBar.Seek(S(4), -QuranBar.Step, S(600)));
        Assert.Equal(S(590), QuranBar.Seek(S(600), -QuranBar.Step, S(600)));
        Assert.Equal(S(600), QuranBar.Seek(S(595), QuranBar.Step, S(600)));
        Assert.Equal(S(130), QuranBar.Seek(S(120), QuranBar.Step, S(600)));
        Assert.Equal(S(130), QuranBar.Seek(S(120), QuranBar.Step, null));   // no length known yet: never below zero
        Assert.Equal(S(10), QuranBar.Step.Duration());
    }

    [Fact]
    public void SurahSteps_StopAtTheFirstAndTheLast()
    {
        Assert.False(QuranBar.CanPrev(1));
        Assert.True(QuranBar.CanPrev(2));
        Assert.True(QuranBar.CanNext(113));
        Assert.False(QuranBar.CanNext(114));
    }

    [Fact]
    public void Buttons_FollowTheState_AndCanSeek()
    {
        var playing = new QuranBarView(18, "ar.alafasy", RecitationState.Playing, S(724), S(2590), CanSeek: true);
        foreach (var b in new[] { BarButton.PrevSurah, BarButton.Back10, BarButton.Toggle, BarButton.Forward10, BarButton.NextSurah, BarButton.Track, BarButton.Close })
            Assert.True(QuranBar.Enabled(b, playing), b.ToString());
        var noSeek = playing with { CanSeek = false };
        Assert.False(QuranBar.Enabled(BarButton.Back10, noSeek));
        Assert.False(QuranBar.Enabled(BarButton.Forward10, noSeek));
        Assert.False(QuranBar.Enabled(BarButton.Track, noSeek));
        Assert.True(QuranBar.Enabled(BarButton.Toggle, noSeek));
        var opening = playing with { State = RecitationState.Opening };
        foreach (var b in new[] { BarButton.PrevSurah, BarButton.Back10, BarButton.Toggle, BarButton.Forward10, BarButton.NextSurah, BarButton.Track })
            Assert.False(QuranBar.Enabled(b, opening), b.ToString());
        Assert.True(QuranBar.Enabled(BarButton.Close, opening));
        Assert.False(QuranBar.Enabled(BarButton.NextSurah, playing with { Surah = 114 }));
        Assert.False(QuranBar.Enabled(BarButton.PrevSurah, playing with { Surah = 1 }));
        var failed = playing with { State = RecitationState.Failed };
        Assert.True(QuranBar.Enabled(BarButton.Retry, failed));
        Assert.True(QuranBar.Enabled(BarButton.Dismiss, failed));
        Assert.False(QuranBar.Enabled(BarButton.Toggle, failed));
        Assert.False(QuranBar.Enabled(BarButton.Retry, playing));
    }

    [Fact]
    public void Track_FillsFromTheRight_AndAClickNearTheRightIsTheStart()
    {
        Assert.Equal(0f, QuranBar.Fill(S(0), S(100)));
        Assert.Equal(0.28f, QuranBar.Fill(S(28), S(100)), 3);
        Assert.Equal(1f, QuranBar.Fill(S(150), S(100)));
        Assert.Equal(0f, QuranBar.Fill(S(10), null));
        var track = QuranBarLayout.Track;
        Assert.Equal(S(0), QuranBar.FromTrack(track.X + track.Width, track, S(100)));
        Assert.Equal(S(100), QuranBar.FromTrack(track.X, track, S(100)));
        Assert.Equal(S(25), QuranBar.FromTrack(track.X + track.Width * 0.75f, track, S(100)));
        Assert.Equal(S(0), QuranBar.FromTrack(track.X + track.Width + 40, track, S(100)));   // clamped
        // The knob's centre for 28 %: 28 % of the width in from the right edge.
        Assert.Equal(track.X + track.Width * 0.72f, QuranBar.KnobX(0.28f, track), 3);
    }

    [Fact]
    public void Keys_FollowTheScreen_InRtl()
    {
        const int left = 0x25, right = 0x27, space = 0x20, esc = 0x1B;
        Assert.Equal(BarKey.Forward10, QuranBar.Key(left, ctrl: false));
        Assert.Equal(BarKey.Back10, QuranBar.Key(right, ctrl: false));
        Assert.Equal(BarKey.NextSurah, QuranBar.Key(left, ctrl: true));
        Assert.Equal(BarKey.PrevSurah, QuranBar.Key(right, ctrl: true));
        Assert.Equal(BarKey.Toggle, QuranBar.Key(space, ctrl: false));
        Assert.Equal(BarKey.Collapse, QuranBar.Key(esc, ctrl: false));
        Assert.Null(QuranBar.Key(0x41, ctrl: false));
    }

    [Fact]
    public void Clock_ArabicDigits_Tabular_AndDashesWhenUnknown()
    {
        Assert.Equal("١٢:٠٤", QuranBar.Clock(S(724)));
        Assert.Equal("٤٣:١٠", QuranBar.Clock(S(2590)));
        Assert.Equal("١:٠٥:٠٩", QuranBar.Clock(S(3909)));
        Assert.Equal("--:--", QuranBar.Clock(null));
        Assert.Equal("١٢:٠٤ من ٤٣:١٠", QuranBar.ValueText(S(724), S(2590)));
    }

    [Fact]
    public void Collapse_AfterSixIdleSeconds_NotWithThePointerOver()
    {
        var t0 = new DateTimeOffset(2026, 11, 26, 20, 0, 0, TimeSpan.Zero);
        Assert.False(QuranBar.Collapse(t0, t0.AddSeconds(5.9), pointerOver: false));
        Assert.True(QuranBar.Collapse(t0, t0.AddSeconds(6), pointerOver: false));
        Assert.False(QuranBar.Collapse(t0, t0.AddSeconds(60), pointerOver: true));
    }

    [Fact]
    public void Lines_SyrianArabic_StateAfterTheReciter()
    {
        var v = new QuranBarView(18, "ar.alafasy", RecitationState.Paused, S(10), S(100), true);
        Assert.Equal("الكهف", QuranBar.Title(v));
        Assert.Equal("مشاري العفاسي · واقفة", QuranBar.Subtitle(v));
        Assert.Equal("مشاري العفاسي · عم تفتح…", QuranBar.Subtitle(v with { State = RecitationState.Opening }));
        Assert.Equal("مشاري العفاسي", QuranBar.Subtitle(v with { State = RecitationState.Playing }));
        Assert.Equal("ما قدرت شغّل التلاوة هلق. يمكن النت ضعيف.", QuranBar.FailedLine);
        Assert.Equal("كمّل", QuranBar.Label(BarButton.Toggle, v));
        Assert.Equal("وقّف شوي", QuranBar.Label(BarButton.Toggle, v with { State = RecitationState.Playing }));
        Assert.Equal("السورة يلي قبل", QuranBar.Label(BarButton.PrevSurah, v));
        Assert.Equal("السورة الجاية", QuranBar.Label(BarButton.NextSurah, v));
    }

    // ---- resume where he left off (ruling 5) ----

    [Fact]
    public void Resume_KeepsSurahReciterAndSeconds_NotTheFirstSeconds()
    {
        Assert.Equal(new QuranResume(18, "ar.alafasy", 724), QuranBar.Keep(18, "ar.alafasy", S(724.6)));
        Assert.Null(QuranBar.Keep(18, "ar.alafasy", S(3)));
    }

    [Fact]
    public void Resume_OnlyTheSameSurahAndReciter_AndNotAtTheVeryEnd()
    {
        var saved = new QuranResume(18, "ar.alafasy", 724);
        Assert.Equal(S(724), QuranBar.ResumeFrom(saved, 18, "ar.alafasy", S(2590)));
        Assert.Equal(S(724), QuranBar.ResumeFrom(saved, 18, "ar.alafasy", null));
        Assert.Null(QuranBar.ResumeFrom(saved, 19, "ar.alafasy", S(2590)));
        Assert.Null(QuranBar.ResumeFrom(saved, 18, "ar.husary", S(2590)));
        Assert.Null(QuranBar.ResumeFrom(saved, 18, "ar.alafasy", S(726)));   // within 5 s of the end: start again
        Assert.Null(QuranBar.ResumeFrom(null, 18, "ar.alafasy", S(2590)));
    }

    [Fact]
    public void Resume_IsKeptInSettings_RoundTrip()
    {
        var dir = Directory.CreateTempSubdirectory("sahar-qresume-").FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json");
            SettingsStore.Save(path, new SaharSettings { QuranResume = new QuranResume(18, "ar.alafasy", 724) });
            Assert.Equal(new QuranResume(18, "ar.alafasy", 724), SettingsStore.Load(path).QuranResume);
            Assert.Contains("\"quranResume\"", File.ReadAllText(path));
            Assert.Null(new SaharSettings().QuranResume);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- the layout (V15QuranBar: 340 wide, padding 12, rows 38 / 24 / 48, gap 10) ----

    [Fact]
    public void Layout_MatchesTheMockup_RightToLeft()
    {
        Assert.Equal(340, QuranBarLayout.Width);
        Assert.Equal(154, QuranBarLayout.Height(RecitationState.Playing));
        Assert.Equal(142, QuranBarLayout.Height(RecitationState.Failed));
        var r = QuranBarLayout.Buttons(RecitationState.Playing);
        // From right to left: previous surah · back 10 · play · forward 10 · next surah.
        Assert.True(r[BarButton.PrevSurah].X > r[BarButton.Back10].X);
        Assert.True(r[BarButton.Back10].X > r[BarButton.Toggle].X);
        Assert.True(r[BarButton.Toggle].X > r[BarButton.Forward10].X);
        Assert.True(r[BarButton.Forward10].X > r[BarButton.NextSurah].X);
        Assert.Equal(48, r[BarButton.Toggle].Width);
        Assert.Equal(170, r[BarButton.Toggle].X + r[BarButton.Toggle].Width / 2);   // centred
        Assert.Equal(12, r[BarButton.Close].X);   // the × on the left (the end side)
        Assert.Equal(QuranBarLayout.Track, r[BarButton.Track]);
        var f = QuranBarLayout.Buttons(RecitationState.Failed);
        Assert.True(f[BarButton.Retry].X > f[BarButton.Dismiss].X);   // «جرّب كمان مرة» first, on the right
        Assert.False(f.ContainsKey(BarButton.Toggle));
        Assert.Equal(BarButton.Toggle, QuranBarLayout.Hit(r, 170, 118));
        Assert.Null(QuranBarLayout.Hit(r, 170, 2));
    }
}
