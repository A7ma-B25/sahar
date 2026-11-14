using Microsoft.Win32;
using Sahar.Core.Update;

namespace Sahar.Engine.Host;

/// <summary>Whether she runs from the installer's folder: the uninstall entry's InstallLocation (per user).</summary>
internal static class InstallInfo
{
    public static bool Installed()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{UpdateRules.InstallerAppId}_is1");
            return UpdateRules.IsInstalled(key?.GetValue("InstallLocation") as string, AppContext.BaseDirectory);
        }
        catch (Exception) { return false; }
    }
}
