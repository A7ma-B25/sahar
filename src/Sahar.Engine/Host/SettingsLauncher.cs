using System.Diagnostics;
using Microsoft.Win32;
using Sahar.Engine.Diagnostics;

namespace Sahar.Engine.Host;

/// <summary>
/// The «إعداداتي» door (spec §5 footer): the settings app (0.10c) — <c>&lt;engine dir&gt;\Sahar.Settings\Sahar.Settings.exe</c> —
/// needs the WebView2 runtime. Until both exist the footer button is hidden (owner rule: hidden, not greyed out).
/// </summary>
internal static class SettingsLauncher
{
    private const string WebView2 = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "Sahar.Settings", "Sahar.Settings.exe");

    public static bool Available => File.Exists(ExePath) && WebView2Installed();

    private static bool WebView2Installed()
    {
        static bool Ok(RegistryKey root, string path)
        {
            try
            {
                using var k = root.OpenSubKey(path);
                return k?.GetValue("pv") is string v && v.Length > 0 && v != "0.0.0.0";
            }
            catch (Exception) { return false; }
        }
        return Ok(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\" + WebView2) || Ok(Registry.CurrentUser, @"Software\" + WebView2);
    }

    public static bool Open(bool testMode)
    {
        if (!Available) return false;
        try
        {
            Process.Start(new ProcessStartInfo(ExePath) { UseShellExecute = false, Arguments = testMode ? "--test-mode" : "" });
            Log.Write("settings: opened");
            return true;
        }
        catch (Exception e)
        {
            Log.Write($"settings: not opened ({e.GetType().Name})");
            return false;
        }
    }
}
