using System.Net;
using System.Text;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Online;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Tests.Mind.Online;

internal sealed class MemBox : ISecretBox
{
    public readonly Dictionary<string, byte[]> Files = [];
    public bool Locked;
    public byte[]? Read(string name) => Locked ? null : Files.TryGetValue(name, out var b) ? b : null;
    public void Write(string name, ReadOnlySpan<byte> data) { if (Locked) throw new MindStoreException("locked"); Files[name] = data.ToArray(); }
    public void Delete(string name) => Files.Remove(name);
}

/// <summary>0.17 Task 2: the quota, the sealed keys, the persisted turns, the key test (spec §4, §7).</summary>
public sealed class OnlineStoreTests
{
    private DateTimeOffset _now = new(2026, 11, 27, 23, 50, 0, TimeSpan.FromHours(3));

    [Fact]
    public void OnlineState_CountsEveryAttempt_AndPersists()
    {
        var box = new MemBox();
        var s = new OnlineState(box, () => _now);
        s.Count("groq"); s.Count("groq"); s.Count("openrouter");
        var again = new OnlineState(box, () => _now);
        Assert.Equal((2, (int?)null), again.Usage("groq"));
        Assert.Equal(1, again.Usage("openrouter").Used);
        Assert.False(again.Exhausted("groq"));
    }

    [Fact]
    public void OnlineState_ProviderNumbersWin_AndZeroRemainingExhausts()
    {
        var s = new OnlineState(new MemBox(), () => _now);
        s.Count("groq");
        s.Seen("groq", new Sahar.Core.Mind.Runtime.RateInfo(990, 1000, null));
        Assert.Equal((10, (int?)1000), s.Usage("groq"));
        s.SetRemote("openrouter", 50, 50);
        Assert.True(s.Exhausted("openrouter"));
        s.Exhaust("groq");
        Assert.True(s.Exhausted("groq"));
    }

    [Fact]
    public void OnlineState_NewDay_Resets()
    {
        var box = new MemBox();
        var s = new OnlineState(box, () => _now);
        s.Count("groq");
        s.Exhaust("groq");
        Assert.True(s.TellOnce());
        Assert.False(s.TellOnce());
        _now = _now.AddMinutes(20);   // 00:10 the next day
        Assert.False(s.Exhausted("groq"));
        Assert.Equal(0, s.Usage("groq").Used);
        Assert.True(s.TellOnce());
    }

    [Fact]
    public void OnlineState_LockedBox_StillCountsInMemory()
    {
        var box = new MemBox { Locked = true };
        var s = new OnlineState(box, () => _now);
        s.Count("groq");
        Assert.Equal(1, s.Usage("groq").Used);
    }

    [Fact]
    public void OnlineKeys_TrimsAndRefusesEmpty()
    {
        var box = new MemBox();
        var k = new OnlineKeys(box);
        Assert.False(k.Set("groq", "   \n"));
        Assert.Empty(k.Providers());
        Assert.True(k.Set("groq", "  gsk_abc\r\n"));
        Assert.Equal("gsk_abc", new OnlineKeys(box).Get("groq"));
        Assert.False(k.Set("nope", "x"));   // only known providers
        Assert.True(k.Set("openrouter", "sk-or-1"));
        Assert.Equal(["groq", "openrouter"], k.Providers().Order());
        k.Remove("groq");
        Assert.Null(k.Get("groq"));
        Assert.False(k.Set("custom", new string('a', 600)));   // no key is that long
    }

    [Fact]
    public void OnlineKeys_StoredOnlyInTheBox()
    {
        var box = new MemBox();
        new OnlineKeys(box).Set("groq", "gsk_secret");
        Assert.Equal([OnlineKeys.File], box.Files.Keys);
    }

    [Fact]
    public void TurnHistory_PersistsCapsAndPrunesByDays()
    {
        var box = new MemBox();
        int days = 7;
        var h = new TurnHistory(box, () => _now, () => days);
        for (int i = 0; i < 45; i++) h.Add("u" + i, "h" + i, i % 2 == 0 ? "local" : "online");
        var r = new TurnHistory(box, () => _now, () => days).Recent();
        Assert.Equal(TurnHistory.Max, r.Count);
        Assert.Equal("u44", r[^1].User);
        Assert.Equal("online", r[^2].Brain);
        _now = _now.AddDays(8);
        Assert.Empty(new TurnHistory(box, () => _now, () => days).Recent());
    }

    [Fact]
    public void TurnHistory_EmptyLinesNotKept()
    {
        var h = new TurnHistory(new MemBox(), () => _now, () => 30);
        h.Add("", "x", "local");
        h.Add("x", " ", "local");
        Assert.Empty(h.Recent());
    }

    [Fact]
    public async Task KeyCheck_OpenRouter_ReadsTheFreeDailyCounter()
    {
        var f = new FakeProvider(new FakeProvider.Answer(HttpStatusCode.OK,
            """{"data":{"label":"x","is_free_tier":true,"free_model_daily_requests":{"used":12,"limit":50,"remaining":38}}}"""), Models());
        var r = await KeyCheck.RunAsync(f, ProviderProfiles.OpenRouter(), "sk-or", default);
        Assert.Equal(new KeyResult("ok", 12, 50), r);
        Assert.EndsWith("/api/v1/key", f.Seen[0].Url);
    }

    [Fact]
    public async Task KeyCheck_OpenRouter_NoneLeftIsQuota()
    {
        var f = new FakeProvider(new FakeProvider.Answer(HttpStatusCode.OK, """{"data":{"free_model_daily_requests":{"used":50,"limit":50,"remaining":0}}}"""), Models());
        Assert.Equal("quota", (await KeyCheck.RunAsync(f, ProviderProfiles.OpenRouter(), "k", default)).Code);
    }

    private static FakeProvider.Answer Models() => new(HttpStatusCode.OK, """{"data":[{"id":"google/gemma-4-26b-a4b-it:free"},{"id":"google/gemma-4-31b-it:free"},{"id":"openai/gpt-oss-120b"}]}""");

    [Theory]
    [InlineData(200, "ok")]
    [InlineData(401, "key-bad")]
    [InlineData(403, "blocked")]
    [InlineData(429, "quota")]
    public async Task KeyCheck_Groq_ModelsEndpoint(int status, string code)
    {
        var f = new FakeProvider(new FakeProvider.Answer((HttpStatusCode)status, """{"data":[{"id":"openai/gpt-oss-120b"}]}"""));
        Assert.Equal(code, (await KeyCheck.RunAsync(f, ProviderProfiles.Groq(), "k", default)).Code);
        Assert.EndsWith("/openai/v1/models", f.Seen[0].Url);
        Assert.Equal("Bearer k", f.Seen[0].Req.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task KeyCheck_NoNetwork_IsOffline()
    {
        var f = new FakeProvider(FakeProvider.Fail(HttpRequestError.NameResolutionError));
        Assert.Equal("offline", (await KeyCheck.RunAsync(f, ProviderProfiles.Groq(), "k", default)).Code);
    }
}
