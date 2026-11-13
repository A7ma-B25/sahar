using Sahar.Core.Activities;

namespace Sahar.Core.Tests.Activities;

public class ClaudeBoardTests
{
    private static readonly DateTimeOffset T = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

    private static ClaudeSignal S(string ev, string session = "s1", ClaudeStep step = ClaudeStep.None, string? target = null, bool asking = false,
        string project = "sahar") => new(ev, session, project, step, target, null, asking);

    [Fact]
    public void Board_StepText_AndMinutes()
    {
        var b = new ClaudeBoard();
        b.Apply(S("UserPromptSubmit"), T);
        b.Apply(S("PreToolUse", step: ClaudeStep.Editing, target: "OverlayPlan.cs"), T.AddMinutes(1));
        var a = Assert.Single(b.Activities(T.AddMinutes(4)));
        Assert.Equal((ActivityKind.ClaudeWorking, "Claude عم يعدّل", "٤ د"), (a.Kind, a.Text, a.Meta));
        Assert.Equal("OverlayPlan.cs", b.Sessions.Single().Target);
    }

    [Fact]
    public void Board_DoneMoment_Minutes()
    {
        var b = new ClaudeBoard();
        b.Apply(S("UserPromptSubmit"), T);
        b.Apply(S("Stop"), T.AddMinutes(12));
        Assert.Empty(b.Activities(T.AddMinutes(12)));
        var m = Assert.Single(b.TakeMoments());
        Assert.Equal((ActivityKind.ClaudeDone, "Claude خلّص", "١٢ د"), (m.Kind, m.Text, m.Meta));
        Assert.Empty(b.TakeMoments());
    }

    [Fact]
    public void Board_StopWithoutWork_NoMoment()
    {
        var b = new ClaudeBoard();
        b.Apply(S("Stop"), T);
        Assert.Empty(b.TakeMoments());
    }

    [Fact]
    public void Board_Ask_FirstAndAccent()
    {
        var b = new ClaudeBoard();
        b.Apply(S("PreToolUse", step: ClaudeStep.Running), T);
        b.Apply(S("PermissionRequest", step: ClaudeStep.Running, asking: true), T.AddSeconds(1));
        var ask = b.Activities(T.AddSeconds(2)).Single(a => a.Kind == ActivityKind.ClaudeAsk);
        Assert.True(ask.Asking);
        Assert.Equal("Claude ناطر جوابك", ask.Text);
        Assert.True(b.AskingNow);
    }

    [Fact]
    public void ClaudeBoard_DoneInOtherSession_KeepsAsk()
    {
        var b = new ClaudeBoard();
        b.Apply(S("PermissionRequest", "one", ClaudeStep.Running, asking: true), T);
        b.Apply(S("UserPromptSubmit", "two"), T);
        b.Apply(S("Stop", "two"), T.AddMinutes(1));
        Assert.Contains(b.Activities(T.AddMinutes(1)), a => a.Kind == ActivityKind.ClaudeAsk && a.Id == "claude-ask:one");
    }

    [Theory]
    [InlineData("PreToolUse")]
    [InlineData("PostToolUse")]
    [InlineData("PostToolUseFailure")]
    [InlineData("Stop")]
    [InlineData("UserPromptSubmit")]
    [InlineData("SessionEnd")]
    public void Board_AskClearedBySameSessionNext(string ev)
    {
        var b = new ClaudeBoard();
        b.Apply(S("Notification", asking: true), T);
        b.Apply(S(ev), T.AddSeconds(5));
        Assert.DoesNotContain(b.Activities(T.AddSeconds(5)), a => a.Kind == ActivityKind.ClaudeAsk);
    }

    [Fact]
    public void Board_SessionEnd_Removes()
    {
        var b = new ClaudeBoard();
        b.Apply(S("UserPromptSubmit"), T);
        b.Apply(S("SessionEnd"), T.AddMinutes(1));
        Assert.Empty(b.Activities(T.AddMinutes(1)));
        Assert.Empty(b.Sessions);
    }

    [Fact]
    public void Board_SilentSession_GoesAfterHalfAnHour()
    {
        var b = new ClaudeBoard();
        b.Apply(S("PreToolUse", step: ClaudeStep.Reading), T);
        Assert.Single(b.Activities(T.AddMinutes(29)));
        Assert.Empty(b.Activities(T.AddMinutes(31)));
    }

    [Fact]
    public void Board_Todos_KeptFromTheLastTodoWrite()
    {
        var b = new ClaudeBoard();
        b.Apply(S("PreToolUse") with { Todos = [new ClaudeTodo("a", "completed"), new ClaudeTodo("b", "pending")] }, T);
        b.Apply(S("PreToolUse", step: ClaudeStep.Reading), T.AddSeconds(1));
        Assert.Equal(2, b.Sessions.Single().Todos.Count);
    }
}
