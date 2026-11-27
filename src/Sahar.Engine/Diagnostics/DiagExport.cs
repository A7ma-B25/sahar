using System.Diagnostics;
using Sahar.Core.Diagnostics;
using Sahar.Core.Settings;

namespace Sahar.Engine.Diagnostics;

/// <summary>0.15 T7 «ابعت سجلّي»: her logs in a zip on his Desktop (her folder when there is no Desktop), and Explorer
/// opened on it. What goes in, and what never does, is <see cref="DiagBundle"/>'s. engine.log says only that it was
/// made and where in words (desktop or her folder), never the path.</summary>
internal static class DiagExport
{
    public sealed record Made(string Path, bool OnDesktop);

    public static Made? Make(bool testMode)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            bool onDesktop = desktop.Length > 0 && Directory.Exists(desktop);
            Log.Write("diag: making the bundle");   // first, so this run's line is in it
            Perf.Write();   // 0.17.2: perf.txt (numbers only) fresh beside the logs
            var sources = DiagBundle.Read(Log.Folder, SettingsStore.DefaultPath, DiagBundle.Versions("engine"), DiagBundle.PrivateNames());
            var path = DiagBundle.Write(onDesktop ? desktop : Log.Folder, sources, DateTimeOffset.Now);
            Log.Write(onDesktop ? "diag: bundle on the desktop" : "diag: bundle in her folder (no desktop)");
            if (testMode) Log.Write("diag: explorer not opened (test mode)");
            else Select(path);
            return new Made(path, onDesktop);
        }
        catch (Exception e)
        {
            Log.Write($"diag: failed ({LogRules.Kind(e)})");
            return null;
        }
    }

    /// <summary>Explorer with the zip selected (explorer /select,"path").</summary>
    private static void Select(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false })?.Dispose(); }
        catch (Exception e) { Log.Write($"diag: explorer failed ({LogRules.Kind(e)})"); }
    }
}
