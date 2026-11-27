using System.Globalization;
using System.Text;

namespace Sahar.Core.Diagnostics;

/// <summary>
/// 0.17.2 perf.txt for «ابعت سجلّي»: numbers only — how long her UI thread was late (max, p95), and per named source
/// (a sensor's poll, a hook's events) its calls, rate per minute, total and longest time. Never a title, an app or a
/// path. Thread-safe.
/// </summary>
public sealed class PerfCounters(DateTimeOffset start)
{
    public const string File = "perf.txt";
    private const int MaxStalls = 20_000;
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Calls, double Total, double Max)> _src = new(StringComparer.Ordinal);
    private readonly Queue<double> _stalls = new();
    private double _stallMax;

    /// <summary>One call of <paramref name="name"/> that took <paramref name="ms"/>.</summary>
    public void Time(string name, double ms)
    {
        lock (_gate)
        {
            var (c, t, m) = _src.GetValueOrDefault(name);
            _src[name] = (c + 1, t + ms, Math.Max(m, ms));
        }
    }

    /// <summary><paramref name="n"/> events of <paramref name="name"/> (a hook's callbacks), no time.</summary>
    public void Count(string name, int n = 1)
    {
        lock (_gate)
        {
            var (c, t, m) = _src.GetValueOrDefault(name);
            _src[name] = (c + n, t, m);
        }
    }

    /// <summary>How late her UI thread ran a ping (the watchdog's sample).</summary>
    public void Stall(double ms)
    {
        lock (_gate)
        {
            _stalls.Enqueue(ms);
            if (_stalls.Count > MaxStalls) _stalls.Dequeue();
            _stallMax = Math.Max(_stallMax, ms);
        }
    }

    public string Text(DateTimeOffset now, IEnumerable<KeyValuePair<string, string>> extra)
    {
        var inv = CultureInfo.InvariantCulture;
        double minutes = Math.Max(1.0 / 60, (now - start).TotalMinutes);
        var b = new StringBuilder();
        b.AppendLine("Sahar perf (numbers only)");
        b.AppendLine(string.Create(inv, $"uptime: {minutes:0.0} min"));
        lock (_gate)
        {
            if (_stalls.Count > 0)
            {
                var sorted = _stalls.OrderBy(x => x).ToArray();
                double p95 = sorted[Math.Clamp((int)Math.Ceiling(0.95 * sorted.Length) - 1, 0, sorted.Length - 1)];
                b.AppendLine(string.Create(inv, $"ui stalls: samples={sorted.Length} p95={p95:0}ms max={_stallMax:0}ms over50ms={sorted.Count(x => x > 50)} over250ms={sorted.Count(x => x > 250)}"));
            }
            else b.AppendLine("ui stalls: samples=0");
            foreach (var (name, (c, t, m)) in _src.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                b.AppendLine(t > 0 || m > 0
                    ? string.Create(inv, $"{name} calls={c} perMin={c / minutes:0.0} totalMs={t:0.0} maxMs={m:0.0}")
                    : string.Create(inv, $"{name} calls={c} perMin={c / minutes:0.0}"));
        }
        foreach (var (k, v) in extra) b.AppendLine($"{k}: {v}");
        return b.ToString();
    }
}
