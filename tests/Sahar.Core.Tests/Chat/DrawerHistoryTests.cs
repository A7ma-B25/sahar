using Sahar.Core.Chat;
using Sahar.Core.Mind.Memory;
using Xunit;

namespace Sahar.Core.Tests.Chat;

/// <summary>Owner (0.16 review): «الدرج» remembers the last 24 h across restarts, sealed in her box, wiped by «انسي كل شي».</summary>
public class DrawerHistoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("drawerbox").FullName;
    private DateTimeOffset _now = new(2026, 11, 27, 9, 0, 0, TimeSpan.FromHours(3));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }
    private SecretBox Box() => new(_dir, new TestKeyWrap());
    private DrawerHistory History(SecretBox box) => new(box, () => _now);

    [Fact]
    public void RoundTrip_ExpiresAfter24h_AndGoesWithForgetAll()
    {
        var h = History(Box());
        h.Add("SENTINEL خلص المؤقت", "clock");
        _now = _now.AddHours(2);
        h.Add("وصلت رسالة", "bell");
        var again = History(Box());   // her next start
        Assert.Equal(["وصلت رسالة", "SENTINEL خلص المؤقت"], again.Items.Select(i => i.Text));
        Assert.Equal("clock", again.Items[1].Icon);
        foreach (var f in Directory.EnumerateFiles(_dir))
            Assert.DoesNotContain("SENTINEL", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(f)));
        _now = _now.AddHours(23);   // the first is 25 h old now
        Assert.Equal(["وصلت رسالة"], History(Box()).Items.Select(i => i.Text));
        var box = Box();
        box.ForgetAll();
        h.Forget();
        Assert.Empty(h.Items);
        Assert.Empty(History(Box()).Items);
    }

    [Fact]
    public void KeepsTheNewest40()
    {
        var h = History(Box());
        for (int i = 0; i < 45; i++) { _now = _now.AddMinutes(1); h.Add("سطر " + i, "bell"); }
        Assert.Equal(DrawerHistory.Max, h.Items.Count);
        Assert.Equal("سطر 44", h.Items[0].Text);
    }
}
