using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Sahar.Core.Geometry;
using Sahar.Core.Surfaces;

namespace Sahar.Settings.Surfaces;

/// <summary>
/// <c>Sahar.Settings.exe --surfaces</c> (0.16): her warm, hidden surface host. The engine starts it once; it makes two
/// transparent WebView2 windows at once (the panel: pickers, input cards, ask bubbles, tray menu, «الدرج»; the chat
/// window) and keeps them hidden, so a surface opens in well under 150 ms. The engine sends what to show and where her
/// body and monitor are; the page renders, measures its card, the host places the window (PanelPlacement) and shows it.
/// It quits when the engine has been gone for 15 s. Logs kinds, sequence numbers and milliseconds — never a model.
/// </summary>
internal sealed class SurfaceHost
{
    public const string Host = "sahar.surfaces";
    public const string Origin = "https://" + Host + "/";
    /// <summary>The transparent margin around a card that holds its shadow (DIP).</summary>
    private const float ML = 24, MT = 20, MR = 24, MB = 32;
    public static UiSyncContext? Ui { get; private set; }

    private sealed class Surface(string name, VisualWindow win)
    {
        public string Name { get; } = name;
        public VisualWindow Win { get; } = win;
        public bool Ready;
        public SurfaceMsg? Showing;        // the show being placed / shown
        public SurfaceMsg? Queued;         // a show that came before the page was ready
        public bool Open;
        public bool Placing;
        public readonly Stopwatch Clock = new();
        public double LastMs;
        public (int X, int Y, int W, int H) Card;
        public int Opens;
    }

    private readonly Options _options;
    private readonly Dictionary<string, Surface> _s = [];
    private readonly Dictionary<nint, (int X, int Y)> _chatPlace = [];
    private SurfacePipe? _pipe;
    private CoreWebView2Environment? _env;
    private int _blocked;

    private SurfaceHost(Options options) => _options = options;

    public static int Run(Options options)
    {
        using var mutex = new Mutex(true, @"Local\Sahar.Surfaces", out bool first);
        if (!first) { SettingsLog.Write("surfaces: already running"); return 0; }
        try
        {
            var ui = new UiSyncContext();
            Ui = ui;
            SynchronizationContext.SetSynchronizationContext(ui);
            VisualWindow.Register();
            var host = new SurfaceHost(options);
            var panel = new VisualWindow("panel", "Sahar", tool: true, topmost: true);
            var chat = new VisualWindow("chat", "سحورة", tool: false, topmost: false);
            ui.Hwnd = panel.Hwnd;
            host._s["panel"] = new Surface("panel", panel);
            host._s["chat"] = new Surface("chat", chat);
            ui.Post(async _ => await host.StartAsync(), null);
            int code = Loop();
            SettingsLog.Write($"surfaces: exit {code}");
            return code;
        }
        catch (Exception e)
        {
            SettingsLog.Write($"surfaces: fatal ({Sahar.Core.Diagnostics.LogRules.Kind(e)})");
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static unsafe int Loop()
    {
        Native.MSG m;
        while (Native.GetMessageW(&m, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(&m);
            Native.DispatchMessageW(&m);
        }
        return (int)m.wParam;
    }

    public static string DataFolder(Options o) => o.WebViewData is { } d ? d + "-surfaces"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar", "Surfaces.WebView2");

    private async Task StartAsync()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var envOptions = new CoreWebView2EnvironmentOptions
            {
                AreBrowserExtensionsEnabled = false,
                IsCustomCrashReportingEnabled = true,
                AdditionalBrowserArguments = Sahar.Core.Settings.SettingsAppPolicy.BrowserArguments(_options.TestMode),
            };
            Directory.CreateDirectory(DataFolder(_options));
            _env = await CoreWebView2Environment.CreateAsync(null, DataFolder(_options), envOptions).WaitAsync(TimeSpan.FromSeconds(60));
            foreach (var s in _s.Values)
            {
                await s.Win.StartAsync(_env, 1f).WaitAsync(TimeSpan.FromSeconds(60));
                Configure(s);
            }
            SettingsLog.Write($"surfaces: warm in {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception e)
        {
            SettingsLog.Write($"surfaces: webview2 failed ({Sahar.Core.Diagnostics.LogRules.Kind(e)})");
            Native.PostQuitMessage(2);
            return;
        }
        _pipe = new SurfacePipe(Ui!, Received);
        _pipe.Gone += () => Native.PostQuitMessage(0);
        _pipe.Lost += () => { foreach (var x in _s.Values) if (x.Open) Close(x, null); };   // she quit or restarted: nothing of hers stays up
        _pipe.Start();
    }

    private void Configure(Surface s)
    {
        var view = s.Win.View!;
        var set = view.Settings;
        set.AreDevToolsEnabled = _options.TestMode;
        set.AreDefaultContextMenusEnabled = false;
        set.IsStatusBarEnabled = false;
        set.IsZoomControlEnabled = false;
        set.IsReputationCheckingRequired = false;
        set.IsGeneralAutofillEnabled = false;
        set.IsPasswordAutosaveEnabled = false;
        set.AreBrowserAcceleratorKeysEnabled = false;
        set.IsBuiltInErrorPageEnabled = false;
        set.AreHostObjectsAllowed = false;
        set.IsSwipeNavigationEnabled = false;
        view.SetVirtualHostNameToFolderMapping(Host, WebHost.WebFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        view.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        view.WebResourceRequested += (_, e) =>
        {
            if (e.Request.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            _blocked++;
            SettingsLog.Write("surfaces: blocked a request");
            e.Response = _env!.CreateWebResourceResponse(null, 403, "Forbidden", "");
        };
        view.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            _blocked++;
        };
        view.NewWindowRequested += (_, e) => e.Handled = true;
        view.WebMessageReceived += (_, e) => FromPage(s, e);
        s.Win.Activated += active => { if (!active) Deactivated(s); };
        s.Win.CloseAsked += () => { if (s.Open) Close(s, "closed"); };
        view.Navigate(Origin + "surfaces/" + (s.Name == "chat" ? "chat.html" : "panel.html"));
    }

    // ---- engine → host ----

    private void Received(SurfaceMsg m)
    {
        switch (m.T)
        {
            case "show" when m.Surface is { } name && _s.TryGetValue(name, out var s):
                Show(s, m);
                break;
            case "update" when m.Surface is { } name && _s.TryGetValue(name, out var s) && s.Ready:
                PostRaw(s, "update", m, includeModel: true);
                break;
            case "hide" when m.Surface is { } name && _s.TryGetValue(name, out var s):
                if (s.Open) Close(s, null);
                break;
            case "focus" when m.Surface is { } name && _s.TryGetValue(name, out var s):
                if (s.Open) { s.Win.Front(); Post(s, """{"t":"focus"}"""); }
                break;
            case "theme":
                foreach (var x in _s.Values) Post(x, m.Dark ? """{"t":"theme","dark":true}""" : """{"t":"theme","dark":false}""");
                break;
            case "capture":
                foreach (var x in _s.Values)
                {
                    x.Win.SetCaptureExcluded(!m.Capturable);
                    if (x.Showing is { } shown) shown.Capturable = m.Capturable;
                }
                break;
            case "debug":
                _pipe?.Send(new SurfaceMsg { T = "reply", Seq = m.Seq, Model = Debug() });
                break;
            case "eval" when _options.TestMode && m.Surface is { } name && _s.TryGetValue(name, out var s):
                Eval(s, m);
                break;
        }
    }

    private void Show(Surface s, SurfaceMsg m)
    {
        s.Clock.Restart();
        s.Showing = m;
        s.Placing = true;
        s.Open = true;
        s.Win.SetCaptureExcluded(!m.Capturable);
        s.Win.Wake();
        if (!s.Ready) { s.Queued = m; return; }
        PostRaw(s, "show", m, includeModel: true);
    }

    private void PostRaw(Surface s, string t, SurfaceMsg m, bool includeModel)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("t", t);
            w.WriteNumber("seq", m.Seq);
            if (includeModel && m.Model is { Length: > 0 } model) { w.WritePropertyName("model"); w.WriteRawValue(model, skipInputValidation: false); }
            w.WriteBoolean("dark", m.Dark);
            w.WriteBoolean("still", m.Still);
            if (t == "show")
            {
                w.WriteStartObject("margin");
                w.WriteNumber("l", ML); w.WriteNumber("t", MT);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        Post(s, Encoding.UTF8.GetString(ms.ToArray()));
    }

    private static void Post(Surface s, string json)
    {
        try { s.Win.View?.PostWebMessageAsJson(json); }
        catch (Exception e) { SettingsLog.Write($"surfaces: post failed ({e.GetType().Name})"); }
    }

    // ---- page → host ----

    private void FromPage(Surface s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
        JsonElement r;
        try { r = JsonDocument.Parse(e.WebMessageAsJson).RootElement; }
        catch (JsonException) { return; }
        string t = r.TryGetProperty("t", out var tt) ? tt.GetString() ?? "" : "";
        int seq = r.TryGetProperty("seq", out var sq) && sq.ValueKind == JsonValueKind.Number ? sq.GetInt32() : s.Showing?.Seq ?? 0;
        switch (t)
        {
            case "ready":
                s.Ready = true;
                if (s.Queued is { } q) { s.Queued = null; PostRaw(s, "show", q, includeModel: true); }
                else if (!s.Open) s.Win.Rest();   // warm and quiet until asked
                break;
            case "measured" when s.Showing is { } m && m.Seq == seq:
                Placed(s, m, r.GetProperty("w").GetSingle(), r.GetProperty("h").GetSingle(),
                    again: r.TryGetProperty("again", out var ag) && ag.ValueKind == JsonValueKind.True);
                break;
            case "shown" when s.Showing is { } m && m.Seq == seq:
                s.LastMs = s.Clock.Elapsed.TotalMilliseconds;
                s.Placing = false;
                s.Opens++;
                SettingsLog.Write($"surfaces: {s.Name} {m.Kind} shown ({(int)s.LastMs} ms)");
                break;
            case "hidden":
                if (!s.Open) s.Win.Hide();
                break;
            case "drag":
                if (s.Name == "chat") s.Win.Drag();
                break;
            case "event":
                string kind = r.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
                string? value = r.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (kind == "close") { Close(s, "closed"); break; }
                _pipe?.Send(new SurfaceMsg { T = "event", Surface = s.Name, Kind = kind, Value = value, Seq = s.Showing?.Seq ?? seq });
                break;
        }
    }

    private void Placed(Surface s, SurfaceMsg m, float wDip, float hDip, bool again = false)
    {
        float dpi = m.Dpi > 0 ? m.Dpi : 1f;
        var work = new RectF(m.Wx, m.Wy, m.Ww, m.Wh);
        var size = new SizeF(wDip * dpi, hDip * dpi);
        var body = new RectF(m.X, m.Y, m.W, m.H);
        RectF card;
        bool left = false;
        float claspY = 0;
        switch (m.Mode)
        {
            case "above":
                card = PanelPlacement.Above(body, work, size, dpi);
                break;
            case "anchor":
                card = PanelPlacement.Anchor(m.X, m.Y, work, size, dpi);
                break;
            case "window":
                var p = PanelPlacement.Place(body, work, size, dpi, PanelPlacement.ChatGapDip);
                card = p.Rect;
                nint mon = SN.MonitorFromPoint(new SN.POINT { X = (int)(work.X + work.Width / 2), Y = (int)(work.Y + work.Height / 2) }, 2);
                if (_chatPlace.TryGetValue(mon, out var at))
                    card = new RectF(Math.Clamp(at.X, work.X, MathF.Max(work.X, work.Right - size.Width)), Math.Clamp(at.Y, work.Y, MathF.Max(work.Y, work.Bottom - size.Height)), size.Width, size.Height);
                break;
            default:
                var chat = _s["chat"];
                RectF? avoid = s.Name == "panel" && chat.Open
                    ? new RectF(chat.Card.X - 8 * dpi, chat.Card.Y - 8 * dpi, chat.Card.W + 16 * dpi, chat.Card.H + 16 * dpi) : null;
                var b = PanelPlacement.Place(body, work, size, dpi, avoid: avoid);
                card = b.Rect;
                left = b.OnHerLeft;
                claspY = (b.ClaspY - b.Rect.Y) / dpi;
                break;
        }
        int x = (int)MathF.Round(card.X - ML * dpi), y = (int)MathF.Round(card.Y - MT * dpi);
        int w = (int)MathF.Ceiling(card.Width + (ML + MR) * dpi), h = (int)MathF.Ceiling(card.Height + (MT + MB) * dpi);
        s.Card = ((int)card.X, (int)card.Y, (int)card.Width, (int)card.Height);
        s.Win.Place(x, y, w, h, dpi, show: true, activate: !again);
        Post(s, $$"""{"t":"place","seq":{{m.Seq}},"left":{{(left ? "true" : "false")}},"claspY":{{claspY.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"mode":"{{m.Mode ?? "beside"}}","again":{{(again ? "true" : "false")}}}""");
    }

    private void Deactivated(Surface s)
    {
        // A panel closes when he clicks elsewhere (as the old popups did); the chat window stays.
        if (s.Name != "panel" || !s.Open || s.Placing) return;
        Close(s, "closed");
    }

    private void Close(Surface s, string? tell)
    {
        if (!s.Open) return;
        s.Open = false;
        s.Placing = false;
        if (s.Name == "chat")
        {
            var r = s.Win.Rect;
            nint mon = SN.MonitorFromWindow(s.Win.Hwnd, 2);
            _chatPlace[mon] = (r.X + (int)(ML * (s.Showing?.Dpi ?? 1)), r.Y + (int)(MT * (s.Showing?.Dpi ?? 1)));
        }
        Post(s, """{"t":"hide"}""");
        var seq = s.Showing?.Seq ?? 0;
        _ = Task.Delay(300).ContinueWith(_ => Ui!.Post(__ => { if (!s.Open) s.Win.Hide(); }, null));
        if (tell is not null) _pipe?.Send(new SurfaceMsg { T = "event", Surface = s.Name, Kind = tell, Seq = seq });
    }

    private string Debug()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("blocked", _blocked);
            foreach (var s in _s.Values)
            {
                w.WriteStartObject(s.Name);
                w.WriteBoolean("ready", s.Ready);
                w.WriteBoolean("open", s.Open && !s.Placing);
                w.WriteBoolean("visible", s.Win.Visible);
                w.WriteString("kind", s.Open ? s.Showing?.Kind : null);
                w.WriteNumber("seq", s.Showing?.Seq ?? 0);
                w.WriteNumber("ms", Math.Round(s.LastMs, 1));
                w.WriteNumber("opens", s.Opens);
                w.WriteNumber("x", s.Card.X); w.WriteNumber("y", s.Card.Y); w.WriteNumber("w", s.Card.W); w.WriteNumber("h", s.Card.H);
                w.WriteBoolean("dark", s.Showing?.Dark ?? false);
                w.WriteBoolean("excluded", !(s.Showing?.Capturable ?? true));
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async void Eval(Surface s, SurfaceMsg m)
    {
        string result;
        try { result = await s.Win.View!.ExecuteScriptAsync(m.Value ?? "null"); }
        catch (Exception e) { result = JsonSerializer.Serialize(e.GetType().Name); }
        _pipe?.Send(new SurfaceMsg { T = "reply", Seq = m.Seq, Model = result });
    }
}
