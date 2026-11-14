using System.Text;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Tests.Mind.Memory;

/// <summary>Plan 0.14 Task B1: the encrypted store (spec §7.2, §7.3, §7.8).</summary>
public sealed class StoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-store-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(3));
    private LearnConsent _consent = new(Chats: true, Imports: true, Examples: true, Music: true);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private SecretBox Box() => new(_dir, new TestKeyWrap());
    private MindStore Store(int days = 30) => new(Box(), () => _now, () => _consent, () => days);
    private ItemSource Chat(int daysAgo = 0, string? snippet = null) => new(LearnSource.Chats, _now.AddDays(-daysAgo), snippet);

    [Fact]
    public void Box_RoundTrip_TamperDetected()
    {
        var box = Box();
        var text = "بتحب الجواب قصير الصبح"u8.ToArray();
        box.Write("store", text);
        Assert.Equal(text, box.Read("store"));

        // At rest: nothing plain, the key is wrapped, the file is not the text.
        var raw = File.ReadAllBytes(Path.Combine(_dir, "store.enc"));
        Assert.True(File.Exists(Path.Combine(_dir, SecretBox.KeyFile)));
        Assert.DoesNotContain("الجواب", Encoding.UTF8.GetString(raw));

        // One flipped bit: GCM refuses it and Read says nothing (never garbage).
        raw[^1] ^= 0x01;
        File.WriteAllBytes(Path.Combine(_dir, "store.enc"), raw);
        Assert.Null(box.Read("store"));

        // A file moved under another name does not open either (the name is bound to it).
        box.Write("a", "one"u8);
        File.Copy(Path.Combine(_dir, "a.enc"), Path.Combine(_dir, "b.enc"), overwrite: true);
        Assert.Null(box.Read("b"));
        Assert.Equal("one"u8.ToArray(), box.Read("a"));

        Assert.Null(box.Read("missing"));
        Assert.Throws<ArgumentException>(() => box.Write("../out", "x"u8));
    }

    [Fact]
    public void Proposed_NeverInPrompt()
    {
        var store = Store();
        var p = store.Propose("الصبح بتحب الجواب قصير", ProfileCategory.Style, Chat(), Sensitivity.None)!;
        Assert.Equal(ItemStatus.Proposed, p.Status);
        Assert.Empty(store.ForPrompt("الصبح شو بدك", 400));

        Assert.True(store.Confirm(p.Id));
        var shaped = Assert.Single(store.ForPrompt("الصبح شو بدك", 400));
        Assert.Equal(ItemStatus.Confirmed, shaped.Status);
        Assert.Equal(_now, shaped.ConfirmedAt);

        var edited = store.Propose("بعد الشغل الطويل بتحب نكتة", ProfileCategory.WhatHelps, Chat(), Sensitivity.None)!;
        Assert.True(store.Edit(edited.Id, "بعد جلسة شغل طويلة بتحب نكتة صغيرة"));
        Assert.Equal(2, store.ForPrompt("", 400).Count);
        Assert.Contains(store.Profile(), i => i.Status == ItemStatus.Edited && i.Text == "بعد جلسة شغل طويلة بتحب نكتة صغيرة");

        // The budget is kept: a tiny budget takes the most relevant one only.
        var one = Assert.Single(store.ForPrompt("نكتة بعد الشغل", 12));
        Assert.Contains("نكتة", one.Text);
    }

    [Fact]
    public void Rejected_NotProposedAgain()
    {
        var store = Store();
        var p = store.Propose("بتحب القهوة بلا سكر", ProfileCategory.Likes, Chat(), Sensitivity.None)!;
        Assert.True(store.Reject(p.Id));
        Assert.Empty(store.Profile());
        Assert.Null(store.Propose("بتحب القهوة بلا سكر", ProfileCategory.Likes, Chat(), Sensitivity.None));
        Assert.Null(store.Propose("  بتحب  القهوة بلا سكر. ", ProfileCategory.Likes, Chat(1), Sensitivity.None));   // same words, other spacing
        // Kept as a hash only: the words are nowhere in the store.
        Assert.DoesNotContain("القهوة", store.Export());

        var d = store.Propose("بتصحى بكير", ProfileCategory.Routines, Chat(), Sensitivity.None)!;
        Assert.True(store.Delete(d.Id));
        Assert.Null(store.Propose("بتصحى بكير", ProfileCategory.Routines, Chat(), Sensitivity.None));
        Assert.False(store.Confirm(d.Id));
    }

    [Fact]
    public void Confidence_FromSources()
    {
        var store = Store();
        const string text = "الصبح بتحب الجواب قصير";
        Assert.Equal(Confidence.Low, store.Propose(text, ProfileCategory.Style, Chat(0), Sensitivity.None)!.Confidence);
        // The same moment again is not independent.
        Assert.Equal(Confidence.Low, store.Propose(text, ProfileCategory.Style, Chat(0), Sensitivity.None)!.Confidence);
        Assert.Equal(Confidence.Low, store.Propose(text, ProfileCategory.Style, Chat(1), Sensitivity.None)!.Confidence);
        var three = store.Propose(text, ProfileCategory.Style, Chat(2), Sensitivity.None)!;
        Assert.Equal(Confidence.Medium, three.Confidence);
        Assert.Single(store.Profile());   // one item, three sources
        Assert.Equal(4, three.Sources.Count);
        // A second kind of source makes three independent ones strong.
        var mixed = store.Propose(text, ProfileCategory.Style, new ItemSource(LearnSource.Examples, _now), Sensitivity.None)!;
        Assert.Equal(Confidence.High, mixed.Confidence);

        // Old sources count for nothing: five from last year are still weak.
        var old = Store();
        for (int i = 0; i < 5; i++) old.Propose("بتحب الشتي", ProfileCategory.Likes, Chat(300 + i), Sensitivity.None);
        Assert.Equal(Confidence.Low, Assert.Single(old.Profile(), x => x.Text == "بتحب الشتي").Confidence);
    }

    [Fact]
    public void NoConsent_NoProposal()
    {
        _consent = new LearnConsent(Chats: false, Imports: true);
        var store = Store();
        Assert.Null(store.Propose("بتحب الشاي", ProfileCategory.Likes, Chat(), Sensitivity.None));
        Assert.Equal(0, store.AddSamples(LearnSource.Examples, ["هلا والله"]));
        Assert.Equal(0, store.AddSamples(LearnSource.Music, ["x"]));
        Assert.Equal(1, store.AddSamples(LearnSource.Imports, ["يلا تمام"]));
        Assert.NotNull(store.Propose("بتحب الشاي", ProfileCategory.Likes, new ItemSource(LearnSource.Imports, _now), Sensitivity.None));
        _consent = new LearnConsent();
        Assert.Null(store.Propose("بتحب الشاي", ProfileCategory.Likes, new ItemSource(LearnSource.Imports, _now), Sensitivity.None));
        Assert.Equal(0, store.AddSamples(LearnSource.Imports, ["كمان وحدة"]));
    }

    [Fact]
    public void Sensitive_StaysProposed()
    {
        var store = Store();
        for (int i = 0; i < 6; i++)
            store.Propose("عندك ضغط دم", ProfileCategory.Values, Chat(i, "قال إنو عندو ضغط"), Sensitivity.Health);
        var item = Assert.Single(store.Profile());
        Assert.Equal(ItemStatus.Proposed, item.Status);
        Assert.Equal(Sensitivity.Health, item.Sensitive);
        Assert.All(item.Sources, s => Assert.Null(s.Snippet));   // no words kept from a private moment
        Assert.Empty(store.ForPrompt("ضغط", 400));
        // Only his own yes moves it, and the date of that yes is kept.
        Assert.True(store.Confirm(item.Id));
        Assert.Equal(_now, Assert.Single(store.ForPrompt("ضغط", 400)).ConfirmedAt);
    }

    [Fact]
    public void ForgetAll_WhileWriting_LeavesNothingReadable()
    {
        var mind = Store();
        mind.AddMemory("بدو يخلّص المشروع الجمعة", MemoryKind.Goal);
        mind.AddSamples(LearnSource.Examples, ["يلا منحكي بعدين"]);

        using var stop = new CancellationTokenSource();
        var writer = Task.Run(() =>
        {
            int n = 0;
            while (!stop.IsCancellationRequested) mind.AddMemory("ذكرى " + n++, MemoryKind.Fact);
        });
        Thread.Sleep(50);
        var settingsApp = Store();   // the second writer
        settingsApp.ForgetAll();
        stop.Cancel();
        writer.Wait();

        // The writer that saw the old key never makes a new one: nothing at all is left (review B2).
        Assert.False(Directory.Exists(_dir) && Directory.EnumerateFileSystemEntries(_dir).Any());
        Assert.Equal(0, mind.AddSamples(LearnSource.Examples, ["بعد النسيان"]));
        Assert.False(Directory.Exists(_dir) && Directory.EnumerateFileSystemEntries(_dir).Any());
        var fresh = Store();
        Assert.DoesNotContain(fresh.Memories(), m => m.Text == "بدو يخلّص المشروع الجمعة");
        Assert.Empty(fresh.Samples("", 10));
        // And a forget with nobody writing leaves an empty folder: nothing at all.
        fresh.ForgetAll();
        Assert.False(Directory.Exists(_dir) && Directory.EnumerateFileSystemEntries(_dir).Any());
        // The next write starts a fresh store.
        fresh.AddMemory("من جديد", MemoryKind.Fact);
        Assert.Equal("من جديد", Assert.Single(Store().Memories()).Text);
    }

    private sealed class NoWrap : IKeyWrap
    {
        public byte[] Wrap(byte[] key) => new TestKeyWrap().Wrap(key);
        public byte[]? Unwrap(byte[] wrapped) => null;   // another user, a copied disk, a damaged key
    }

    [Fact]
    public void UnopenableKey_RefusesWrites_KeepsEverything()
    {
        var store = Store();
        store.AddMemory("بدو يخلّص المشروع", MemoryKind.Goal);
        var key = File.ReadAllBytes(Path.Combine(_dir, SecretBox.KeyFile));
        var data = File.ReadAllBytes(Path.Combine(_dir, "store.enc"));
        var locked = new MindStore(new SecretBox(_dir, new NoWrap()), () => _now, () => _consent);
        Assert.Equal(StoreStatus.Locked, locked.Status);
        Assert.Null(locked.Propose("بتحب الشاي", ProfileCategory.Likes, Chat(), Sensitivity.None));
        Assert.Equal(0, locked.AddSamples(LearnSource.Examples, ["يلا"]));
        locked.AddMemory("شي جديد", MemoryKind.Fact);
        Assert.Equal(key, File.ReadAllBytes(Path.Combine(_dir, SecretBox.KeyFile)));   // never a new key over his
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(_dir, "store.enc")));        // never an empty store over his
        Assert.Equal("بدو يخلّص المشروع", Assert.Single(Store().Memories()).Text);
    }

    [Fact]
    public void DamagedFile_KeptAside_NotOverwritten()
    {
        var store = Store();
        store.AddMemory("أول ذكرى", MemoryKind.Fact);
        var path = Path.Combine(_dir, "store.enc");
        var raw = File.ReadAllBytes(path);
        raw[^1] ^= 1;
        File.WriteAllBytes(path, raw);
        store.AddMemory("بعد العطل", MemoryKind.Fact);
        var bad = Assert.Single(Directory.GetFiles(_dir, "store*.bad"));
        Assert.Equal(raw, File.ReadAllBytes(bad));
        Assert.Equal(StoreStatus.Recovered, store.Status);
        Assert.Equal("بعد العطل", Assert.Single(store.Memories()).Text);
    }

    [Fact]
    public void MutexHeld_RefusesInsteadOfWritingUnlocked()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var m = new Mutex(false, SecretBox.MutexName);
            m.WaitOne();
            held.Set();
            release.Wait();
            m.ReleaseMutex();
        });
        holder.Start();
        held.Wait();
        try
        {
            var store = new MindStore(new SecretBox(_dir, new TestKeyWrap(), TimeSpan.FromMilliseconds(30)), () => _now, () => _consent);
            store.AddMemory("بلا قفل", MemoryKind.Fact);
            Assert.False(Directory.Exists(_dir));
        }
        finally { release.Set(); holder.Join(); }
        Store().AddMemory("مع القفل", MemoryKind.Fact);
        Assert.Single(Store().Memories());
    }

    [Fact]
    public void Samples_DropSensitiveAndDistressLines_SnippetsScrubbed()
    {
        var store = Store();
        Assert.Equal(1, store.AddSamples(LearnSource.Examples, ["يلا تمام", "عندي سكري من زمان", "بدي موت وخلص"]));
        Assert.Equal(["يلا تمام"], store.Samples("", 10));
        var p = store.Propose("بتحب المشي", ProfileCategory.Likes, Chat(0, "ابعتلي على 0944123456"), Sensitivity.None)!;
        Assert.Equal("ابعتلي على [رقم]", p.Sources[0].Snippet);
    }

    [Fact]
    public void ForgetAll_KeyGoesFirst()
    {
        var box = Box();
        box.Write("store", "x"u8);
        var order = new List<string>();
        box.Deleting += order.Add;
        box.ForgetAll();
        Assert.Equal(SecretBox.KeyFile, order[0]);
        Assert.False(Directory.Exists(_dir));
    }

    [Fact]
    public void Caps_PruneUnconfirmedFirst()
    {
        var store = Store();
        var keep = store.Propose("أول شي أكّدو", ProfileCategory.Likes, Chat(), Sensitivity.None)!;
        store.Confirm(keep.Id);
        for (int i = 0; i < MindStore.MaxProfile + 5; i++)
        {
            _now = _now.AddMinutes(1);
            store.Propose($"اقتراح رقم {i}", ProfileCategory.Likes, Chat(), Sensitivity.None);
        }
        var all = store.Profile();
        Assert.Equal(MindStore.MaxProfile, all.Count);
        Assert.Contains(all, i => i.Id == keep.Id);                    // the confirmed one stays
        Assert.DoesNotContain(all, i => i.Text == "اقتراح رقم 0");      // the oldest proposals went first
        Assert.Contains(all, i => i.Text == $"اقتراح رقم {MindStore.MaxProfile + 4}");

        for (int i = 0; i < MindStore.MaxSamples + 3; i++) store.AddSamples(LearnSource.Examples, [$"مثال {i}"]);
        Assert.Equal(MindStore.MaxSamples, store.Style().Samples);
    }

    [Fact]
    public void Retention_DropsChatWordsAfterTheDays()
    {
        var store = Store(days: 7);
        store.Propose("بتحب المشي", ProfileCategory.Likes, Chat(0, "طلعت مشيت ساعة"), Sensitivity.None);
        store.AddSamples(LearnSource.Chats, ["شو عم تعمل هلق"]);
        store.AddSamples(LearnSource.Examples, ["هلا والله"]);
        _now = _now.AddDays(8);
        store.AddMemory("بعد أسبوع", MemoryKind.Event);   // any write applies the rule
        Assert.Null(Assert.Single(store.Profile()).Sources[0].Snippet);
        Assert.Equal(["هلا والله"], store.Samples("", 10));
    }

    [Fact]
    public void Memories_AddSearchDelete_AndExport()
    {
        var store = Store();
        var m = store.AddMemory("أخوه اسمو سامر", MemoryKind.Fact);
        store.AddMemory("بيحب فيروز الصبح", MemoryKind.Preference);
        Assert.Equal(2, store.Memories().Count);
        Assert.Equal("بيحب فيروز الصبح", Assert.Single(store.Memories("فيروز")).Text);
        var json = store.Export();
        Assert.Contains("فيروز", json);   // readable on purpose (the page warns)
        Assert.True(store.DeleteMemory(m.Id));
        Assert.Single(store.Memories());
        Assert.False(store.DeleteMemory("nope"));
    }

    [Fact]
    public void EmptyStore_AnswersEverything()
    {
        var store = Store();
        Assert.Empty(store.Profile());
        Assert.Empty(store.Memories());
        Assert.Empty(store.ForPrompt("x", 100));
        Assert.Empty(store.Samples("x", 3));
        Assert.Equal(0, store.Style().Samples);
        Assert.False(store.Confirm("x"));
        Assert.StartsWith("{", store.Export());
        store.ForgetAll();
    }

    [Fact]
    public void Parts_BuildTheRealStore()
    {
        var store = MindParts.Store(_dir, () => _now, () => _consent);
        Assert.IsType<MindStore>(store);
        Assert.IsType<SecretBox>(MindParts.SecretBox(_dir));
    }

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(SkipUnless = nameof(OnWindows), Skip = "DPAPI is Windows only")]
    public void Dpapi_Wrap()
    {
        var wrap = new DpapiKeyWrap();
        var key = new byte[32];
        Random.Shared.NextBytes(key);
        var wrapped = wrap.Wrap(key);
        Assert.NotEqual(key, wrapped);
        Assert.Equal(key, wrap.Unwrap(wrapped));
        wrapped[^1] ^= 1;
        Assert.Null(wrap.Unwrap(wrapped));
        Assert.IsType<DpapiKeyWrap>(KeyWrap.Default);
    }
}
