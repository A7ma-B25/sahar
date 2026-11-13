using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Sahar.Core.Protocol;

namespace Sahar.Settings;

/// <summary>
/// Test mode only: the pipe <c>sahar-settings</c> the UI stage drives the page with (plan 0.10c Task 3). Same JSON
/// lines as the engine's pipe; data replies are <c>key=value</c> in the error slot, text as base64. Keys and typing go
/// through DevTools input events, so they are trusted (Tab moves focus, Space presses a button).
/// </summary>
internal sealed class TestPipe(UiSyncContext ui, HostWindow window, WebHost web, Options options)
{
    public const string Name = "sahar-settings";
    private static readonly UTF8Encoding Utf8 = new(false);

    public void Start() => _ = Task.Run(Accept);

    private async Task Accept()
    {
        while (true)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(Name, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
            }
            catch (Exception)
            {
                await Task.Delay(200);
                continue;
            }
            _ = Task.Run(() => Serve(server));
        }
    }

    private async Task Serve(NamedPipeServerStream stream)
    {
        try
        {
            await using (stream)
            {
                using var reader = new StreamReader(stream, Utf8);
                await using var writer = new StreamWriter(stream, Utf8) { NewLine = "\n", AutoFlush = true };
                while (await reader.ReadLineAsync() is { } line)
                {
                    PipeReply reply;
                    try
                    {
                        var req = ProtocolJson.ParseRequest(line);
                        reply = await ui.Run(() => Handle(req)).WaitAsync(TimeSpan.FromSeconds(20));
                    }
                    catch (Exception e)
                    {
                        reply = new PipeReply(false, e.GetType().Name + ": " + e.Message);
                    }
                    await writer.WriteLineAsync(ProtocolJson.Write(reply));
                }
            }
        }
        catch (Exception) { /* the test went away */ }
    }

    private static string Arg(PipeRequest r, string k) => r.Args?.TryGetValue(k, out var v) == true ? v : "";
    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private static string Js(string s) => JsonSerializer.Serialize(s, TestJson.Default.String);

    private async Task<string> Eval(string script)
    {
        if (web.View is not { } view) throw new InvalidOperationException("no page yet");
        return await view.ExecuteScriptAsync(script);
    }

    private async Task<PipeReply> Handle(PipeRequest r)
    {
        switch (r.Name)
        {
            case "state":
            {
                var (cw, ch) = window.ClientSize;
                var (wx, wy, ww, wh) = window.WindowRect;
                string head = FormattableString.Invariant(
                    $"ready={B(web.Ready)} blocked={web.Blocked} clientW={cw} clientH={ch} dpi={window.Dpi:0.###} winX={wx} winY={wy} winW={ww} winH={wh} pid={Environment.ProcessId}");
                if (!web.Ready) return new PipeReply(true, head);
                var json = await Eval("JSON.stringify(window.sahar ? window.sahar.state() : {})");
                using var doc = JsonDocument.Parse(JsonSerializer.Deserialize(json, TestJson.Default.String) ?? "{}");
                var o = doc.RootElement;
                string S(string k) => o.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                return new PipeReply(true, head + $" page={S("page")} theme={S("theme")} dir={S("dir")} title64={B64(S("title"))} h1_64={B64(S("h1"))} focus64={B64(S("focus"))} broken={S("broken")}");
            }
            case "nav":
                return new PipeReply(await Eval($"window.sahar.nav({Js(Arg(r, "page"))})") == "true");
            case "click":
                return new PipeReply(await Eval($"(() => {{ const e = document.querySelector({Js(Arg(r, "sel"))}); if (!e) return false; e.click(); return true; }})()") == "true");
            case "focus":
                return new PipeReply(await Eval($"(() => {{ const e = document.querySelector({Js(Arg(r, "sel"))}); if (!e) return false; e.focus(); return document.activeElement === e; }})()") == "true");
            case "type":
            {
                var sel = Arg(r, "sel");
                if (await Eval($"(() => {{ const e = document.querySelector({Js(sel)}); if (!e) return false; e.focus(); e.select && e.select(); return true; }})()") != "true")
                    return new PipeReply(false, "no such element");
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(Arg(r, "text64")));
                await web.View!.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new InsertText(text), TestJson.Default.InsertText));
                return new PipeReply(true);
            }
            case "key":
                await Key(Arg(r, "key"), Arg(r, "mods"));
                return new PipeReply(true);
            case "snapshot":
            {
                var dir = Path.Combine(options.SnapshotDir ?? Path.GetTempPath(), "settings-html");
                Directory.CreateDirectory(dir);
                var html = JsonSerializer.Deserialize(await Eval("document.documentElement.outerHTML"), TestJson.Default.String) ?? "";
                await File.WriteAllTextAsync(Path.Combine(dir, Arg(r, "name") + ".html"), "<!doctype html>\n" + html);
                return new PipeReply(true);
            }
            case "capture":
            {
                var dir = options.SnapshotDir ?? Path.GetTempPath();
                Directory.CreateDirectory(dir);
                await using var file = File.Create(Path.Combine(dir, Arg(r, "name") + ".png"));
                await web.View!.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);
                return new PipeReply(true);
            }
            case "emulate":
            {
                var features = new List<MediaFeature>();
                if (r.Args?.TryGetValue("reducedMotion", out var rm) == true) features.Add(new("prefers-reduced-motion", rm == "true" ? "reduce" : ""));
                if (r.Args?.TryGetValue("theme", out var th) == true) features.Add(new("prefers-color-scheme", th is "dark" or "light" ? th : ""));
                await web.View!.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",
                    JsonSerializer.Serialize(new EmulatedMedia(features), TestJson.Default.EmulatedMedia));
                return new PipeReply(true);
            }
            case "eval":
            {
                var code = Encoding.UTF8.GetString(Convert.FromBase64String(Arg(r, "code64")));
                return new PipeReply(true, "result64=" + B64(await Eval(code)));
            }
            case "close":
                Native.PostMessageW(window.Hwnd, Native.WM_CLOSE, 0, 0);
                return new PipeReply(true);
            default:
                return new PipeReply(false, $"unknown '{r.Name}'");
        }
    }

    private static string B(bool b) => b ? "true" : "false";

    /// <summary>A trusted key press: Tab, Enter, Escape, Space, Arrow*, Home, End, a letter or digit; mods "ctrl,alt,shift".</summary>
    private async Task Key(string key, string mods)
    {
        int m = 0;
        foreach (var part in mods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            m |= part.ToLowerInvariant() switch { "alt" => 1, "ctrl" => 2, "meta" => 4, "shift" => 8, _ => 0 };
        (string k, string code, int vk, string? text) = key switch
        {
            "Tab" => ("Tab", "Tab", 9, null),
            "Enter" => ("Enter", "Enter", 13, "\r"),
            "Escape" => ("Escape", "Escape", 27, null),
            "Space" => (" ", "Space", 32, " "),
            "ArrowLeft" => ("ArrowLeft", "ArrowLeft", 37, null),
            "ArrowUp" => ("ArrowUp", "ArrowUp", 38, null),
            "ArrowRight" => ("ArrowRight", "ArrowRight", 39, null),
            "ArrowDown" => ("ArrowDown", "ArrowDown", 40, null),
            "Home" => ("Home", "Home", 36, null),
            "End" => ("End", "End", 35, null),
            { Length: 1 } c when char.IsAsciiLetter(c[0]) => (c.ToLowerInvariant(), "Key" + c.ToUpperInvariant(), char.ToUpperInvariant(c[0]), (m & 3) == 0 ? c.ToLowerInvariant() : null),
            { Length: 1 } d when char.IsAsciiDigit(d[0]) => (d, "Digit" + d, d[0], (m & 3) == 0 ? d : null),
            { Length: >= 2 } f when f[0] == 'F' && int.TryParse(f.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) => (f, f, 0x70 + n - 1, null),
            _ => throw new ArgumentException($"unknown key '{key}'"),
        };
        if (key == "Tab" && (m & 8) != 0) text = null;
        var down = new KeyEvent(text is null ? "rawKeyDown" : "keyDown", m, k, code, vk, vk, text ?? "", text ?? "");
        await web.View!.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(down, TestJson.Default.KeyEvent));
        var up = down with { Type = "keyUp", Text = "", UnmodifiedText = "" };
        await web.View!.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(up, TestJson.Default.KeyEvent));
    }
}

internal sealed record InsertText(string text);
internal sealed record MediaFeature(string name, string value);
internal sealed record EmulatedMedia(List<MediaFeature> features);
internal sealed record KeyEvent(
    [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
    [property: System.Text.Json.Serialization.JsonPropertyName("modifiers")] int Modifiers,
    [property: System.Text.Json.Serialization.JsonPropertyName("key")] string Key,
    [property: System.Text.Json.Serialization.JsonPropertyName("code")] string Code,
    [property: System.Text.Json.Serialization.JsonPropertyName("windowsVirtualKeyCode")] int WindowsVirtualKeyCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("nativeVirtualKeyCode")] int NativeVirtualKeyCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
    [property: System.Text.Json.Serialization.JsonPropertyName("unmodifiedText")] string UnmodifiedText);

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
[System.Text.Json.Serialization.JsonSerializable(typeof(InsertText))]
[System.Text.Json.Serialization.JsonSerializable(typeof(EmulatedMedia))]
[System.Text.Json.Serialization.JsonSerializable(typeof(KeyEvent))]
internal sealed partial class TestJson : System.Text.Json.Serialization.JsonSerializerContext;
