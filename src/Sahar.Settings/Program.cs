using Sahar.Core;
using Sahar.Core.Diagnostics;
using Sahar.Core.Settings;

namespace Sahar.Settings;

/// <summary>
/// «إعداداتي» (plan 0.10c): one window at a time; a second start brings the first to the front and exits 0. Without a
/// working WebView2 it says so (not in test mode) and exits 2. It fully exits when closed.
/// </summary>
internal static class Program
{
    public const int ExitWebView2 = 2;
    public static int ExitCode { get; set; }

    private static bool RuntimeMissing()
    {
        try { return string.IsNullOrEmpty(Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString()); }
        catch (Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException) { return true; }
        catch (Exception) { return false; }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        using var mutex = new Mutex(initiallyOwned: true, @"Local\Sahar.Settings", out bool first);
        if (!first)
        {
            nint other = Native.FindWindowW(HostWindow.ClassName, null);
            if (other != 0)
            {
                if (Native.IsIconic(other)) Native.ShowWindow(other, Native.SW_RESTORE);
                Native.SetForegroundWindow(other);
            }
            SettingsLog.Write("second start: the open window comes to the front");
            return 0;
        }
        try
        {
            SettingsLog.Write($"start {CoreInfo.Version} (test mode {options.TestMode})");
            var ui = new UiSyncContext();
            SynchronizationContext.SetSynchronizationContext(ui);
            var window = new HostWindow(ui);
            var engine = new EngineLink(options.TestMode);
            var bridge = new SettingsBridge(SettingsStore.DefaultPath, engine, new Locator(), () => DateTimeOffset.Now, CleanStart.RoamingFolder)
            {
                // 0.13: Claude Code's hooks run the engine next to this app («اربطها»); only when it is really there.
                EnginePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Sahar.Engine.exe")) is var exe && File.Exists(exe) ? exe : null,
            };
            var web = new WebHost(window, options, bridge);
            bool closing = false;
            window.CloseRequested += async () =>
            {
                if (closing) return;
                closing = true;
                SettingsLog.Write("close");
                await web.FlushAsync();   // a name typed a moment ago is saved first (review fix m8)
                web.Close();
                window.Close();
            };
            if (options.TestMode) new TestPipe(ui, window, web, options).Start();
            ui.Post(async _ =>
            {
                try
                {
                    await web.StartAsync();
                    window.Show();
                    web.Shown();
                }
                catch (Exception e)
                {
                    // WebView2 is named only when it is really missing (review fix m9).
                    bool missing = RuntimeMissing();
                    SettingsLog.Write($"webview2: failed ({(missing ? "runtime missing" : "runtime present")}, {LogRules.Kind(e)})");
                    ExitCode = ExitWebView2;
                    if (!options.TestMode)
                        Native.MessageBoxW(0, SettingsAppPolicy.StartFailed(missing), HostWindow.Title,
                            Native.MB_OK | Native.MB_ICONINFORMATION | Native.MB_RTLREADING | Native.MB_RIGHT);
                    window.Close();
                }
            }, null);
            int code = HostWindow.Loop();
            SettingsLog.Write($"exit {code}");
            return code;
        }
        catch (Exception e)
        {
            SettingsLog.Write($"fatal: {LogRules.Kind(e)}");
            return 1;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
