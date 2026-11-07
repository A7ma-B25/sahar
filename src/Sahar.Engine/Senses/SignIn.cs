using System.Runtime.InteropServices;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Senses;

/// <summary>When you signed in to this Windows session (for her hello, motion 18). Null if Windows won't say.</summary>
internal static unsafe partial class SignIn
{
    private const int WTSSessionInfo = 24;
    private const uint CurrentSession = 0xFFFFFFFF;
    // WTSINFOW: State, SessionId, 6 counters (32 bytes), WinStationName[32], Domain[17], UserName[21] (wchar),
    // then 8-byte aligned FILETIMEs: Connect 176, Disconnect 184, LastInput 192, Logon 200, Current 208.
    private const int LogonTimeOffset = 200, InfoSize = 216;

    [LibraryImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW")]
    private static partial int WTSQuerySessionInformation(nint server, uint session, int infoClass, nint* buffer, uint* bytes);

    [LibraryImport("wtsapi32.dll")]
    private static partial void WTSFreeMemory(nint memory);

    public static DateTimeOffset? Time()
    {
        nint buffer = 0;
        uint bytes = 0;
        try
        {
            if (WTSQuerySessionInformation(0, CurrentSession, WTSSessionInfo, &buffer, &bytes) == 0 || buffer == 0 || bytes < InfoSize) return null;
            long logon = *(long*)(buffer + LogonTimeOffset);
            return logon > 0 ? DateTimeOffset.FromFileTime(logon) : null;
        }
        catch (Exception e)
        {
            Log.Write($"sign-in time unknown: {e.Message}");
            return null;
        }
        finally
        {
            if (buffer != 0) WTSFreeMemory(buffer);
        }
    }
}
