using System.Runtime.InteropServices;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Mind;

/// <summary>
/// 0.15.0 T9: a process's dedicated video memory from Windows' performance counter "GPU Process Memory" (what Task
/// Manager shows), read with PDH: one query, one collect (the counter is a raw byte count), the formatted array of
/// every instance the path matches; <see cref="GpuMemory"/> keeps the exact pid's and sums them. The English path
/// (PdhAddEnglishCounter) works on an Arabic Windows too. Numbers only: nothing of a process but its id is read.
/// </summary>
internal static unsafe partial class GpuPdh
{
    private const uint PdhMoreData = 0x800007D2, FmtLarge = 0x00000400;

    [StructLayout(LayoutKind.Sequential)]
    private struct Item   // PDH_FMT_COUNTERVALUE_ITEM_W (x64: name 8, CStatus 4 + 4 padding, the LONGLONG 8)
    {
        public char* Name;
        public uint CStatus;
        public long Large;
    }

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhOpenQuery(string? source, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint PdhAddEnglishCounter(nint query, string path, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    private static partial uint PdhGetFormattedCounterArray(nint counter, uint format, uint* bufferSize, uint* count, void* buffer);

    [LibraryImport("pdh.dll")]
    private static partial uint PdhCloseQuery(nint query);

    /// <summary>The process's dedicated video memory in MB (0: none, or the counter is not there).</summary>
    public static int ProcessMB(int pid) => Read(pid).MB;

    /// <summary>The MB, how many instances the path gave, and PDH's status (0 = read), for the log and the CI check.</summary>
    public static (int MB, int Instances, uint Status) Read(int pid)
    {
        if (pid <= 0) return (0, 0, 0);
        var (status, items) = Instances(GpuMemory.CounterPath(pid));
        if (status != 0) (status, items) = Instances(GpuMemory.AllPath);   // a partial wildcard refused: every process, filtered
        return (GpuMemory.SumMB(pid, items), items.Count(i => GpuMemory.Pid(i.Instance) == pid), status);
    }

    private static (uint Status, List<(string Instance, long Bytes)> Items) Instances(string path)
    {
        var list = new List<(string, long)>();
        try
        {
            uint st = PdhOpenQuery(null, 0, out var query);
            if (st != 0) return (st, list);
            try
            {
                if ((st = PdhAddEnglishCounter(query, path, 0, out var counter)) != 0) return (st, list);
                if ((st = PdhCollectQueryData(query)) != 0) return (st, list);
                uint size = 0, count = 0;
                st = PdhGetFormattedCounterArray(counter, FmtLarge, &size, &count, null);
                if (st == 0) return (0, list);   // no instance at all
                if (st != PdhMoreData || size == 0) return (st, list);
                void* buffer = NativeMemory.Alloc(size);
                try
                {
                    if ((st = PdhGetFormattedCounterArray(counter, FmtLarge, &size, &count, buffer)) != 0) return (st, list);
                    var items = (Item*)buffer;
                    for (uint i = 0; i < count; i++)
                        if (items[i].CStatus is 0 or 1 /* PDH_CSTATUS_VALID_DATA / NEW_DATA */ && items[i].Name is not null)
                            list.Add((new string(items[i].Name), items[i].Large));
                    return (0, list);
                }
                finally { NativeMemory.Free(buffer); }
            }
            finally { PdhCloseQuery(query); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return (uint.MaxValue, list);
        }
    }
}
