namespace Sahar.Core.Diagnostics;

/// <summary>
/// 0.14.1 fix 3: a timer that throws every tick must not fill engine.log (his had ~1 273 identical traces an hour).
/// The first of each distinct error (where + kind + throwing frame) is logged with its trace; repeats are only counted,
/// and the count is logged at most every <see cref="Every"/> when the error comes again. Kinds and frames only.
/// </summary>
public sealed class ErrorTally(Func<DateTimeOffset> now)
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (int Count, DateTimeOffset Logged, bool Tallied)> _seen = [];
    private readonly object _gate = new();

    /// <summary>The line to log for this error, or null (a counted repeat).</summary>
    public string? Record(string where, Exception e)
    {
        var st = e.StackTrace ?? "";
        int nl = st.IndexOf('\n');
        var key = where + "|" + LogRules.Kind(e) + "|" + (nl < 0 ? st : st[..nl]).Trim();
        lock (_gate)
        {
            var t = now();
            if (!_seen.TryGetValue(key, out var s))
            {
                _seen[key] = (0, t, false);
                return $"{where} error: {LogRules.Describe(e)}";
            }
            s.Count++;
            if (t - s.Logged < Every) { _seen[key] = s; return null; }
            var line = $"{where} error: {LogRules.Kind(e)} again ×{s.Count} since the {(s.Tallied ? "last count" : "first")}";
            _seen[key] = (0, t, true);
            return line;
        }
    }
}
