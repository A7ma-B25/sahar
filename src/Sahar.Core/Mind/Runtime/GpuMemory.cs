using System.Globalization;
using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Runtime;

/// <summary>
/// 0.15.0 T9: what llama-server holds in video memory, from Windows' own performance counter (the one Task Manager's
/// "Dedicated GPU memory" column reads): <c>\GPU Process Memory(pid_N*)\Dedicated Usage</c>, in bytes, one instance
/// per adapter ("pid_4321_luid_0x…_0x…_phys_0"). 0.14.x read llama.cpp's "buffer size" lines (hidden at b12207's
/// verbosity) and the device's free memory before and after (its driver often reports the same number twice), and
/// logged vram=0MB on his RTX 4050. The wildcard "pid_12*" also matches pid 123: only the exact pid is summed.
/// Pure and table-tested; the Mind does the PDH calls.
/// </summary>
public static partial class GpuMemory
{
    /// <summary>The English counter path for one process (PdhAddEnglishCounter: the same on an Arabic Windows).</summary>
    public static string CounterPath(int pid) => $@"\GPU Process Memory(pid_{pid.ToString(CultureInfo.InvariantCulture)}*)\Dedicated Usage";

    /// <summary>Every process's instances: the fallback when a partial wildcard is refused.</summary>
    public const string AllPath = @"\GPU Process Memory(*)\Dedicated Usage";

    [GeneratedRegex(@"^pid_(\d{1,10})(?:_|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PidPrefix();

    /// <summary>The process id an instance name belongs to, or null for anything else ("_Total", no pid).</summary>
    public static int? Pid(string instance) =>
        PidPrefix().Match(instance ?? "") is { Success: true } m && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ? pid : null;

    /// <summary>The process's dedicated video memory over all its adapters, in MB (rounded); 0 when it has none.</summary>
    public static int SumMB(int pid, IEnumerable<(string Instance, long Bytes)> items)
    {
        if (pid <= 0) return 0;
        long bytes = 0;
        foreach (var (instance, b) in items)
            if (b > 0 && Pid(instance) == pid) bytes += b;
        return (int)Math.Round(bytes / (1024.0 * 1024.0), MidpointRounding.AwayFromZero);
    }

    /// <summary>The number she logs and reports: Windows' counter, else llama.cpp's buffer lines, else the free-memory
    /// difference; with where it came from (for the log).</summary>
    public static (int MB, string Source) Best(int pdhMB, int buffersMB, int freeDeltaMB) =>
        pdhMB > 0 ? (pdhMB, "pdh") : buffersMB > 0 ? (buffersMB, "buffers") : freeDeltaMB > 0 ? (freeDeltaMB, "free-delta") : (0, "none");
}
