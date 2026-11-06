using Sahar.Core.Brain;
using Sahar.Core.Life;

namespace Sahar.Core.Tests.Life;

public class PreferencesTests
{
    [Fact]
    public void Preferences_Liked_IncreasesAndCaps()
    {
        var p = new Preferences();
        Assert.Equal(1f, p.Weight(LifeAction.Wander));
        p.Liked(LifeAction.Wander);
        Assert.Equal(1.25f, p.Weight(LifeAction.Wander), 0.001f);
        for (int i = 0; i < 10; i++) p.Liked(LifeAction.Wander);
        Assert.Equal(3f, p.Weight(LifeAction.Wander));
        Assert.Equal(1f, p.Weight(LifeAction.PeekTaskbar));
    }

    [Fact]
    public void Preferences_Disliked_DecreasesAndFloors()
    {
        var p = new Preferences();
        p.Disliked(LifeAction.PerchWindow);
        Assert.Equal(0.7f, p.Weight(LifeAction.PerchWindow), 0.001f);
        for (int i = 0; i < 10; i++) p.Disliked(LifeAction.PerchWindow);
        Assert.Equal(0.25f, p.Weight(LifeAction.PerchWindow));
    }

    [Fact]
    public void Preferences_Decay_MovesTowardOne()
    {
        var p = new Preferences();
        for (int i = 0; i < 10; i++) { p.Liked(LifeAction.Wander); p.Disliked(LifeAction.PeekTaskbar); }
        p.Decay();
        Assert.Equal(3f - 0.05f * 2f, p.Weight(LifeAction.Wander), 0.001f);
        Assert.Equal(0.25f + 0.05f * 0.75f, p.Weight(LifeAction.PeekTaskbar), 0.001f);
    }
}

public class RoutineTests
{
    private static readonly TimeSpan Utc3 = TimeSpan.FromHours(3);
    private static DateTimeOffset Day(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, Utc3);

    private static DateTimeOffset Run(Routine r, DateTimeOffset start, int minutes, bool active)
    {
        var t = start;
        for (int i = 0; i <= minutes; i++) { r.Tick(t, active); if (i < minutes) t = t.AddMinutes(1); }
        return t;
    }

    [Fact]
    public void Routine_TwoHoursActive_BreakOnce()
    {
        var r = new Routine();
        var t = Run(r, Day(6, 9), 120, active: true);
        Assert.True(r.TakeBreakDue());
        Assert.False(r.TakeBreakDue());
        Run(r, t.AddMinutes(1), 30, active: true);
        Assert.False(r.TakeBreakDue());
    }

    [Fact]
    public void Routine_FiveMinuteGap_Resets()
    {
        var r = new Routine();
        var t = Run(r, Day(6, 9), 110, active: true);
        t = Run(r, t.AddMinutes(1), 6, active: false);
        Run(r, t.AddMinutes(1), 20, active: true);
        Assert.False(r.TakeBreakDue());
    }

    [Fact]
    public void Routine_ClockJump_NoBreak()
    {
        var r = new Routine();
        var t = Run(r, Day(6, 9), 60, active: true);
        Run(r, t.AddHours(3), 30, active: true);
        Assert.False(r.TakeBreakDue());
    }

    [Fact]
    public void Routine_LateNight_OncePerNight()
    {
        var r = new Routine();
        r.Tick(Day(7, 0, 30), active: true);
        Assert.True(r.TakeLateNightDue());
        r.Tick(Day(7, 1, 30), active: true);
        Assert.False(r.TakeLateNightDue());
        r.Tick(Day(7, 23, 0), active: true);
        Assert.False(r.TakeLateNightDue());
        r.Tick(Day(8, 0, 10), active: true);
        Assert.True(r.TakeLateNightDue());
        r.Tick(Day(8, 2, 0), active: false);
        Assert.False(r.TakeLateNightDue());
    }

    [Fact]
    public void Routine_Profile_LearnsWorkHours()
    {
        var r = new Routine();
        for (int d = 1; d <= 10; d++)
        {
            Run(r, Day(d, 10), 59, active: true);
            Run(r, Day(d, 22), 59, active: false);
        }
        r.Tick(Day(11, 9), active: false);
        Assert.True(r.BusyFactor(10) > 1.6f, $"{r.BusyFactor(10)}");
        Assert.True(r.BusyFactor(22) < 1.1f, $"{r.BusyFactor(22)}");
    }
}

public class TypingAndMediaTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Typing_RecentKey_IsTyping() =>
        Assert.True(new TypingDetector().Update(T0, sinceKeyboard: TimeSpan.FromMilliseconds(300)));

    [Fact]
    public void Typing_StopsThreeSecondsAfterLastKey()
    {
        var d = new TypingDetector();
        Assert.True(d.Update(T0, TimeSpan.FromSeconds(2.9)));
        Assert.False(d.Update(T0, TimeSpan.FromSeconds(3.1)));
    }

    [Fact]
    public void Typing_NoKeyEver_IsNotTyping() =>
        Assert.False(new TypingDetector().Update(T0, TimeSpan.MaxValue));

    [Fact]
    public void Media_MusicStartStop_Edges()
    {
        var m = new MediaEdges();
        Assert.Equal([EventKind.Music], m.Update(playing: true, isVideo: false, fromBrowser: false, browserInFront: false));
        Assert.Empty(m.Update(true, false, false, false));
        Assert.Equal([EventKind.MusicStop], m.Update(false, false, false, false));
        Assert.Empty(m.Update(false, false, false, false));
    }

    [Fact]
    public void Media_Video_NoMusicEvents_SetsVideo()
    {
        var m = new MediaEdges();
        Assert.Empty(m.Update(true, isVideo: true, false, false));
        Assert.True(m.VideoPlaying);
        Assert.Empty(m.Update(false, true, false, false));
        Assert.False(m.VideoPlaying);
    }

    [Fact]
    public void Media_MusicToVideo_StopsMusic()
    {
        var m = new MediaEdges();
        m.Update(true, false, false, false);
        Assert.Equal([EventKind.MusicStop], m.Update(true, true, false, false));
        Assert.True(m.VideoPlaying);
    }
}

public class BrowserMediaTests
{
    [Theory]
    [InlineData("Chrome", true)]
    [InlineData("MSEdge", true)]
    [InlineData("308046B0AF4A39CB", true)]           // Firefox's media session id
    [InlineData("firefox.exe", true)]
    [InlineData("Spotify.exe", false)]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false)]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", false)]
    public void Media_IsBrowser(string appId, bool browser) => Assert.Equal(browser, MediaEdges.IsBrowser(appId));

    [Fact]
    public void Media_BrowserPlaying_IsMusic_AndStillWhileInFront()
    {
        // A YouTube song shows (headphones); since it may be a video, no big moves while the browser is in front.
        var m = new MediaEdges();
        Assert.Equal([EventKind.Music], m.Update(playing: true, isVideo: false, fromBrowser: true, browserInFront: true));
        Assert.True(m.VideoPlaying);
    }

    [Fact]
    public void Media_BrowserPlaying_Behind_MusicNotVideo()
    {
        var m = new MediaEdges();
        Assert.Equal([EventKind.Music], m.Update(true, false, fromBrowser: true, browserInFront: false));
        Assert.False(m.VideoPlaying);
    }
}

public class ActionMemoryTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ActionMemory_LearnsOncePerAction()
    {
        var m = new ActionMemory();
        m.Started(LifeAction.Wander, T0);
        Assert.Equal(LifeAction.Wander, m.TakeForFeedback(T0.AddSeconds(3)));
        Assert.Null(m.TakeForFeedback(T0.AddSeconds(5)));   // five pokes are still one like
        m.Started(LifeAction.PeekTaskbar, T0.AddMinutes(2));
        Assert.Equal(LifeAction.PeekTaskbar, m.TakeForFeedback(T0.AddMinutes(2).AddSeconds(1)));
    }

    [Fact]
    public void ActionMemory_AfterWindow_Nothing()
    {
        var m = new ActionMemory();
        m.Started(LifeAction.Wander, T0);
        Assert.Null(m.TakeForFeedback(T0.AddSeconds(16)));
    }
}

public class LifeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-life-" + Guid.NewGuid().ToString("N"));
    private string PathIn => System.IO.Path.Combine(_dir, "life.json");

    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void LifeStore_RoundTrip()
    {
        var p = new Preferences();
        p.Liked(LifeAction.Wander);
        p.Disliked(LifeAction.PerchWindow);
        var r = new Routine();
        for (int d = 1; d <= 3; d++)
            for (int m = 0; m < 60; m++) r.Tick(new DateTimeOffset(2026, 11, d, 10, m, 0, TimeSpan.Zero), true);
        r.Tick(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero), false);

        LifeStore.Save(PathIn, new LifeState(1, p.ToState(), r.ProfileToState()));
        var s = LifeStore.Load(PathIn);
        var p2 = Preferences.FromState(s.Weights);
        var r2 = Routine.FromState(s.Hours);
        Assert.Equal(p.Weight(LifeAction.Wander), p2.Weight(LifeAction.Wander));
        Assert.Equal(p.Weight(LifeAction.PerchWindow), p2.Weight(LifeAction.PerchWindow));
        Assert.Equal(r.BusyFactor(10), r2.BusyFactor(10));
    }

    [Fact]
    public void LifeStore_RemembersLastDecayDay()
    {
        LifeStore.Save(PathIn, new LifeState(1, [], new float[24], "2026-11-01"));
        Assert.Equal("2026-11-01", LifeStore.Load(PathIn).LastDecay);
    }

    [Fact]
    public void Preferences_DecayDays_CatchesUpMissedDays()
    {
        var a = new Preferences(); var b = new Preferences();
        for (int i = 0; i < 10; i++) { a.Liked(LifeAction.Wander); b.Liked(LifeAction.Wander); }
        a.DecayDays(3);
        b.Decay(); b.Decay(); b.Decay();
        Assert.Equal(b.Weight(LifeAction.Wander), a.Weight(LifeAction.Wander), 0.0001f);
        var c = new Preferences();
        for (int i = 0; i < 10; i++) c.Liked(LifeAction.Wander);
        c.DecayDays(100000);   // capped, never hangs
        Assert.InRange(c.Weight(LifeAction.Wander), 1f, 1.01f);
    }

    [Fact]
    public void LifeStore_Corrupt_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, "{ not json");
        var s = LifeStore.Load(PathIn);
        Assert.Empty(s.Weights);
        Assert.Equal(24, s.Hours.Length);
        Assert.True(File.Exists(PathIn + ".bad"));
        Assert.Equal(1f, Preferences.FromState(s.Weights).Weight(LifeAction.Wander));
    }

    [Fact]
    public void LifeStore_UnknownActionsAndWrongHours_Normalized()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathIn, """{"version":1,"weights":{"Moonwalk":2,"Wander":9},"hours":[1,1]}""");
        var s = LifeStore.Load(PathIn);
        Assert.Equal(24, s.Hours.Length);
        var p = Preferences.FromState(s.Weights);
        Assert.Equal(3f, p.Weight(LifeAction.Wander));   // clamped
    }

    [Fact]
    public void LifeStore_SizeIsFixed()
    {
        var p = new Preferences();
        var r = new Routine();
        for (int i = 0; i < 1000; i++) p.Liked((LifeAction)(1 + i % 5));
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int h = 0; h < 100 * 24; h++) { r.Tick(t, h % 3 == 0); t = t.AddHours(1); }
        LifeStore.Save(PathIn, new LifeState(1, p.ToState(), r.ProfileToState()));
        Assert.True(new FileInfo(PathIn).Length < 2048, $"{new FileInfo(PathIn).Length} bytes");
        Assert.Equal(24, LifeStore.Load(PathIn).Hours.Length);
    }
}
