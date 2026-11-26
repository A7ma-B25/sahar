using System.Diagnostics;
using Sahar.Core.Diagnostics;
using Sahar.Core.Settings;

namespace Sahar.Settings;

/// <summary>0.15 T7 «ابعت سجلّي» while she is not running (she crashed, or he quit her): the app makes the same zip
/// itself (<see cref="DiagBundle"/>), on his Desktop, and opens Explorer on it. settings.log never gets the path.</summary>
internal static class Diag
{
    public static (string Name, bool OnDesktop)? Make(bool testMode)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bool onDesktop = desktop.Length > 0 && Directory.Exists(desktop);
            SettingsLog.Write("diag: making the bundle (she is not running)");
            var sources = DiagBundle.Read(SettingsLog.Folder, SettingsStore.DefaultPath, DiagBundle.Versions("settings"), DiagBundle.PrivateNames());
            var path = DiagBundle.Write(onDesktop ? desktop : SettingsLog.Folder, sources, DateTimeOffset.Now);
            SettingsLog.Write(onDesktop ? "diag: bundle on the desktop" : "diag: bundle in her folder (no desktop)");
            if (!testMode)
                try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose(); }
                catch (Exception e) { SettingsLog.Write($"diag: explorer failed ({LogRules.Kind(e)})"); }
            return (Path.GetFileName(path), onDesktop);
        }
        catch (Exception e)
        {
            SettingsLog.Write($"diag: failed ({LogRules.Kind(e)})");
            return null;
        }
    }
}
