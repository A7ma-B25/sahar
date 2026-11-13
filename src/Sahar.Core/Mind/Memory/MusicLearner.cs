namespace Sahar.Core.Mind.Memory;

/// <summary>
/// What he listens to (owner answer: titles + times, local only; opt-in: «شو عم تسمع»). Off, nothing is kept — not
/// even in memory. On, each new title is kept with its time (encrypted), and an artist heard on three different days in
/// the same part of the day becomes a profile proposal («بتحب تسمع فيروز الصبح») that he confirms or not.
/// </summary>
public sealed class MusicLearner(MindStore store, Func<LearnConsent> consent, Func<DateTimeOffset> now)
{
    private static readonly TimeSpan Same = TimeSpan.FromMinutes(10);
    private const int Days = 3;
    private static readonly TimeSpan Window = TimeSpan.FromDays(14);
    private (string Title, DateTimeOffset At)? _last;

    /// <summary>A title is playing; true when it was kept.</summary>
    public bool Heard(string title, string? artist)
    {
        if (!consent().Music) { _last = null; return false; }
        title = MindStore.Clean(title, 100);
        artist = string.IsNullOrWhiteSpace(artist) ? null : MindStore.Clean(artist, 60);
        if (title.Length == 0) return false;
        var full = artist is null ? title : $"{title} — {artist}";
        var at = now();
        if (_last is { } l && l.Title == full && at - l.At < Same) return false;
        _last = (full, at);
        if (!store.AddListen(full, at)) return false;
        if (artist is not null) Notice(artist, at);
        return true;
    }

    private void Notice(string artist, DateTimeOffset at)
    {
        var band = Band(at);
        int days = store.Listens()
            .Where(x => at - x.At <= Window && x.Title.EndsWith(" — " + artist, StringComparison.Ordinal) && Band(x.At) == band)
            .Select(x => DateOnly.FromDateTime(x.At.Date)).Distinct().Count();
        if (days >= Days)
            store.Propose($"بتحب تسمع {artist} {band}", ProfileCategory.Likes, new ItemSource(LearnSource.Music, at), Sensitivity.None);
    }

    /// <summary>Her words for the part of the day.</summary>
    public static string Band(DateTimeOffset at) => at.Hour switch
    {
        >= 5 and < 12 => "الصبح",
        >= 12 and < 17 => "الضهر",
        >= 17 and < 21 => "المسا",
        _ => "بالليل",
    };
}
