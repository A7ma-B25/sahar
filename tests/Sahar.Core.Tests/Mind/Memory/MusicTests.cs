using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Tests.Mind.Memory;

/// <summary>Plan 0.14 Task B2: what he listens to (titles + times; owner: local only, opt-in).</summary>
public sealed class MusicTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sahar-music-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 11, 10, 8, 0, 0, TimeSpan.FromHours(3));
    private LearnConsent _consent = new(Music: false);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private MindStore Store() => new(new SecretBox(_dir, new TestKeyWrap()), () => _now, () => _consent);

    [Fact]
    public void Music_OffNeverStored()
    {
        var store = Store();
        var music = new MusicLearner(store, () => _consent, () => _now);
        Assert.False(music.Heard("Shams", "Fairuz"));
        Assert.Empty(store.Listens());
        Assert.False(store.AddListen("Shams", _now));   // the store refuses too
        Assert.False(Directory.Exists(_dir));           // nothing at all was written
        Assert.Empty(store.Profile());
    }

    [Fact]
    public void Music_OnKeepsTitleAndTime_OnceAWhile_ThenProposes()
    {
        _consent = new LearnConsent(Music: true);
        var store = Store();
        var music = new MusicLearner(store, () => _consent, () => _now);
        Assert.True(music.Heard("Shams", "Fairuz"));
        Assert.False(music.Heard("Shams", "Fairuz"));   // the same song a minute later is not new
        var l = Assert.Single(store.Listens());
        Assert.Equal("Shams — Fairuz", l.Title);
        Assert.Equal(_now, l.At);
        Assert.Empty(store.Profile());

        for (int day = 1; day <= 2; day++)
        {
            _now = _now.AddDays(1);
            music.Heard("Another " + day, "Fairuz");
        }
        var p = Assert.Single(store.Profile());
        Assert.Equal("بتحب تسمع Fairuz الصبح", p.Text);
        Assert.Equal(ItemStatus.Proposed, p.Status);   // a proposal only: he says «صح»
        Assert.Equal(LearnSource.Music, p.Sources[0].Kind);

        // Turned off: nothing more is kept.
        _consent = new LearnConsent(Music: false);
        _now = _now.AddHours(1);
        Assert.False(music.Heard("Late", "Someone"));
        Assert.Equal(3, store.Listens().Count);
    }
}
