using Sahar.Core.Chat;

namespace Sahar.Core.Mind.Initiative;

/// <summary>
/// 0.15.0 (owner: «often» spoke 7 times in 7 minutes; his log asked her brain every minute): when she may even ASK her
/// brain for a line, on top of the gate's moment rules. A check that brings nothing backs off (Often 10 → 20 → 40 →
/// 60 min at most; Normal 20, Rare 45 to start); after a spoken line the next check waits the line gap (Often 20 min,
/// Normal 45, Rare 90); at most <see cref="HourCap"/> lines in any hour; a line on a topic she already raised today is a
/// repeat (dropped). Pure; the lines' words are kept in memory only, never logged.
/// </summary>
public sealed class InitiativeSchedule(DateTimeOffset started)
{
    public static readonly TimeSpan MaxCheckGap = TimeSpan.FromMinutes(60);
    private const int Remembered = 8;
    private static readonly HashSet<string> Fillers =
        ["رايك", "هلق", "كتير", "بدك", "يعني", "هيك", "انت", "انتي", "لسا", "كمان", "شوي", "يلا", "شلون", "كيفك", "اليوم", "شي"];

    /// <summary>Review (b): after a start the first check waits the shortest check gap (not "due now" on every restart).</summary>
    private DateTimeOffset _nextCheck = started + CheckGap(InitiativeLevel.Often);
    /// <summary>Review (c): a topic counts as "said lately" for half a day.</summary>
    public static readonly TimeSpan TopicMemory = TimeSpan.FromHours(12);
    private int _nothingInARow;
    private readonly List<DateTimeOffset> _lines = [];
    private readonly List<(DateTimeOffset At, HashSet<string> Words)> _topics = [];

    public DateTimeOffset NextCheck => _nextCheck;

    public static int HourCap(InitiativeLevel level) => level switch { InitiativeLevel.Often => 2, InitiativeLevel.Normal or InitiativeLevel.Rare => 1, _ => 0 };

    public static TimeSpan LineGap(InitiativeLevel level) => TimeSpan.FromMinutes(level switch
    {
        InitiativeLevel.Often => 20, InitiativeLevel.Normal => 45, _ => 90,
    });

    public static TimeSpan CheckGap(InitiativeLevel level) => TimeSpan.FromMinutes(level switch
    {
        InitiativeLevel.Often => 10, InitiativeLevel.Normal => 20, _ => 45,
    });

    public int LinesInLastHour(DateTimeOffset now) => _lines.Count(l => now - l < TimeSpan.FromHours(1) && l <= now);

    /// <summary>May she ask her brain now? (Not before the next check, never past the hour's cap, never when Off.)</summary>
    public bool MayCheck(DateTimeOffset now, InitiativeLevel level) =>
        level != InitiativeLevel.Off && now >= _nextCheck && LinesInLastHour(now) < HourCap(level);

    /// <summary>She asked: until a line comes back, it counts as "nothing to say" (an error too), and the next check backs off.</summary>
    public void Asked(DateTimeOffset now, InitiativeLevel level)
    {
        var gap = CheckGap(level) * Math.Pow(2, Math.Min(_nothingInARow, 3));
        _nothingInARow++;
        _nextCheck = now + (gap < MaxCheckGap ? gap : MaxCheckGap);
    }

    /// <summary>She said a line: the level's line gap starts, and its topic is remembered.</summary>
    public void Spoke(DateTimeOffset now, string line, InitiativeLevel level)
    {
        _nothingInARow = 0;
        _lines.Add(now);
        _lines.RemoveAll(l => now - l > TimeSpan.FromHours(24));
        _topics.Add((now, Topic(line)));
        if (_topics.Count > Remembered) _topics.RemoveAt(0);
        _nextCheck = now + LineGap(level);
    }

    /// <summary>The same topic as a line she said lately (most of its words again, in any order, emoji aside).</summary>
    public bool Repeats(string line, DateTimeOffset now)
    {
        var t = Topic(line);
        foreach (var (at, old) in _topics)
        {
            if (now - at > TopicMemory) continue;
            if (t.Count == 0 || old.Count == 0) { if (t.SetEquals(old)) return true; continue; }
            int common = t.Count(old.Contains);
            if (common >= 0.6 * Math.Min(t.Count, old.Count)) return true;
        }
        return false;
    }

    private static HashSet<string> Topic(string line) =>
        ChatCommands.Normalize(line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Bare).Where(w => w.Length >= 3 && !Fillers.Contains(w)).ToHashSet(StringComparer.Ordinal);

    private static string Bare(string w)
    {
        int a = 0, b = w.Length;
        while (a < b && !char.IsLetterOrDigit(w[a])) a++;
        while (b > a && !char.IsLetterOrDigit(w[b - 1])) b--;
        return w[a..b];
    }
}
