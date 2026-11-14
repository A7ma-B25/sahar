using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

/// <summary>The fresh review of 0.13 (2026-11-14): each finding pinned before its fix.</summary>
public class ReviewFixTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    // Critical 1: a copy dialog that stays at 100 % is ended once and never picked up again.
    [Fact]
    public void Copy_EndedDialog_IsNotPickedUpAgain()
    {
        var w = new CopyWatch();
        Assert.True(w.Accept(42));
        Assert.Equal(CopyStep.Done, w.Tick(true, "100% complete", T).Step);
        Assert.False(w.Accept(42));   // still on screen at 100 %: no restart, no recursion
        Assert.True(w.Accept(43));
    }

    [Fact]
    public void Copy_Progress_ThenClosed()
    {
        var w = new CopyWatch();
        w.Accept(7);
        var s = w.Tick(true, "42% complete", T);
        Assert.Equal((CopyStep.Progress, 42.0), (s.Step, s.Percent));
        Assert.Equal(CopyStep.Done, w.Tick(false, "", T.AddSeconds(2)).Step);
        Assert.Equal(CopyStep.None, w.Tick(false, "", T.AddSeconds(3)).Step);
    }

    // Important 2: an ask waits as long as Claude waits.
    [Fact]
    public void Claude_AskingSession_NeverExpires()
    {
        var b = new ClaudeBoard();
        b.Apply(new ClaudeSignal("PermissionRequest", "s", "p", ClaudeStep.Running, null, null, true), T);
        Assert.Contains(b.Activities(T.AddHours(2)), a => a.Kind == ActivityKind.ClaudeAsk);
    }

    // Important 3: async hooks arrive out of order; a Pre/Post within 2 s of the ask is the same tool, not an answer.
    [Fact]
    public void Claude_ToolHookRightAfterAsk_KeepsAsk()
    {
        var b = new ClaudeBoard();
        b.Apply(new ClaudeSignal("PermissionRequest", "s", "p", ClaudeStep.Running, null, null, true), T);
        b.Apply(new ClaudeSignal("PreToolUse", "s", "p", ClaudeStep.Running, null, null, false), T.AddSeconds(0.5));
        Assert.True(b.AskingNow);
        b.Apply(new ClaudeSignal("PostToolUse", "s", "p", ClaudeStep.Running, null, null, false), T.AddSeconds(5));
        Assert.False(b.AskingNow);
    }

    // Important 5: «وإنت غايب» keeps what fullscreen keeps (finishes and the quiet alerts), not every moment.
    [Fact]
    public void Away_OnlyKeptKinds()
    {
        var a = new AwayLog();
        a.Lock(T);
        a.Note(ActivityKind.Screenshot, T.AddMinutes(1));
        a.Note(ActivityKind.Volume, T.AddMinutes(1));
        Assert.Null(a.Unlock(T.AddMinutes(5)));
        a.Lock(T);
        a.Note(ActivityKind.ClaudeDone, T.AddMinutes(1));
        a.Note(ActivityKind.WeatherAlert, T.AddMinutes(2), WeatherAlert.Rain);
        Assert.Equal(2, a.Unlock(T.AddMinutes(5))!.Total);
    }

    // Important 6: the hooks follow Sahar when she moves; not linked, nothing is written.
    [Fact]
    public void Link_Follow_RepointsOnlyOurMovedHooks()
    {
        var old = ClaudeLink.Install(null, @"D:\Old\Sahar.Engine.exe").Json!;
        var moved = ClaudeLink.Follow(old, @"C:\New\Sahar.Engine.exe");
        Assert.True(moved.Ok);
        Assert.True(ClaudeLink.Linked(moved.Json, @"C:\New\Sahar.Engine.exe"));
        Assert.False(ClaudeLink.Follow(moved.Json, @"C:\New\Sahar.Engine.exe").Ok);   // already here: no write
        Assert.False(ClaudeLink.Follow("""{"theme":"dark"}""", @"C:\New\Sahar.Engine.exe").Ok);   // never linked: untouched
        Assert.False(ClaudeLink.Follow(null, @"C:\New\Sahar.Engine.exe").Ok);
    }

    // Important 7: a failed zip leaves no file behind.
    [Fact]
    public void Zip_Fails_NoPartialFile()
    {
        var dir = Directory.CreateTempSubdirectory("zip").FullName;
        var a = Path.Combine(dir, "a.txt");
        File.WriteAllText(a, "a");
        var target = Path.Combine(dir, "out.zip");
        Assert.False(ShelfThings.Zip([a, Path.Combine(dir, "gone.txt")], target));
        Assert.Equal(["a.txt"], Directory.GetFiles(dir).Select(Path.GetFileName));
        Assert.True(ShelfThings.Zip([a], target));
        Assert.True(File.Exists(target));
    }

    // Important 9: a failed fetch does not wait an hour.
    [Fact]
    public void Fetch_FailedTry_CanRetryAtOnce()
    {
        var g = new FetchGate();
        Assert.True(g.TryStart(T));
        Assert.False(g.TryStart(T.AddMinutes(1)));
        g.Failed();
        Assert.True(g.TryStart(T.AddMinutes(2)));
        Assert.False(g.TryStart(T.AddMinutes(30)));
        Assert.True(g.TryStart(T.AddMinutes(63)));
    }
}
