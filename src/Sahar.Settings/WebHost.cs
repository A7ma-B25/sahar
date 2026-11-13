using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Sahar.Core.Design;
using Sahar.Core.Settings;

namespace Sahar.Settings;

/// <summary>
/// The WebView2 inside the window (plan 0.10c Task 3): the page is served from <c>https://sahar.settings/</c> (the
/// <c>web\</c> folder beside the exe); every other request is answered 403 and counted, navigation elsewhere is
/// cancelled, new windows are refused. Page messages go to <see cref="SettingsBridge"/>; "theme" colours the caption.
/// </summary>
internal sealed class WebHost(HostWindow window, Options options, SettingsBridge bridge)
{
    public const string Host = "sahar.settings";
    public const string Origin = "https://" + Host + "/";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);
    private CoreWebView2Environment? _env;
    private CoreWebView2Controller? _controller;

    public CoreWebView2? View => _controller?.CoreWebView2;
    public int Blocked { get; private set; }
    public bool Ready { get; private set; }
    public bool Dark { get; private set; }
    public event Action? PageReady;

    public static string WebFolder => Path.Combine(AppContext.BaseDirectory, "web");

    public static string DataFolder(Options o) => o.WebViewData
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar", "Settings.WebView2");

    public async Task StartAsync()
    {
        // No background traffic of WebView2's own (the README promises the app never goes online): crash reports stay
        // on the PC, no SmartScreen lookups (below), no component or update pings. Test mode also keeps a covered
        // window painting (the CI desktop is small and her card sits on top).
        var envOptions = new CoreWebView2EnvironmentOptions
        {
            AreBrowserExtensionsEnabled = false,
            IsCustomCrashReportingEnabled = true,
            AdditionalBrowserArguments = SettingsAppPolicy.BrowserArguments(options.TestMode),
        };
        // Its own data folder; one that cannot be made (a file in the way, no rights) fails here, at once.
        Directory.CreateDirectory(DataFolder(options));
        _env = await CoreWebView2Environment.CreateAsync(null, DataFolder(options), envOptions).WaitAsync(StartTimeout);
        _controller = await _env.CreateCoreWebView2ControllerAsync(window.Hwnd).WaitAsync(StartTimeout);
        // Her colours before the page paints: dark settings or a dark Windows never flash light (review fix m7).
        Dark = SettingsAppPolicy.IsDark(StartAppearance(), SystemDark());
        var start = Tokens.For(Dark);
        _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, start.Bg.R, start.Bg.G, start.Bg.B);
        window.SetCaption(Rgb(start.Bg), Rgb(start.Ink), Dark);
        var view = _controller.CoreWebView2;
        var s = view.Settings;
        s.AreDevToolsEnabled = options.TestMode;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = true;   // Ctrl + wheel and Ctrl ± (review fix m12)
        s.IsReputationCheckingRequired = false;   // no SmartScreen: nothing about the page leaves the PC
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.AreHostObjectsAllowed = false;
        view.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
        view.SetVirtualHostNameToFolderMapping(Host, WebFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        view.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        view.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            Blocked++;
            SettingsLog.Write("network: blocked a request");   // never the address
            e.Response = _env.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
        view.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            Blocked++;
            SettingsLog.Write("network: blocked a navigation");
        };
        view.NewWindowRequested += (_, e) => e.Handled = true;
        view.WebMessageReceived += (_, e) => OnMessage(e);
        window.Resized += Fit;
        window.Activated += active =>
        {
            if (!active || !Ready) return;
            _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
            Push("""{"type":"focus"}""");   // the page asks again: the engine may have saved meanwhile
        };
        Fit();
        view.Navigate(Origin + "index.html");
    }

    /// <summary>The window is on screen now: the page is told it is visible (it was made while the window was hidden).</summary>
    public void Shown()
    {
        if (_controller is null) return;
        _controller.IsVisible = false;
        _controller.IsVisible = true;
        Fit();
    }

    private void Fit()
    {
        if (_controller is null) return;
        // Minimised: the page rests (no frames, no timers); restored: it lives again.
        bool minimised = Native.IsIconic(window.Hwnd);
        if (_controller.IsVisible == minimised) _controller.IsVisible = !minimised;
        if (minimised) return;
        var (w, h) = window.ClientSize;
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
    }

    /// <summary>The appearance saved in settings.json (System when the file can't be read).</summary>
    private static Appearance StartAppearance()
    {
        try { return SettingsStore.Health(SettingsStore.DefaultPath) == SettingsHealth.Ok ? SettingsStore.Load(SettingsStore.DefaultPath).Appearance : Appearance.System; }
        catch (Exception) { return Appearance.System; }
    }

    /// <summary>Windows' apps in dark mode (Personalize\AppsUseLightTheme = 0).</summary>
    private static bool SystemDark()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    private TaskCompletionSource? _flushed;

    /// <summary>The window is closing: the page saves what is still waiting (2 s at most), then the window goes.</summary>
    public async Task FlushAsync()
    {
        if (!Ready || View is null) return;
        _flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Push("""{"type":"closing"}""");
        try { await _flushed.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { SettingsLog.Write("close: the page did not answer in time"); }
    }

    public bool ZoomControl => View?.Settings.IsZoomControlEnabled == true;

    /// <summary>A message the page did not ask for ({"type":"focus"}).</summary>
    public void Push(string json)
    {
        try { View?.PostWebMessageAsString(json); }
        catch (Exception e) { SettingsLog.Write($"push failed ({e.GetType().Name})"); }
    }

    private async void OnMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }
        if (!e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
        // The host's own: the page is ready; the page's light or dark (caption colours).
        string? type = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var t)) type = t.GetString();
            if (type == "flushed")
            {
                _flushed?.TrySetResult();
                return;
            }
            if (type == "ready")
            {
                Ready = true;
                SettingsLog.Write("page: ready");
                _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
                PageReady?.Invoke();
                return;
            }
            if (type == "theme")
            {
                Dark = doc.RootElement.TryGetProperty("dark", out var d) && d.GetBoolean();
                var tokens = Tokens.For(Dark);
                window.SetCaption(Rgb(tokens.Bg), Rgb(tokens.Ink), Dark);
                if (_controller is not null)
                    _controller.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, tokens.Bg.R, tokens.Bg.G, tokens.Bg.B);
                return;
            }
        }
        catch (JsonException) { }
        string reply = await bridge.HandleAsync(json);
        if (type is not ("get" or "prayer-today")) SettingsLog.Write($"page: {type ?? "?"}");   // the kind only
        Push(reply);
    }

    private static uint Rgb(Sahar.Core.Art.Rgba c) => ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    public void Close()
    {
        try { _controller?.Close(); }
        catch (Exception) { /* closing anyway */ }
        _controller = null;
    }
}
