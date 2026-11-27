using Sahar.Core.Activities;
using Sahar.Core.Chat;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Engine.Tools;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// «الدرج» (owner ruling 12): everything at once — what is live now (every activity, not only the tag's), what is on
/// her shelf, and what happened (activities and moments as they came, newest first). The history is the last 24 h,
/// kept across restarts sealed in her personal box (<see cref="DrawerHistory"/>, owner 0.16 review) and wiped by
/// «انسي كل شي»; nothing of it is logged.
/// </summary>
internal sealed class Drawer
{
    public const int MaxHistory = DrawerHistory.Max;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ISecretBox> _box;
    private DrawerHistory _history;

    public Drawer(Func<ISecretBox>? box = null, Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _box = box ?? (() => MindParts.SecretBox(MindPaths.Personal));
        _history = new DrawerHistory(_box(), _now);
    }

    public int Count => _history.Items.Count;

    public void Happened(Activity a) => Happened(a.Meta is { Length: > 0 } m ? a.Text + " · " + m : a.Text, Icon(a.Icon));

    public void Happened(string text, string icon) => _history.Add(text, icon);

    /// <summary>«انسي كل شي», first half: nothing is written while her folder is shredded.</summary>
    public void Pause() => _history = new DrawerHistory(new NoBox(), _now);

    /// <summary>«انسي كل شي» done: empty, in a fresh box.</summary>
    public void Forget() => _history = new DrawerHistory(_box(), _now);

    private sealed class NoBox : ISecretBox
    {
        public byte[]? Read(string name) => null;
        public void Write(string name, ReadOnlySpan<byte> data) => throw new MindStoreException("forgotten");
        public void Delete(string name) { }
    }

    public static string Icon(string activityIcon) => activityIcon switch
    {
        "notes" => "music", "stack" => "drawer", "lock" => "lock", "copy" => "copy", "check" => "check", "cloud" or "rain" or "storm" => "rain",
        "sun" => "sparkle", "timer" or "clock" => "clock", "terminal" or "claude" => "terminal", "bell" => "bell", "book" => "book", _ => "bell",
    };

    private string Clock(DateTimeOffset at)
    {
        var l = at.ToLocalTime();
        int h = l.Hour % 12 == 0 ? 12 : l.Hour % 12;
        return Sahar.Core.Text.Digits.ToArabicIndic($"{h}:{l.Minute:00}");
    }

    /// <param name="live">Every activity now (ActivityStack.All), plus her own running things (timer, quiet, Quran).</param>
    public PanelSpec Spec(IReadOnlyList<Activity> live, IReadOnlyList<(string Text, string Icon, string? Meta)> own, IReadOnlyList<ShelfItem> shelf)
    {
        var liveRows = own.Select((o, i) => new PickRow("own:" + i, o.Text) { Icon = o.Icon, Meta = o.Meta, Live = true })
            .Concat(live.Where(a => a.Kind != ActivityKind.Shelf).Select(a => new PickRow("act:" + a.Id, a.Text)
            { Icon = Icon(a.Icon), Meta = a.Meta, Live = ActivityRank.Hangs(a.Kind), Soft = a.Asking }))
            .ToList();
        var shelfRows = shelf.Take(12).Select((s, i) => new PickRow("shelf:" + i, Path.GetFileName(s.Path)) { Icon = "drawer", Meta = ClipboardTool.Age(_now() - s.Added) }).ToList();
        var history = _history.Items.Select(h => new PickRow("", h.Text) { Icon = h.Icon, Meta = Clock(h.At) }).ToList();
        return new PanelSpec
        {
            Kind = "drawer", Name = "drawer", Title = "الدرج", Sub = Sahar.Core.Settings.Address.ToYou("كل شي شغّال، وكل شي صار اليوم"),
            Sections = [("شغّال هلق", liveRows), ("عالرف", shelfRows), ("شو صار اليوم", history)],
            Rows = [.. liveRows, .. shelfRows, .. history],
            Empty = new PickBox(Sahar.Core.Settings.Address.ToYou("ما في شي هلق"), Sahar.Core.Settings.Address.ToYou("أي شي بيصير معي بيطلع هون."), "drawer"),
        };
    }
}
