using System.Text;
using Sahar.Core.Chat;
using Sahar.Core.Mind.Memory;
using Xunit;

namespace Sahar.Core.Tests.Chat;

public class ChatStoreTests
{
    private sealed class Box : ISecretBox
    {
        public readonly Dictionary<string, byte[]> Files = [];
        public bool Locked;
        /// <summary>Reads fail (the mutex is held elsewhere), writes still work: the review I5 case.</summary>
        public bool Busy;
        public readonly List<string> Writes = [];
        public byte[]? Read(string name) => Locked || Busy ? null : Files.TryGetValue(name, out var b) ? b : null;
        public bool TryRead(string name, out byte[]? data) { data = Read(name); return !Locked && !Busy; }
        public IReadOnlyList<string> Names() => [.. Files.Keys];
        public void Write(string name, ReadOnlySpan<byte> data) { if (Locked) throw new MindStoreException("locked"); Files[name] = data.ToArray(); Writes.Add(name); }
        public void Delete(string name) { if (Locked) throw new MindStoreException("locked"); Files.Remove(name); }
    }

    private DateTimeOffset _now = new(2026, 11, 20, 9, 30, 0, TimeSpan.FromHours(3));
    private int _days = 30;
    private ChatStore Store(Box box) => new(box, () => _now, () => _days);
    private ChatEntry Me(string text) => new(_now, ChatWho.Me, ChatLine.Text, text);
    private ChatEntry Her(string text) => new(_now, ChatWho.Her, ChatLine.Text, text);

    [Fact]
    public void Append_NewConversation_TitleFromTheFirstLineAboutFourWords()
    {
        var box = new Box();
        var s = Store(box);
        var id = s.Append(null, Me("صباح الخير يا سحورة كيفك اليوم"));
        s.Append(id, Her("صباح النور!"));
        var list = s.List();
        Assert.Single(list);
        Assert.Equal("صباح الخير يا سحورة", list[0].Title);
        Assert.Equal(2, list[0].Count);
        Assert.Equal("صباح النور!", list[0].LastLine);
        Assert.Equal(2, s.Load(id).Count);
        Assert.Equal(ChatWho.Her, s.Load(id)[1].Who);
    }

    [Fact]
    public void List_NewestFirst_RenameAndDelete()
    {
        var s = Store(new Box());
        var a = s.Append(null, Me("أول"));
        _now = _now.AddMinutes(5);
        var b = s.Append(null, Me("تاني"));
        Assert.Equal([b, a], s.List().Select(c => c.Id));
        s.Rename(a, "سمّيته");
        Assert.Equal("سمّيته", s.List().Single(c => c.Id == a).Title);
        s.Delete(b);
        Assert.Equal([a], s.List().Select(c => c.Id));
        Assert.Empty(s.Load(b));
    }

    [Fact]
    public void Prune_DropsMessagesPastTheDays_AndEmptyConversationsDisappear()
    {
        var box = new Box();
        var s = Store(box);
        var old = s.Append(null, Me("قديم"));
        _now = _now.AddDays(20);
        var mixed = s.Append(null, Me("بالنص"));
        _now = _now.AddDays(11);   // the first is now 31 days old, the second 11
        s.Append(mixed, Her("جديد"));
        s.Prune();
        Assert.DoesNotContain(s.List(), c => c.Id == old);
        Assert.Equal(2, s.Load(mixed).Count);
        _now = _now.AddDays(20);   // «بالنص» is 31 days old now, «جديد» 20
        s.Prune();
        Assert.Equal(["جديد"], s.Load(mixed).Select(m => m.Text));
        Assert.False(box.Files.ContainsKey(ChatStore.FileName(old)));
    }

    [Fact]
    public void Prune_FollowsTheDaysSetting()
    {
        var s = Store(new Box());
        var id = s.Append(null, Me("هلا"));
        _now = _now.AddDays(8);
        _days = 7;
        Assert.Empty(s.List());
        _days = 90;
        Assert.Single(s.List());
    }

    [Fact]
    public void Files_HoldNoPlaintext_ThroughTheBox()
    {
        // The box encrypts; the store never writes anything outside it (names carry no text).
        var box = new Box();
        var s = Store(box);
        s.Append(null, Me("SENTINEL-سطر-سري"));
        Assert.All(box.Files.Keys, k => Assert.DoesNotContain("SENTINEL", k));
        Assert.Contains(box.Files.Values, v => Encoding.UTF8.GetString(v).Contains("SENTINEL"));   // the fake box is plain: the real one seals it
    }

    [Fact]
    public void LockedBox_ChatStillWorksForTheSession_NotSaved()
    {
        var box = new Box { Locked = true };
        var s = Store(box);
        var id = s.Append(null, Me("بدون حفظ"));
        Assert.False(s.Saving);
        Assert.Single(s.Load(id));
        Assert.Single(s.List());
        Assert.Empty(box.Files);
    }

    [Fact]
    public void Forget_EmptiesTheSessionCopyToo()
    {
        var box = new Box();
        var s = Store(box);
        s.Append(null, Me("شي"));
        box.Files.Clear();   // «انسي كل شي» shredded the folder
        s.Forget();
        Assert.Empty(s.List());
    }

    // ---- review I5 ----

    private static string IndexText(Box box) => Encoding.UTF8.GetString(box.Files[ChatStore.IndexName]);

    [Fact]
    public void BusyIndexRead_NeverWritesTheIndex_AndOlderChatsComeBackLater()
    {
        var box = new Box();
        var old = Store(box).Append(null, Me("قديم"));
        box.Busy = true;   // her other process holds the box this moment
        var s = Store(box);
        _now = _now.AddMinutes(1);
        var fresh = s.Append(null, Me("جديد"));
        Assert.False(s.Saving);
        Assert.Contains(old, IndexText(box));   // the older chat is still in the index (it was orphaned)
        Assert.DoesNotContain(fresh, IndexText(box));
        box.Busy = false;
        _now = _now.AddMinutes(2);
        Assert.Equal([fresh, old], s.List().Select(c => c.Id));
        Assert.True(s.Saving);
        Assert.Contains(old, IndexText(box));
        Assert.Contains(fresh, IndexText(box));
        Assert.Single(Store(box).Load(fresh));
    }

    [Fact]
    public void NewConversation_TheIndexIsWrittenBeforeItsFile()
    {
        var box = new Box();
        var id = Store(box).Append(null, Me("هلا"));
        Assert.True(box.Writes.IndexOf(ChatStore.IndexName) < box.Writes.IndexOf(ChatStore.FileName(id)));   // a crash between them leaves no orphan
    }

    [Fact]
    public void Prune_DeletesOrphanFiles_AndRowsWithoutAFile()
    {
        var box = new Box();
        var s = Store(box);
        var kept = s.Append(null, Me("باقي"));
        var gone = s.Append(null, Me("راح"));
        box.Files.Remove(ChatStore.FileName(gone));          // a crash before its file
        box.Files[ChatStore.FileName("orphan01")] = [1, 2];   // a file no index row names
        box.Files["memory-facts"] = [3];                       // not hers: never touched
        var t = Store(box);
        t.Prune();
        Assert.False(box.Files.ContainsKey(ChatStore.FileName("orphan01")));
        Assert.True(box.Files.ContainsKey("memory-facts"));
        Assert.DoesNotContain(gone, IndexText(box));
        Assert.Contains(kept, IndexText(box));
    }

    [Fact]
    public void Prune_WithTheIndexUnreadable_DeletesNothing()
    {
        var box = new Box();
        var id = Store(box).Append(null, Me("هلا"));
        box.Busy = true;
        Store(box).Prune();
        Assert.True(box.Files.ContainsKey(ChatStore.FileName(id)));
        Assert.Contains(id, IndexText(box));
    }

    [Fact]
    public void AskMessages_KeepTheirAnswer()
    {
        var s = Store(new Box());
        var id = s.Append(null, new ChatEntry(_now, ChatWho.Her, ChatLine.Ask, "بدي احفظ ملاحظة. بتسمحلي؟") { Answer = "yes" });
        Assert.Equal("yes", s.Load(id)[0].Answer);
    }

    [Fact]
    public void Brain_RoundTrips_AndOldLinesHaveNone()
    {
        var box = new Box();
        var id = Store(box).Append(null, Me("اشرحلي شي"));
        Store(box).Append(id, Her("هيك…") with { Brain = "online" });
        var again = Store(box).Load(id);
        Assert.Null(again[0].Brain);
        Assert.Equal("online", again[1].Brain);
    }
}

public class ChatStoreRealBoxTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("chatbox").FullName;
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void WorksInTheRealSecretBox_EncryptedAtRest_AndGoneAfterForgetAll()
    {
        var box = new SecretBox(_dir, new TestKeyWrap());
        var now = DateTimeOffset.Now;
        var s = new ChatStore(box, () => now);
        var id = s.Append(null, new ChatEntry(now, ChatWho.Me, ChatLine.Text, "SENTINEL سطر سري"));
        Assert.True(s.Saving);
        foreach (var f in Directory.EnumerateFiles(_dir))
            Assert.DoesNotContain("SENTINEL", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f)));
        Assert.Single(new ChatStore(new SecretBox(_dir, new TestKeyWrap()), () => now).Load(id));
        var order = new List<string>();
        box.Deleting += order.Add;
        box.ForgetAll();
        Assert.Equal(SecretBox.KeyFile, order[0]);   // the key goes first (crypto-shred)
        Assert.Empty(new ChatStore(new SecretBox(_dir, new TestKeyWrap()), () => now).List());
    }

    [Fact]
    public void RealBox_TryRead_MissingIsTrue_AndNamesListsItsFiles()
    {
        var box = new SecretBox(_dir, new TestKeyWrap());
        Assert.True(((ISecretBox)box).TryRead("chat-none", out var none));
        Assert.Null(none);
        box.Write("chat-abc", [1, 2, 3]);
        Assert.Contains("chat-abc", box.Names());
        Assert.True(box.TryRead("chat-abc", out var data));
        Assert.Equal([1, 2, 3], data);
    }
}
