using System.Globalization;
using System.Text.Json;

namespace Sahar.Core.Activities;

/// <summary>Explorer's copy (spec 0.13 §2 File copy): our own smoothed pace from the dialog's percentage, so the time
/// left is steady; a stall (no progress for 5 s) has no time left; 100 %, or the dialog gone after progress, is done.</summary>
public sealed class CopyPace
{
    public static readonly TimeSpan Stall = TimeSpan.FromSeconds(5);
    private double? _rate, _last;
    private DateTimeOffset _lastAt, _movedAt;

    public double Percent => _last ?? 0;
    public bool Done { get; private set; }
    public TimeSpan? Left { get; private set; }

    public void Sample(double percent, DateTimeOffset t)
    {
        if (_last is { } last && t > _lastAt)
        {
            double dt = (t - _lastAt).TotalSeconds;
            if (percent > last)
            {
                double r = (percent - last) / dt;
                _rate = _rate is { } old ? 0.3 * r + 0.7 * old : r;
                _movedAt = t;
            }
        }
        else _movedAt = t;
        _last = percent;
        _lastAt = t;
        if (percent >= 100) { Done = true; Left = TimeSpan.Zero; return; }
        Left = _rate is { } rate && rate > 0 && t - _movedAt < Stall ? TimeSpan.FromSeconds((100 - percent) / rate) : null;
    }

    /// <summary>The dialog closed.</summary>
    public void Gone(DateTimeOffset t)
    {
        if (_last is not null) Done = true;
    }
}

/// <summary>«وإنت غايب» (spec 0.13 §2): kinds, counts and times only.</summary>
public sealed record AwaySummary(TimeSpan Away, IReadOnlyList<(ActivityKind Kind, int Count, DateTimeOffset Last)> Rows, int More, int Total);

public sealed class AwayLog
{
    public static readonly TimeSpan MinAway = TimeSpan.FromMinutes(2);
    public const int MaxRows = 5;
    private DateTimeOffset? _since;
    private readonly List<(ActivityKind Kind, DateTimeOffset At)> _notes = [];

    public bool Locked => _since is not null;

    public void Lock(DateTimeOffset t)
    {
        _since ??= t;
        _notes.Clear();
    }

    public void Note(ActivityKind kind, DateTimeOffset t)
    {
        if (_since is not null) _notes.Add((kind, t));
    }

    /// <summary>The summary when the lock lasted ≥ 2 min and something happened; null otherwise. Once.</summary>
    public AwaySummary? Unlock(DateTimeOffset t)
    {
        if (_since is not { } since) return null;
        _since = null;
        var notes = _notes.ToList();
        _notes.Clear();
        if (t - since < MinAway || notes.Count == 0) return null;
        var rows = notes.GroupBy(n => n.Kind).Select(g => (g.Key, g.Count(), g.Max(n => n.At))).OrderByDescending(r => r.Item3).ToList();
        return new AwaySummary(t - since, rows.Take(MaxRows).ToList(), Math.Max(0, rows.Count - MaxRows), notes.Count);
    }
}

/// <summary>
/// Screen time (spec 0.13 §2): seconds per app per day while you are there (not idle, not locked; a playing video
/// counts), in <c>usage\YYYY-MM-DD.json</c>, kept 7 days. App names only (no paths, no titles).
/// </summary>
public sealed class ScreenTime(string folder)
{
    public const int KeepDays = 7;
    private DateOnly? _day;
    private Dictionary<string, double> _apps = [];

    public static bool Counts(bool idle, bool locked, bool videoPlaying) => !locked && (!idle || videoPlaying);

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["msedge"] = "Edge", ["ms-teams"] = "Teams", ["explorer"] = "الملفات", ["winword"] = "Word", ["excel"] = "Excel",
        ["powerpnt"] = "PowerPoint", ["windowsterminal"] = "Terminal", ["wt"] = "Terminal",
    };

    /// <summary>The name shown for a program (its file name, no path).</summary>
    public static string AppName(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath.Replace('\\', '/').Split('/')[^1]);
        if (Names.TryGetValue(name, out var known)) return known;
        return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public void Add(string app, TimeSpan span, DateTimeOffset now)
    {
        Day(now);
        _apps[app] = _apps.GetValueOrDefault(app) + span.TotalSeconds;
    }

    public IReadOnlyList<(string App, TimeSpan Time)> Today(DateTimeOffset now)
    {
        Day(now);
        return _apps.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (p.Key, TimeSpan.FromSeconds(Math.Round(p.Value)))).ToList();
    }

    public TimeSpan Total(DateTimeOffset now) => TimeSpan.FromSeconds(Math.Round(Today(now).Sum(a => a.Time.TotalSeconds)));

    public void Save()
    {
        if (_day is not { } day) return;
        try
        {
            Directory.CreateDirectory(folder);
            using var s = new MemoryStream();
            using (var w = new Utf8JsonWriter(s))
            {
                w.WriteStartObject();
                w.WriteStartObject("apps");
                foreach (var (k, v) in _apps) w.WriteNumber(k, Math.Round(v));
                w.WriteEndObject();
                w.WriteEndObject();
            }
            File.WriteAllBytes(FileFor(day), s.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Days older than the last 7 go (only our dated files).</summary>
    public void Prune(DateTimeOffset now)
    {
        var oldest = DateOnly.FromDateTime(now.Date).AddDays(-(KeepDays - 1));
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*.json"))
                if (DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < oldest)
                    File.Delete(f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string FileFor(DateOnly d) => Path.Combine(folder, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");

    private void Day(DateTimeOffset now)
    {
        var day = DateOnly.FromDateTime(now.Date);
        if (_day == day) return;
        if (_day is not null) Save();
        _day = day;
        _apps = [];
        try
        {
            if (!File.Exists(FileFor(day))) return;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(FileFor(day)));
            if (doc.RootElement.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Object)
                foreach (var p in apps.EnumerateObject()) if (p.Value.TryGetDouble(out var v)) _apps[p.Name] = v;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { _apps = []; }
    }
}

/// <summary>Small system facts (spec 0.13 §2 System).</summary>
public static class SystemNow
{
    /// <summary>Time to a full battery from Windows' battery report (mWh, mW); null when not charging or full.</summary>
    public static TimeSpan? ToFull(int remainingMWh, int fullMWh, int rateMW) =>
        rateMW > 0 && fullMWh > remainingMWh ? TimeSpan.FromHours((fullMWh - remainingMWh) / (double)rateMW) : null;

    /// <summary>The app's name from its camera/mic consent key: a package family ("Microsoft.WindowsCamera_8wekyb3d8bbwe")
    /// or a non-packaged path with '#' for '\'. Shown only, never logged.</summary>
    public static string AppName(string consentKey)
    {
        if (consentKey.Contains('#')) return ScreenTime.AppName(consentKey.Replace('#', '\\'));
        var family = consentKey.Split('_')[0];
        return family.Split('.')[^1];
    }
}

/// <summary>A file on her shelf: its path (no copy) and when it came.</summary>
public sealed record ShelfItem(string Path, DateTimeOffset Added);

/// <summary>Her shelf (spec 0.13 §2 Shelf): paths in shelf.json; missing files drop out by themselves.</summary>
public sealed class Shelf
{
    public const int Max = 40;
    private readonly string _path;
    private readonly List<ShelfItem> _items = [];

    public Shelf(string jsonPath)
    {
        _path = jsonPath;
        try
        {
            if (!File.Exists(jsonPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(jsonPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var e in doc.RootElement.EnumerateArray())
                if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } path)
                    _items.Add(new ShelfItem(path, e.TryGetProperty("added", out var a) && a.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.MinValue));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { _items.Clear(); }
    }

    public IReadOnlyList<ShelfItem> Items => _items;

    public void Add(IEnumerable<string> paths, DateTimeOffset now)
    {
        foreach (var p in paths)
            if (!_items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) _items.Add(new ShelfItem(p, now));
        if (_items.Count > Max) _items.RemoveRange(0, _items.Count - Max);
        Save();
    }

    public void Remove(string path)
    {
        if (_items.RemoveAll(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    /// <summary>Drops the items whose files are gone; returns how many stay.</summary>
    public int Prune(Func<string, bool> exists)
    {
        if (_items.RemoveAll(i => !exists(i.Path)) > 0) Save();
        return _items.Count;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using var s = new MemoryStream();
            using (var w = new Utf8JsonWriter(s))
            {
                w.WriteStartArray();
                foreach (var i in _items)
                {
                    w.WriteStartObject();
                    w.WriteString("path", i.Path);
                    w.WriteString("added", i.Added);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }
            File.WriteAllBytes(_path, s.ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>A new screenshot is taken only once its size stops changing.</summary>
public static class Settle
{
    public static bool Stable(long sizeBefore, long sizeNow) => sizeNow > 0 && sizeNow == sizeBefore;
}
