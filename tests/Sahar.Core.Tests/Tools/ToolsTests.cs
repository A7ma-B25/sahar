using Sahar.Core.Life;
using Sahar.Core.Brain;
using Sahar.Core.Tools;
using B = Sahar.Core.Brain.Brain;

namespace Sahar.Core.Tests.Tools;

public class FocusTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FocusTimer_Remaining_CountsDown()
    {
        var t = new FocusTimer();
        t.Start(TimeSpan.FromMinutes(25), T0);
        Assert.True(t.Running);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddMinutes(10)));
        Assert.Equal(TimeSpan.Zero, t.Remaining(T0.AddMinutes(30)));
    }

    [Fact]
    public void FocusTimer_TakeDone_OnceAfterTheEnd()
    {
        var t = new FocusTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        Assert.False(t.TakeDone(T0.AddMinutes(4)));
        Assert.True(t.TakeDone(T0.AddMinutes(5)));
        Assert.False(t.TakeDone(T0.AddMinutes(6)));
        Assert.False(t.Running);
    }

    [Fact]
    public void FocusTimer_AfterASleepPastTheEnd_DoneRightAway()
    {
        var t = new FocusTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        Assert.True(t.TakeDone(T0.AddHours(2)));
        Assert.False(t.TakeDone(T0.AddHours(2).AddSeconds(1)));
    }

    [Fact]
    public void FocusTimer_Cancel_NeverDone()
    {
        var t = new FocusTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        t.Cancel();
        Assert.False(t.Running);
        Assert.False(t.TakeDone(T0.AddMinutes(10)));
    }

    [Fact]
    public void FocusTimer_IsLong_From25Minutes()
    {
        var t = new FocusTimer();
        t.Start(TimeSpan.FromMinutes(24), T0);
        Assert.False(t.IsLong);
        t.Start(TimeSpan.FromMinutes(25), T0);
        Assert.True(t.IsLong);
    }
}

public class TimerInputTests
{
    [Theory]
    [InlineData("25", 25)]
    [InlineData(" 25 ", 25)]
    [InlineData("٢٥", 25)]
    [InlineData("۲۵", 25)]
    [InlineData("5د", 5)]
    [InlineData("5 min", 5)]
    [InlineData("600", 600)]
    [InlineData("1", 1)]
    public void TimerInput_ParseMinutes_Valid(string text, int minutes) => Assert.Equal(minutes, TimerInput.ParseMinutes(text));

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("601")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("2.5")]
    [InlineData("99999999999")]
    public void TimerInput_ParseMinutes_Invalid(string text) => Assert.Null(TimerInput.ParseMinutes(text));
}

public class NotesStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-notes-" + Guid.NewGuid().ToString("N"));
    private string PathFor => Path.Combine(_dir, "notes.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void NotesStore_AddAndLatest_NewestFirst()
    {
        var s = new NotesStore(PathFor);
        Assert.True(s.Add("one", T0));
        Assert.True(s.Add("two", T0.AddMinutes(1)));
        Assert.True(s.Add("three", T0.AddMinutes(2)));
        Assert.True(s.Add("four", T0.AddMinutes(3)));
        Assert.Equal(["four", "three", "two"], s.Latest(3).Select(n => n.Text));
    }

    [Fact]
    public void NotesStore_SurvivesRestart()
    {
        new NotesStore(PathFor).Add("اشتري خبز", T0);
        Assert.Equal("اشتري خبز", Assert.Single(new NotesStore(PathFor).Latest(3)).Text);
    }

    [Fact]
    public void NotesStore_OneLineTrimmedMax500()
    {
        var s = new NotesStore(PathFor);
        s.Add("  a\r\nb\tc  ", T0);
        Assert.Equal("a b c", s.Latest(1)[0].Text);
        s.Add(new string('x', 900), T0);
        Assert.Equal(500, s.Latest(1)[0].Text.Length);
    }

    [Fact]
    public void NotesStore_EmptyNote_NotAdded()
    {
        var s = new NotesStore(PathFor);
        Assert.False(s.Add("   \n ", T0));
        Assert.Empty(s.Latest(3));
    }

    [Fact]
    public void NotesStore_Corrupt_MakesBadCopyAndStartsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(PathFor, "{ not json");
        var s = new NotesStore(PathFor);
        Assert.Empty(s.Latest(3));
        Assert.True(File.Exists(PathFor + ".bad"));
        Assert.True(s.Add("after", T0));
        Assert.Single(new NotesStore(PathFor).Latest(3));
    }

    [Fact]
    public void NotesStore_KeepsAtMost50()
    {
        var s = new NotesStore(PathFor);
        for (int i = 0; i < 60; i++) s.Add($"n{i}", T0.AddMinutes(i));
        var all = new NotesStore(PathFor).Latest(100);
        Assert.Equal(50, all.Count);
        Assert.Equal("n59", all[0].Text);
    }
}

public class DndStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DndState_Timed_EndsOnce()
    {
        var d = new DndState();
        d.TurnOn(TimeSpan.FromMinutes(30), T0);
        Assert.True(d.IsOn(T0.AddMinutes(29)));
        Assert.False(d.TakeEnded(T0.AddMinutes(29)));
        Assert.True(d.TakeEnded(T0.AddMinutes(31)));
        Assert.False(d.TakeEnded(T0.AddMinutes(32)));
        Assert.False(d.IsOn(T0.AddMinutes(31)));
    }

    [Fact]
    public void DndState_UntilOff_NeverEndsByItself()
    {
        var d = new DndState();
        d.TurnOn(null, T0);
        Assert.True(d.IsOn(T0.AddDays(3)));
        Assert.False(d.TakeEnded(T0.AddDays(3)));
    }

    [Fact]
    public void DndState_TurnOff_EndsNow()
    {
        var d = new DndState();
        d.TurnOn(TimeSpan.FromHours(1), T0);
        d.TurnOff();
        Assert.False(d.IsOn(T0.AddMinutes(1)));
        Assert.False(d.TakeEnded(T0.AddHours(2)));
    }
}

public class BrainEndTests
{
    [Fact]
    public void Brain_End_RemovesTheEvent()
    {
        var t0 = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        var b = new B();
        b.Post(EventKind.TimerDone, t0, TimeSpan.FromSeconds(60));
        Assert.Equal("timer-done", b.Tick(t0.AddSeconds(1)).ExpressionKey);
        b.End(EventKind.TimerDone);
        Assert.Equal("idle", b.Tick(t0.AddSeconds(2)).ExpressionKey);
    }
}

public class NotesStoreLockedTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-notes-lock-" + Guid.NewGuid().ToString("N"));
    private string PathFor => Path.Combine(_dir, "notes.json");

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void NotesStore_UnreadableAtStart_NeverOverwritesTheFile()
    {
        new NotesStore(PathFor).Add("old note", T0);
        NotesStore s;
        using (new FileStream(PathFor, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            s = new NotesStore(PathFor);
            Assert.True(s.Add("new note", T0.AddMinutes(1)));
            Assert.True(s.SaveFailed);
        }
        Assert.True(s.Add("later note", T0.AddMinutes(2)));   // the file can be read again: merge, then save
        var all = new NotesStore(PathFor).Latest(10).Select(n => n.Text).ToList();
        Assert.Equal(["later note", "new note", "old note"], all);
    }
}

public class HelloTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hello_SoonAfterSignIn() => Assert.True(Hello.ShouldGreet(Now, Now.AddMinutes(-2), testMode: false, simulateBoot: false));

    [Fact]
    public void Hello_NotHoursAfterSignIn() => Assert.False(Hello.ShouldGreet(Now, Now.AddHours(-3), testMode: false, simulateBoot: false));

    [Fact]
    public void Hello_UnknownSignIn_NoHello() => Assert.False(Hello.ShouldGreet(Now, null, testMode: false, simulateBoot: false));

    [Fact]
    public void Hello_TestMode_OnlyWhenAsked()
    {
        Assert.False(Hello.ShouldGreet(Now, Now.AddMinutes(-1), testMode: true, simulateBoot: false));
        Assert.True(Hello.ShouldGreet(Now, null, testMode: true, simulateBoot: true));
    }
}
