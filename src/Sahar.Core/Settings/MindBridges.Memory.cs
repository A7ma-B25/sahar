using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Import;
using Sahar.Core.Mind.Memory;

namespace Sahar.Core.Settings;

// STREAM B owns this file (feat/0.14-memory): the «عنك» page's requests.
public static partial class MindBridges
{
    static partial void AddMemory(Dictionary<string, Handler> t, MindBridgeContext ctx) =>
        new MemoryBridge(MindPaths.Personal, MindPaths.Models, MindPaths.Runtime, ctx).Register(t);
}

/// <summary>
/// «شو فهمت عنك» (plan 0.14 Task B4; spec §7.2, §7.5, §7.8, §8): the profile (confirm / edit / reject / delete),
/// memories, his style (imports he previews before anything is kept, examples he writes), «انسي كل شي» and the
/// export. The settings app is the store's second writer (same mutex as the Mind). Each learning source needs its own
/// consent in settings.json (Ruling 3): without it nothing is kept and the page asks. The page sees kinds, counts and
/// dates of sources, never snippets. Nothing here logs text.
/// </summary>
public sealed class MemoryBridge(string personal, string models, string runtime, MindBridgeContext ctx, Func<TimeSpan, Task>? waitMindGone = null)
{
    public void Register(Dictionary<string, MindBridges.Handler> t)
    {
        t.Add("mind-profile", _ => Ok(Profile));
        t.Add("mind-profile-confirm", r => Item(r, (s, id, _) => s.Confirm(id)));
        t.Add("mind-profile-edit", r => Item(r, (s, id, text) => text is { Length: > 0 } && !string.IsNullOrWhiteSpace(text) && s.Edit(id, text), needsText: true));
        t.Add("mind-profile-reject", r => Item(r, (s, id, _) => s.Reject(id)));
        t.Add("mind-profile-delete", r => Item(r, (s, id, _) => s.Delete(id)));
        t.Add("mind-memories", r => Ok(w => Memories(w, Store(), Str(r, "search"))));
        t.Add("mind-memory-add", MemoryAdd);
        t.Add("mind-memory-delete", r =>
        {
            var store = Store();
            return Task.FromResult(store.DeleteMemory(Str(r, "item") ?? "")
                ? new MindBridgeReply(true, w => Memories(w, store, null)) : MindBridgeReply.Fail("not-found"));
        });
        t.Add("mind-style", _ => Ok(w => Style(w, Store().Style())));
        t.Add("mind-style-examples", Examples);
        t.Add("mind-import-preview", Preview);
        t.Add("mind-import-save", Save);
        t.Add("mind-forget", Forget);
        t.Add("mind-export", _ => Ok(w => w.WriteString("json", Store().Export())));
    }

    // ---- the store, as the page's process sees it ----

    private SaharSettings Settings()
    {
        try { return SettingsStore.Readable(ctx.SettingsPath) ? SettingsStore.Load(ctx.SettingsPath) : new SaharSettings(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new SaharSettings(); }
    }

    private MindStore Store() => new(new SecretBox(personal, KeyWrap.Default), ctx.Now, () => LearnConsent.From(Settings()), () => Settings().MindEpisodeDays);

    // ---- handlers ----

    private Task<MindBridgeReply> Item(JsonElement r, Func<MindStore, string, string?, bool> change, bool needsText = false)
    {
        var id = Str(r, "item");   // never "id": that is the page's request id
        var text = Str(r, "text");
        if (string.IsNullOrEmpty(id)) return Fail("bad-request");
        if (needsText && string.IsNullOrWhiteSpace(text)) return Fail("bad-text");
        var store = Store();
        if (store.Status == StoreStatus.Locked) return Fail("store-locked");
        return change(store, id, text) ? Ok(w => Profile(w, store)) : Fail("not-found");
    }

    private void Profile(Utf8JsonWriter w) => Profile(w, Store());

    private void Profile(Utf8JsonWriter w, MindStore store)
    {
        var all = store.Profile();
        w.WriteStartArray("proposed");
        foreach (var i in all.Where(i => i.Status == ItemStatus.Proposed).OrderByDescending(i => i.Confidence).ThenByDescending(i => i.CreatedAt)) WriteItem(w, i);
        w.WriteEndArray();
        w.WriteStartArray("confirmed");
        foreach (var i in all.Where(i => i.ShapesHer).OrderBy(i => i.Category).ThenByDescending(i => i.ConfirmedAt)) WriteItem(w, i);
        w.WriteEndArray();
        Style(w, store.Style());
        w.WriteString("store", Camel(store.Status.ToString()));
        var c = LearnConsent.From(Settings());
        w.WriteStartObject("consent");
        w.WriteBoolean("chats", c.Chats);
        w.WriteBoolean("imports", c.Imports);
        w.WriteBoolean("examples", c.Examples);
        w.WriteBoolean("music", c.Music);
        w.WriteEndObject();
    }

    private static void WriteItem(Utf8JsonWriter w, ProfileItem i)
    {
        w.WriteStartObject();
        w.WriteString("id", i.Id);
        w.WriteString("text", i.Text);
        w.WriteString("category", Camel(i.Category.ToString()));
        w.WriteString("confidence", Camel(i.Confidence.ToString()));
        w.WriteString("status", Camel(i.Status.ToString()));
        w.WriteString("sensitive", Camel(i.Sensitive.ToString()));
        w.WriteString("createdAt", i.CreatedAt);
        if (i.ConfirmedAt is { } c) w.WriteString("confirmedAt", c);
        w.WriteStartArray("sources");
        foreach (var s in i.Sources)
        {
            w.WriteStartObject();
            w.WriteString("kind", Camel(s.Kind.ToString()));
            w.WriteString("at", s.At);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    private static void Style(Utf8JsonWriter w, StyleCard s)
    {
        w.WriteStartObject("style");
        w.WriteString("summary", s.SummaryAr);
        w.WriteNumber("samples", s.Samples);
        w.WriteNumber("fromImports", s.FromImports);
        w.WriteNumber("fromExamples", s.FromExamples);
        w.WriteEndObject();
    }

    private static void Memories(Utf8JsonWriter w, MindStore store, string? search)
    {
        w.WriteStartArray("memories");
        foreach (var m in store.Memories(search))
        {
            w.WriteStartObject();
            w.WriteString("id", m.Id);
            w.WriteString("text", m.Text);
            w.WriteString("kind", Camel(m.Kind.ToString()));
            w.WriteString("at", m.At);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    private Task<MindBridgeReply> MemoryAdd(JsonElement r)
    {
        var text = Str(r, "text");
        if (string.IsNullOrWhiteSpace(text)) return Fail("bad-text");
        var kind = Enum.TryParse<MemoryKind>(Str(r, "kind"), ignoreCase: true, out var k) ? k : MemoryKind.Fact;
        var store = Store();
        store.AddMemory(text, kind, Mind.Safety.Sensitive.Check(text));   // he wrote it himself: his yes is the add
        return Ok(w => Memories(w, store, null));
    }

    private Task<MindBridgeReply> Examples(JsonElement r)
    {
        var lines = Mind.Import.Examples.Lines(Str(r, "text"));
        if (lines.Count == 0) return Fail("bad-text");
        if (!Settings().MindLearnExamples) return Fail("no-consent");
        var store = Store();
        int kept = store.AddSamples(LearnSource.Examples, lines);
        return Ok(w => { w.WriteNumber("kept", kept); Style(w, store.Style()); });
    }

    /// <summary>The page shows who wrote what (to pick «مين إنت؟») and a few of his scrubbed lines; nothing is kept.</summary>
    private Task<MindBridgeReply> Preview(JsonElement r)
    {
        var content = Str(r, "content") ?? "";
        if (content.Length > ChatImport.MaxChars) return Fail("too-big");
        if (ChatImport.Parse(content, Str(r, "name")) is not { } chat) return Fail("not-a-chat");
        var senders = chat.Senders;
        // "me" is a sender key: the export's id when it has ids, else the name. A name from settings picks him only
        // when exactly one sender has it.
        static string Key(SenderCount s) => s.Id ?? s.Name;
        var byName = Settings().UserName is { Length: > 0 } name ? senders.Where(s => string.Equals(s.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList() : [];
        var me = Str(r, "me") is { Length: > 0 } asked && senders.Any(s => Key(s) == asked) ? asked
            : chat.Me is { } known && senders.Any(s => Key(s) == known) ? known
            : byName.Count == 1 ? Key(byName[0])
            : null;
        var his = me is null ? [] : chat.His(me);
        return Ok(w =>
        {
            w.WriteString("format", chat.Format);
            w.WriteStartArray("senders");
            foreach (var s in senders.Take(50))
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                w.WriteString("key", Key(s));
                w.WriteNumber("count", s.Count);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (me is null) w.WriteNull("me");
            else w.WriteString("me", me);
            w.WriteNumber("kept", his.Count);
            w.WriteStartArray("sample");
            foreach (var l in his.Take(5)) w.WriteStringValue(l.Length > 160 ? l[..160] + "…" : l);
            w.WriteEndArray();
        });
    }

    private Task<MindBridgeReply> Save(JsonElement r)
    {
        var content = Str(r, "content") ?? "";
        var me = Str(r, "me");
        if (content.Length > ChatImport.MaxChars) return Fail("too-big");
        if (!Settings().MindLearnImports) return Fail("no-consent");
        if (ChatImport.Parse(content, Str(r, "name")) is not { } chat) return Fail("not-a-chat");
        if (string.IsNullOrEmpty(me) || chat.Senders.All(s => (s.Id ?? s.Name) != me)) return Fail("bad-sender");
        var store = Store();
        int kept = store.AddSamples(LearnSource.Imports, chat.His(me));
        return Ok(w => { w.WriteNumber("kept", kept); Style(w, store.Style()); });
    }

    /// <summary>«انسي كل شي» (spec §7.8): the key first, then the mind folder; settings and notes stay, and the
    /// downloaded model too unless he asks (it holds nothing about him). She is told, so she stops the Mind and says so.</summary>
    private async Task<MindBridgeReply> Forget(JsonElement r)
    {
        // The Mind first (review B2): told to quit, then waited for, so no turn in flight writes after the shred (and a
        // store that saw the old key never makes a new one anyway).
        await ctx.Engine.CommandAsync("mind-stop", null);
        await (waitMindGone ?? WaitMindGone)(TimeSpan.FromSeconds(5));
        Store().ForgetAll();
        bool model = r.ValueKind == JsonValueKind.Object && r.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.True;
        if (model)
            foreach (var folder in new[] { models, runtime })
                try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var said = await ctx.Engine.CommandAsync("mind-forgot", null);
        bool gone = !Directory.Exists(personal) || !Directory.EnumerateFileSystemEntries(personal).Any();
        return new MindBridgeReply(gone, w =>
        {
            w.WriteBoolean("told", said is not null);
            w.WriteBoolean("model", model);
            if (!gone) w.WriteString("error", "in-use");
        });
    }

    private static async Task WaitMindGone(TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("Sahar.Mind"))
        {
            try
            {
                var left = deadline - DateTime.UtcNow;
                if (left > TimeSpan.Zero) await p.WaitForExitAsync(new CancellationTokenSource(left).Token);
            }
            catch (Exception) { /* gone already, or the time is up: the shred goes on */ }
            finally { p.Dispose(); }
        }
    }

    // ---- small parts ----

    private static Task<MindBridgeReply> Ok(Action<Utf8JsonWriter> body) => Task.FromResult(new MindBridgeReply(true, body));
    private static Task<MindBridgeReply> Fail(string error) => Task.FromResult(MindBridgeReply.Fail(error));

    private static string? Str(JsonElement r, string name) =>
        r.ValueKind == JsonValueKind.Object && r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Camel(string pascal) => pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
}
