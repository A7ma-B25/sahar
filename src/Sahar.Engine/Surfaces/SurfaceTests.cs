using System.Text;
using Sahar.Core.Protocol;
using Sahar.Engine.Pipe;
using Windows.System;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// Test mode only (registered under it by App): her pipe drives the real pages — typing sets the field and fires the
/// page's own input and key handlers, clicks press the real buttons — and reads the host's state (placement, open
/// time, theme, capture exclusion).
/// </summary>
internal static class SurfaceTests
{
    private static string B64(string? s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
    private static string? Text(IReadOnlyDictionary<string, string>? a, string key) =>
        a?.TryGetValue(key, out var v) == true ? Encoding.UTF8.GetString(Convert.FromBase64String(v)) : null;
    private static string Js(string s) => System.Text.Json.JsonSerializer.Serialize(s, SurfaceTestJson.Default.String);

    public static void Register(PipeServer pipe, DispatcherQueue ui, SurfaceClient client, Panel panel, ChatHub chat)
    {
        async Task<PipeReply> Eval(string surface, string script)
        {
            var result = await client.Ask("eval", surface, script);
            return result is null ? new PipeReply(false, "no host") : new PipeReply(true, "result64=" + B64(result));
        }
        pipe.RegisterAsync("surface-debug", async _ =>
        {
            var json = await client.Ask("debug");
            return json is null ? new PipeReply(false, "no host") : new PipeReply(true, $"connected={(client.Connected ? "true" : "false")} json64={B64(json)}");
        });
        pipe.RegisterAsync("surface-eval", r => Eval(r.Args?.GetValueOrDefault("surface") ?? "panel", Text(r.Args, "script64") ?? "null"));
        // Types into the open card's field (or the picker's search) the way the page sees typing, then Enter if asked.
        pipe.RegisterAsync("bubble-type", r =>
        {
            var text = Text(r.Args, "text64") ?? "";
            bool enter = r.Args?.GetValueOrDefault("enter") == "true";
            return Eval("panel", $$"""
                (() => { const f = document.querySelector('[data-id=value],[data-id=search]'); if (!f) return 'no field';
                  f.focus(); f.value = {{Js(text)}}; f.dispatchEvent(new InputEvent('input', { bubbles: true }));
                  {{(enter ? "document.activeElement.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));" : "")}}
                  return 'ok'; })()
                """);
        });
        pipe.RegisterAsync("panel-key", r => Eval(r.Args?.GetValueOrDefault("surface") ?? "panel", $$"""
            (() => { (document.activeElement || document).dispatchEvent(new KeyboardEvent('keydown', { key: {{Js(r.Args?.GetValueOrDefault("key") ?? "Escape")}}, bubbles: true })); return 'ok'; })()
            """));
        pipe.RegisterAsync("panel-click", r => Eval(r.Args?.GetValueOrDefault("surface") ?? "panel", $$"""
            (() => { const b = document.querySelector('[data-id=' + CSS.escape({{Js(r.Args?.GetValueOrDefault("id") ?? "")}}) + ']'); if (!b) return 'missing'; b.click(); return 'ok'; })()
            """));
        pipe.RegisterAsync("panel-state", r => Eval(r.Args?.GetValueOrDefault("surface") ?? "panel", "JSON.stringify(window.__state ? window.__state() : null)"));
        pipe.Register("debug-panel", _ => new PipeReply(true, panel.Describe()));
        pipe.Register("chat-open", r => { chat.Open(r.Args?.GetValueOrDefault("fresh") == "true"); return new PipeReply(true, chat.Describe()); });
        pipe.Register("chat-close", _ => { chat.Close(); return new PipeReply(true, chat.Describe()); });
        pipe.Register("debug-chat", _ => new PipeReply(true, chat.Describe()));
        pipe.Register("chat-said", r => { chat.Said(Text(r.Args, "text64") ?? "", brain: r.Args?.GetValueOrDefault("brain")); return new PipeReply(true, chat.Describe()); });
        pipe.RegisterAsync("chat-type", r =>
        {
            var text = Text(r.Args, "text64") ?? "";
            return Eval("chat", $$"""
                (() => { const f = document.querySelector('[data-id=composer]'); if (!f) return 'no composer';
                  f.focus(); f.value = {{Js(text)}}; f.dispatchEvent(new InputEvent('input', { bubbles: true }));
                  f.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); return 'ok'; })()
                """);
        });
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class SurfaceTestJson : System.Text.Json.Serialization.JsonSerializerContext;
