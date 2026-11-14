using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>Her reminders survive a restart (in the encrypted box), and the ones she set herself wait for the gate.</summary>
public sealed class ReminderBookTests
{
    private sealed class Box : ISecretBox
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public byte[]? Read(string name) => Files.TryGetValue(name, out var b) ? b : null;
        public void Write(string name, ReadOnlySpan<byte> data) => Files[name] = data.ToArray();
        public void Delete(string name) => Files.Remove(name);
    }

    private DateTimeOffset _now = new(2026, 11, 14, 9, 0, 0, TimeSpan.FromHours(3));
    private readonly Box _box = new();

    [Fact]
    public void Reminders_PersistAcrossRestart()
    {
        var book = new ReminderBook(_box, () => _now);
        var a = book.Add("اشرب مي", _now.AddMinutes(30), self: false);
        var b = book.Add("قوم تمشّى", _now.AddHours(2), self: true);
        Assert.Single(_box.Files);
        Assert.DoesNotContain(".", _box.Files.Keys.Single());
        var after = new ReminderBook(_box, () => _now);   // the engine started again
        Assert.Equal([a.Id, b.Id], after.Pending().Select(r => r.Id));
        Assert.True(after.Remove(a.Id));
        Assert.False(after.Remove(a.Id));
        Assert.Equal([b.Id], new ReminderBook(_box, () => _now).Pending().Select(r => r.Id));
    }

    [Fact]
    public void Reminders_DueOnesAndTheGate()
    {
        var book = new ReminderBook(_box, () => _now);
        var his = book.Add("x", _now.AddMinutes(1), self: false);
        var hers = book.Add("y", _now.AddMinutes(1), self: true);
        Assert.Empty(book.Due());
        _now = _now.AddMinutes(2);
        Assert.Equal(2, book.Due().Count);
        Assert.True(ReminderBook.MaySpeak(his, gateOpen: false));    // his own: like his timer, always
        Assert.False(ReminderBook.MaySpeak(hers, gateOpen: false));  // hers: only when she may speak on her own
        Assert.True(ReminderBook.MaySpeak(hers, gateOpen: true));
    }

    [Fact]
    public void Reminders_ABrokenBoxStartsEmpty()
    {
        _box.Files[ReminderBook.File] = "nope"u8.ToArray();
        Assert.Empty(new ReminderBook(_box, () => _now).Pending());
    }
}
