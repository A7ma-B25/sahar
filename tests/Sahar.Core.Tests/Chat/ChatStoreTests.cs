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
        public byte[]? Read(string name) => Locked ? null : Files.TryGetValue(name, out var b) ? b : null;
        public void Write(string name, ReadOnlySpan<byte> data) { if (Locked) throw new MindStoreException("locked"); Files[name] = data.ToArray(); }
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
        Assert.False(box.Files.ContainsKey("chat." + old));
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

    [Fact]
    public void AskMessages_KeepTheirAnswer()
    {
        var s = Store(new Box());
        var id = s.Append(null, new ChatEntry(_now, ChatWho.Her, ChatLine.Ask, "بدي احفظ ملاحظة. بتسمحلي؟") { Answer = "yes" });
        Assert.Equal("yes", s.Load(id)[0].Answer);
    }
}
