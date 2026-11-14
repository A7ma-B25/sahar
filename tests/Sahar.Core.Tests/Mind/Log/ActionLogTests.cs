using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Log;

/// <summary>C3: «شو عملت اليوم» — through the encrypted box only, 30 days, undo while it still means something.</summary>
public sealed class ActionLogTests
{
    private sealed class Box : ISecretBox
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public byte[]? Read(string name) => Files.TryGetValue(name, out var b) ? b : null;
        public void Write(string name, ReadOnlySpan<byte> data) => Files[name] = data.ToArray();
        public void Delete(string name) => Files.Remove(name);
    }

    /// <summary>A timer: its undo means something only while "running" holds its token.</summary>
    private sealed class FakeTimer : ITool
    {
        public string? Running = "t1";
        public int Undos;
        public ToolSpec Spec => ToolCatalog.Get("timer.start")!;
        public ToolOutcome Run(ToolCall call) => new(true, "");
        public bool Undo(string token)
        {
            if (Running != token) return false;
            Running = null;
            Undos++;
            return true;
        }
    }

    private DateTimeOffset _now = new(2026, 11, 13, 10, 12, 0, TimeSpan.FromHours(3));
    private readonly Box _box = new();
    private readonly FakeTimer _timer = new();

    private IActionLog Log() => MindParts.ActionLog(_box, t => t == "timer.start" ? _timer : null, () => _now);

    [Fact]
    public void Log_AppendsAndReadsBack_ThroughTheBoxOnly()
    {
        var log = Log();
        var e = log.Append("timer.start", "شغّلت مؤقت ٢٥ دقيقة", ActSource.User, "ركّزني شي ربع ساعة", ActionOutcome.Done, "t1");
        Assert.Single(_box.Files);
        Assert.Equal(ActionLog.File, _box.Files.Keys.Single());
        var again = Log().All().Single();   // a second reader (the settings app) sees the same line
        Assert.Equal(e, again);
        Assert.Equal(e, Log().Day(new DateOnly(2026, 11, 13)).Single());
        Assert.Empty(Log().Day(new DateOnly(2026, 11, 12)));
    }

    [Fact]
    public void Log_UndoOnlyWhileMeaningful()
    {
        var log = Log();
        var asked = log.Append("settings.set", "سألتك", ActSource.Self, null, ActionOutcome.AskedNo, "x");
        Assert.Null(asked.UndoToken);   // nothing changed, nothing to undo
        Assert.False(log.Undo(asked.Id));
        var noToken = log.Append("timer.start", "شغّلت", ActSource.User, null, ActionOutcome.Done, null);
        Assert.False(log.Undo(noToken.Id));
        var stale = log.Append("timer.start", "شغّلت", ActSource.User, null, ActionOutcome.Done, "old");
        Assert.False(log.Undo(stale.Id));   // that timer is gone: the inverse no longer applies
        Assert.Equal(ActionOutcome.Done, log.All().First(x => x.Id == stale.Id).Outcome);
        var live = log.Append("timer.start", "شغّلت", ActSource.User, null, ActionOutcome.Done, "t1");
        Assert.True(log.Undo(live.Id));
        Assert.Equal(1, _timer.Undos);
        var after = log.All().First(x => x.Id == live.Id);
        Assert.Equal(ActionOutcome.Undone, after.Outcome);
        Assert.Null(after.UndoToken);
        Assert.False(log.Undo(live.Id));   // once
        Assert.False(log.Undo("nope"));
        Assert.False(log.Undo(log.Append("note.save", "حفظت", ActSource.User, null, ActionOutcome.Done, "n1").Id));   // no such tool here
    }

    [Fact]
    public void Log_KeepsThirtyDays()
    {
        var log = Log();
        var old = log.Append("timer.start", "قديم", ActSource.User, null, ActionOutcome.Done, null);
        _now = _now.AddDays(29);
        var mid = log.Append("timer.start", "وسط", ActSource.User, null, ActionOutcome.Done, null);
        Assert.Equal(2, log.All().Count);
        _now = _now.AddDays(1).AddMinutes(1);
        Assert.Equal([mid.Id], log.All().Select(e => e.Id));
        log.Append("hush.on", "سكتّ", ActSource.User, null, ActionOutcome.Done, null);
        Assert.DoesNotContain(old.Id, System.Text.Encoding.UTF8.GetString(_box.Files[ActionLog.File]));   // pruned from the box too
    }

    [Fact]
    public void Log_FeedbackIsOneOfThree()
    {
        var log = Log();
        var e = log.Append("timer.start", "x", ActSource.Self, "بلّشت مكالمة", ActionOutcome.Done, null);
        log.Feedback(e.Id, 5);
        Assert.Equal(1, log.All().Single().Feedback);
        log.Feedback(e.Id, -2);
        Assert.Equal(-1, log.All().Single().Feedback);
        log.Feedback(e.Id, 0);
        Assert.Equal(0, log.All().Single().Feedback);
    }

    [Fact]
    public void Log_NewestFirst_AndWhyClipped()
    {
        var log = Log();
        log.Append("a", "1", ActSource.User, new string('x', 200), ActionOutcome.Done, null);
        _now = _now.AddMinutes(1);
        log.Append("b", "2", ActSource.User, null, ActionOutcome.Done, null);
        Assert.Equal(["b", "a"], log.All().Select(e => e.Tool));
        Assert.Equal(80, log.All()[1].Why!.Length);
    }

    [Fact]
    public void Log_BrokenBoxStartsEmpty()
    {
        _box.Files[ActionLog.File] = "not json"u8.ToArray();
        Assert.Empty(Log().All());
    }

    [Fact]
    public void Log_WorksThroughTheRealSecretBox()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sahar-log-" + Guid.NewGuid().ToString("n"));
        try
        {
            var log = MindParts.ActionLog(MindParts.SecretBox(dir), _ => null, () => _now);
            var e = log.Append("timer.start", "شغّلت مؤقت ٥ دقيقة", ActSource.User, "مؤقت ٥", ActionOutcome.Done, "t");
            Assert.Equal(e.Id, MindParts.ActionLog(MindParts.SecretBox(dir), _ => null, () => _now).All().Single().Id);
            Assert.DoesNotContain("مؤقت", string.Concat(Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(File.ReadAllText)));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Log_AFailingBoxNeverThrows()
    {
        var log = MindParts.ActionLog(new ThrowingBox(), _ => null, () => _now);
        var e = log.Append("timer.start", "x", ActSource.User, null, ActionOutcome.Done, null);
        Assert.Equal("timer.start", e.Tool);
        Assert.Empty(log.All());
        log.Feedback(e.Id, 1);
        Assert.False(log.Undo(e.Id));
    }

    private sealed class ThrowingBox : ISecretBox
    {
        public byte[]? Read(string name) => throw new IOException("locked");
        public void Write(string name, ReadOnlySpan<byte> data) => throw new IOException("locked");
        public void Delete(string name) => throw new IOException("locked");
    }
}
