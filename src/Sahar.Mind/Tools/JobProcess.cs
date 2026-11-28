using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Sahar.Core.Mind.Tools;

namespace Sahar.Mind.Tools;

/// <summary>
/// 0.19 spec §4: one stdio MCP server — his local .exe, started by CreateProcessW directly (never cmd.exe, so nothing in
/// his arguments is a shell command), suspended until it is inside its own Job Object: kill-on-close (it dies with the
/// Mind or when the link is dropped), at most <see cref="MemoryCap"/> of committed memory, no error dialogs. Its folder is
/// its working folder; it gets a small environment plus only its own secret. stdin/stdout are the MCP streams; stderr
/// goes to NUL (never read, never logged).
/// </summary>
internal sealed unsafe partial class JobProcess : IDisposable
{
    public const long MemoryCap = 512L * 1024 * 1024;

    private nint _job, _process;
    public Stream Input { get; }
    public Stream Output { get; }
    public int Id { get; }

    private JobProcess(nint job, nint process, int id, Stream input, Stream output)
    {
        _job = job;
        _process = process;
        Id = id;
        Input = input;
        Output = output;
    }

    /// <summary>The environment names a plain Windows program needs; nothing else of hers reaches it.</summary>
    private static readonly string[] Passed =
    [
        "SystemRoot", "SystemDrive", "windir", "TEMP", "TMP", "PATH", "PATHEXT", "ProgramFiles", "ProgramFiles(x86)", "ProgramData",
        "CommonProgramFiles", "LOCALAPPDATA", "APPDATA", "USERPROFILE", "HOMEDRIVE", "HOMEPATH", "NUMBER_OF_PROCESSORS",
        "PROCESSOR_ARCHITECTURE", "OS", "DOTNET_ROOT",
    ];

    public static JobProcess Start(string exe, IReadOnlyList<string> args, IReadOnlyDictionary<string, string> extraEnv)
    {
        var sa = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), InheritHandle = 1 };
        if (!CreatePipe(out var inRead, out var inWrite, &sa, 0)) throw Win("pipe");
        if (!CreatePipe(out var outRead, out var outWrite, &sa, 0)) { CloseHandle(inRead); CloseHandle(inWrite); throw Win("pipe"); }
        SetHandleInformation(inWrite, 1, 0);   // our ends are not inherited
        SetHandleInformation(outRead, 1, 0);
        nint nul = CreateFileW("NUL", 0x40000000, 3, &sa, 3, 0, 0);
        nint job = 0;
        try
        {
            job = CreateJobObjectW(0, 0);
            if (job == 0) throw Win("job");
            var limit = new ExtendedLimit
            {
                Basic = { LimitFlags = 0x2000 | 0x100 | 0x400 },   // KILL_ON_JOB_CLOSE | PROCESS_MEMORY | DIE_ON_UNHANDLED_EXCEPTION
                ProcessMemoryLimit = (nuint)MemoryCap,
            };
            if (!SetInformationJobObject(job, 9, &limit, (uint)sizeof(ExtendedLimit))) throw Win("job-limit");

            var env = new StringBuilder();
            var vars = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in Passed)
                if (Environment.GetEnvironmentVariable(n) is { Length: > 0 } v) vars[n] = v;
            foreach (var (k, v) in extraEnv) vars[k] = v;
            foreach (var (k, v) in vars) env.Append(k).Append('=').Append(v).Append('\0');
            env.Append('\0');

            var si = new StartupInfo { Cb = (uint)sizeof(StartupInfo), Flags = 0x100, StdInput = inRead, StdOutput = outWrite, StdError = nul };   // STARTF_USESTDHANDLES
            var cmd = McpCatalog.CommandLine(exe, args) + "\0";
            var cwd = Path.GetDirectoryName(exe)!;
            ProcessInformation pi;
            fixed (char* pExe = exe)
            fixed (char* pCmd = cmd)
            fixed (char* pEnv = env.ToString())
            fixed (char* pCwd = cwd)
            {
                // CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW
                if (!CreateProcessW(pExe, pCmd, 0, 0, 1, 0x4 | 0x400 | 0x08000000, pEnv, pCwd, &si, &pi)) throw Win("start");
            }
            if (!AssignProcessToJobObject(job, pi.Process))
            {
                TerminateProcess(pi.Process, 1);
                CloseHandle(pi.Thread);
                CloseHandle(pi.Process);
                throw Win("job-assign");
            }
            ResumeThread(pi.Thread);
            CloseHandle(pi.Thread);
            var input = new FileStream(new SafeFileHandle(inWrite, true), FileAccess.Write, 1, false);
            var output = new FileStream(new SafeFileHandle(outRead, true), FileAccess.Read, 4096, false);
            inWrite = 0;
            outRead = 0;
            var jp = new JobProcess(job, pi.Process, (int)pi.ProcessId, input, output);
            job = 0;
            return jp;
        }
        finally
        {
            CloseHandle(inRead);
            CloseHandle(outWrite);
            if (nul != -1 && nul != 0) CloseHandle(nul);
            if (inWrite != 0) CloseHandle(inWrite);
            if (outRead != 0) CloseHandle(outRead);
            if (job != 0) CloseHandle(job);
        }
    }

    public bool HasExited => _process == 0 || (GetExitCodeProcess(_process, out uint code) && code != 259);

    /// <summary>Closing the job kills the server and anything it started.</summary>
    public void Dispose()
    {
        try { Input.Dispose(); } catch (IOException) { }
        try { Output.Dispose(); } catch (IOException) { }
        if (_job != 0) { CloseHandle(_job); _job = 0; }
        if (_process != 0) { CloseHandle(_process); _process = 0; }
    }

    private static IOException Win(string what) => new($"{what} {Marshal.GetLastPInvokeError()}");

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { public uint Length; public nint Descriptor; public int InheritHandle; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Cb;
        public nint Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2Size;
        public nint Reserved2, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public nint Process, Thread; public uint ProcessId, ThreadId; }

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
        public ulong R1, R2, R3, W1, W2, W3;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out nint read, out nint write, SecurityAttributes* sa, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFileW(string name, uint access, uint share, SecurityAttributes* sa, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateJobObjectW(nint attributes, nint name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(char* app, char* cmd, nint pa, nint ta, int inherit, uint flags, char* env, char* cwd, StartupInfo* si, ProcessInformation* pi);

    [LibraryImport("kernel32.dll")]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint process, out uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
