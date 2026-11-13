namespace Sahar.Core.Activities;

/// <summary>What the tag shows: the front one, up to three beads, and a knot when there are more.</summary>
public sealed record TagView(Activity Front, IReadOnlyList<Activity> Beads, bool Knot);

/// <summary>
/// Every live activity and moment, and which one her tag shows (spec 0.13 §1.3): one at a time, rank first, equal rank
/// newest first. A moment (or Claude asking) that arrives after your pick takes the front, and when it ends your pick is
/// back. The weather of the day shows only when nothing else is live; screen time never hangs by itself but is in the
/// wheel's cycle. The rest wait as beads: at most three, then a knot.
/// </summary>
public sealed class ActivityStack
{
    public const int MaxBeads = 3;

    private sealed record Entry(Activity Activity, DateTimeOffset Since, DateTimeOffset? Until);

    private readonly Dictionary<string, Entry> _items = [];
    private string? _pick;
    private DateTimeOffset _pickedAt;

    /// <summary>Adds or updates a live activity; an update keeps its place in time (it is not news).</summary>
    public void Set(Activity a, DateTimeOffset now) =>
        _items[a.Id] = _items.TryGetValue(a.Id, out var e) && e.Until is null ? e with { Activity = a } : new Entry(a, now, null);

    /// <summary>A moment for its length (6 s, the volume 1.5 s); the same id again extends it.</summary>
    public void Moment(Activity a, DateTimeOffset now) => _items[a.Id] = new Entry(a, now, now + ActivityRank.MomentLength(a.Kind));

    public void Remove(string id) => _items.Remove(id);

    public bool Has(string id) => _items.ContainsKey(id);

    /// <summary>Nothing at all (no allocation: the engine's tick checks it every second).</summary>
    public bool Empty => _items.Count == 0;

    /// <summary>The user brought this one forward (a bead click, the wheel).</summary>
    public void Pick(string id, DateTimeOffset now)
    {
        _pick = id;
        _pickedAt = now;
    }

    /// <summary>The mouse wheel over the tag: the next (or previous) one in the cycle comes forward.</summary>
    public void Wheel(int steps, DateTimeOffset now)
    {
        var cycle = Cycle(now);
        if (cycle.Count < 2 || View(now) is not { } v) return;
        int i = cycle.FindIndex(e => e.Activity.Id == v.Front.Id);
        int next = ((i + steps) % cycle.Count + cycle.Count) % cycle.Count;
        Pick(cycle[next].Activity.Id, now);
    }

    /// <summary>Every live activity in the order they would show (moments that ended are gone).</summary>
    public IReadOnlyList<Activity> All(DateTimeOffset now) => Cycle(now).Select(e => e.Activity).ToList();

    public TagView? View(DateTimeOffset now)
    {
        var cycle = Cycle(now);
        if (cycle.Count == 0 || cycle.All(e => e.Activity.Kind == ActivityKind.ScreenTime)) return null;
        var front = cycle[0];
        if (_pick is { } id && cycle.FirstOrDefault(e => e.Activity.Id == id) is { } picked
            && !cycle.Any(e => ActivityRank.Of(e.Activity.Kind) <= 2 && e.Since > _pickedAt && e != picked))
            front = picked;
        var rest = cycle.Where(e => e != front).Select(e => e.Activity).ToList();
        return new TagView(front.Activity, rest.Take(MaxBeads).ToList(), rest.Count > MaxBeads);
    }

    /// <summary>Hidden while the menu or an activity card is open, while she hides, and while she is dragged.</summary>
    public static bool TagShown(bool cardOpen, bool menuOpen, bool hidden, bool dragged) => !(cardOpen || menuOpen || hidden || dragged);

    private List<Entry> Cycle(DateTimeOffset now)
    {
        foreach (var gone in _items.Where(p => p.Value.Until is { } u && u <= now).Select(p => p.Key).ToList()) _items.Remove(gone);
        var list = _items.Values.OrderBy(e => ActivityRank.Of(e.Activity.Kind)).ThenByDescending(e => e.Since).ToList();
        if (list.Any(e => e.Activity.Kind is not (ActivityKind.WeatherToday or ActivityKind.ScreenTime)))
            list.RemoveAll(e => e.Activity.Kind == ActivityKind.WeatherToday);
        return list;
    }
}
