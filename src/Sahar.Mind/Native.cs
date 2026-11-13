using System.Runtime.InteropServices;

namespace Sahar.Mind;

/// <summary>The few Win32 calls the Mind needs: a kill-on-close Job Object, memory status, DXGI adapters.</summary>
internal static unsafe partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public ulong R1, R2, R3, W1, W2, W3;   // IO_COUNTERS
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
    private static partial nint CreateJobObject(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(MemoryStatusEx* status);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemPowerStatus(byte* status);

    /// <summary>ACLineStatus 0 = on battery.</summary>
    public static bool OnBattery()
    {
        byte* s = stackalloc byte[12];
        return GetSystemPowerStatus(s) && s[0] == 0;
    }

    [LibraryImport("dxgi.dll")]
    private static partial int CreateDXGIFactory1(Guid* riid, void** factory);

    private static nint _job;

    /// <summary>Every child goes into one job that dies with the Mind (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE).</summary>
    public static void KillWithMe(nint process)
    {
        if (_job == 0)
        {
            var job = CreateJobObject(0, 0);
            if (job == 0) return;
            var info = new ExtendedLimit { Basic = { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, &info, (uint)sizeof(ExtendedLimit))) return;
            _job = job;
        }
        AssignProcessToJobObject(_job, process);
    }

    public static long TotalRamMB()
    {
        var s = new MemoryStatusEx { Length = (uint)sizeof(MemoryStatusEx) };
        return GlobalMemoryStatusEx(&s) ? (long)(s.TotalPhys / (1024 * 1024)) : 0;
    }

    /// <summary>DXGI adapters: vendor, dedicated video memory (MB), software flag. Empty when DXGI is unavailable.</summary>
    public static List<(int Vendor, long DedicatedMB, bool Software, string Name)> Adapters()
    {
        var list = new List<(int, long, bool, string)>();
        var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
        void* factory;
        try { if (CreateDXGIFactory1(&iid, &factory) < 0) return list; }
        catch (DllNotFoundException) { return list; }
        var fvt = *(void***)factory;
        var enumAdapters1 = (delegate* unmanaged[Stdcall]<void*, uint, void**, int>)fvt[12];
        byte* desc = stackalloc byte[320];
        for (uint i = 0; ; i++)
        {
            void* adapter;
            if (enumAdapters1(factory, i, &adapter) < 0) break;   // DXGI_ERROR_NOT_FOUND ends the list
            var avt = *(void***)adapter;
            var getDesc1 = (delegate* unmanaged[Stdcall]<void*, byte*, int>)avt[10];
            if (getDesc1(adapter, desc) >= 0)
            {
                var name = new string((char*)desc).TrimEnd('\0');
                int vendor = *(int*)(desc + 256);
                long dedicated = (long)*(nuint*)(desc + 272);
                uint flags = *(uint*)(desc + 304);
                list.Add((vendor, dedicated / (1024 * 1024), (flags & 2) != 0, name));
            }
            ((delegate* unmanaged[Stdcall]<void*, uint>)avt[2])(adapter);
        }
        ((delegate* unmanaged[Stdcall]<void*, uint>)fvt[2])(factory);
        return list;
    }
}
