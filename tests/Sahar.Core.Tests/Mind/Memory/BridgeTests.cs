using System.Text;
using System.Text.Json;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Memory;

/// <summary>Plan 0.14 Task B4: the «شو فهمت عنك» page's requests (MindBridges.Memory.cs) and erase-all.</summary>
public sealed class BridgeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sahar-membridge-").FullName;
    private string Personal => Path.Combine(_dir, "mind");
    private string Models => Path.Combine(_dir, "models");
    private string Runtime => Path.Combine(_dir, "runtime");
    private string SettingsPath => Path.Combine(_dir, "settings.json");
    private readonly FakeEngine _engine = new();
    private readonly DateTimeOffset _now = new(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(3));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    private sealed class FakeEngine : IEngineLink
    {
        public List<string> Commands = [];
        public bool Running => true;
        public Task<bool> ReloadAsync(bool askHello = false) => Task.FromResult(true);
        public Task<(bool Ok, string? Reason)> TryShortcutAsync(string value) => Task.FromResult<(bool, string?)>((true, null));
        public Task<CityResult> FindCityAsync(string text) => Task.FromResult(new CityResult(null, null));
        public Task<bool> ClearClipboardAsync() => Task.FromResult(true);
        public Task<bool> EraseAllAsync() => Task.FromResult(true);
        public Task<IReadOnlyDictionary<string, string>?> CommandAsync(string name, Dictionary<string, string>? args)
        {
            Commands.Add(name);
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string> { ["ok"] = "true" });
        }
    }

    private Dictionary<string, MindBridges.Handler> Table()
    {
        var t = new Dictionary<string, MindBridges.Handler>(StringComparer.Ordinal);
        new MemoryBridge(Personal, Models, Runtime, new MindBridgeContext(SettingsPath, _engine, () => _now)).Register(t);
        return t;
    }

    private MindStore Store() => new(new SecretBox(Personal, KeyWrap.Default), () => _now,
        () => LearnConsent.From(SettingsStore.Load(SettingsPath)), () => 30);

    private async Task<(bool Ok, JsonElement Body)> Ask(string type, string json = "{}")
    {
        var req = JsonDocument.Parse(json).RootElement.Clone();
        var reply = await Table()[type](req);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            reply.Body(w);
            w.WriteEndObject();
        }
        return (reply.Ok, JsonDocument.Parse(ms.ToArray()).RootElement.Clone());
    }

    private static string J(Dictionary<string, object> o) => JsonSerializer.Serialize(o);

    [Fact]
    public void Registers_ItsTypes_AndTheSeamTableHasThem()
    {
        var t = Table();
        foreach (var type in new[] { "mind-profile", "mind-profile-confirm", "mind-profile-edit", "mind-profile-reject", "mind-profile-delete",
                     "mind-memories", "mind-memory-add", "mind-memory-delete", "mind-style", "mind-style-examples",
                     "mind-import-preview", "mind-import-save", "mind-forget", "mind-export" })
            Assert.True(t.ContainsKey(type), type);
        var seam = MindBridges.Table(new MindBridgeContext(SettingsPath, _engine, () => _now));
        Assert.Contains("mind-profile", seam.Keys);
    }

    [Fact]
    public async Task Item_IsNotTheRequestId()
    {
        // The page's request() puts its own numeric "id" on every message: the item travels as "item".
        SettingsStore.Save(SettingsPath, new SaharSettings { MindLearnChats = true });
        var a = Store().Propose("بتحب الشاي", ProfileCategory.Likes, new ItemSource(LearnSource.Chats, _now), Sensitivity.None)!;
        var (ok, body) = await Ask("mind-profile-confirm", "{\"id\":7,\"type\":\"mind-profile-confirm\",\"item\":\"" + a.Id + "\"}");
        Assert.True(ok);
        Assert.Equal(1, body.GetProperty("confirmed").GetArrayLength());
    }

    [Fact]
    public async Task Profile_ConfirmRejectRoundTrip()
    {
        SettingsStore.Save(SettingsPath, new SaharSettings { MindLearnChats = true });
        var store = Store();
        var a = store.Propose("الصبح بتحب الجواب قصير", ProfileCategory.Style, new ItemSource(LearnSource.Chats, _now), Sensitivity.None)!;
        var b = store.Propose("بتحب نكتة بعد الشغل", ProfileCategory.WhatHelps, new ItemSource(LearnSource.Chats, _now), Sensitivity.None)!;

        var (ok, body) = await Ask("mind-profile");
        Assert.True(ok);
        Assert.Equal(2, body.GetProperty("proposed").GetArrayLength());
        Assert.Equal(0, body.GetProperty("confirmed").GetArrayLength());
        var first = body.GetProperty("proposed")[0];
        Assert.Equal("low", first.GetProperty("confidence").GetString());
        Assert.Equal("chats", first.GetProperty("sources")[0].GetProperty("kind").GetString());
        Assert.False(first.GetProperty("sources")[0].TryGetProperty("snippet", out _));   // the page shows kind and date, never words
        Assert.True(body.GetProperty("consent").GetProperty("chats").GetBoolean());

        (ok, body) = await Ask("mind-profile-confirm", J(new() { ["item"] = a.Id }));
        Assert.True(ok);
        Assert.Equal("style", body.GetProperty("confirmed")[0].GetProperty("category").GetString());
        (ok, body) = await Ask("mind-profile-reject", J(new() { ["item"] = b.Id }));
        Assert.True(ok);
        Assert.Equal(0, body.GetProperty("proposed").GetArrayLength());
        Assert.Null(store.Propose("بتحب نكتة بعد الشغل", ProfileCategory.WhatHelps, new ItemSource(LearnSource.Chats, _now), Sensitivity.None));

        (ok, body) = await Ask("mind-profile-edit", J(new() { ["item"] = a.Id, ["text"] = "الصبح بتحب الجواب قصير كتير" }));
        Assert.True(ok);
        Assert.Equal("edited", body.GetProperty("confirmed")[0].GetProperty("status").GetString());
        (ok, _) = await Ask("mind-profile-edit", J(new() { ["item"] = a.Id, ["text"] = "  " }));
        Assert.False(ok);
        (ok, body) = await Ask("mind-profile-delete", J(new() { ["item"] = a.Id }));
        Assert.True(ok);
        Assert.Equal(0, body.GetProperty("confirmed").GetArrayLength());
        (ok, body) = await Ask("mind-profile-confirm", J(new() { ["item"] = "nope" }));
        Assert.False(ok);
        Assert.Equal("not-found", body.GetProperty("error").GetString());
    }

    private const string Chat = "13/11/2026, 21:41 - أحمد: يلا تمام\n13/11/2026, 21:42 - سامر: وينك\n13/11/2026, 21:43 - أحمد: هلق جاي 0944123456";

    [Fact]
    public async Task Import_PreviewThenSave_OnlyWithConsent()
    {
        SettingsStore.Save(SettingsPath, new SaharSettings { UserName = "أحمد" });
        var (ok, body) = await Ask("mind-import-preview", J(new() { ["content"] = Chat, ["name"] = "chat.txt" }));
        Assert.True(ok);
        Assert.Equal("whatsapp", body.GetProperty("format").GetString());
        Assert.Equal("أحمد", body.GetProperty("me").GetString());   // his name from settings, when a sender has it
        Assert.Equal(2, body.GetProperty("senders").GetArrayLength());
        Assert.Equal(2, body.GetProperty("kept").GetInt32());
        Assert.Equal("هلق جاي [رقم]", body.GetProperty("sample")[1].GetString());
        Assert.False(Directory.Exists(Personal));   // a preview keeps nothing

        (ok, body) = await Ask("mind-import-save", J(new() { ["content"] = Chat, ["name"] = "chat.txt", ["me"] = "أحمد" }));
        Assert.False(ok);
        Assert.Equal("no-consent", body.GetProperty("error").GetString());
        Assert.False(Directory.Exists(Personal));

        SettingsStore.Save(SettingsPath, new SaharSettings { MindLearnImports = true });
        (ok, body) = await Ask("mind-import-save", J(new() { ["content"] = Chat, ["name"] = "chat.txt", ["me"] = "سامي" }));
        Assert.False(ok);
        Assert.Equal("bad-sender", body.GetProperty("error").GetString());
        (ok, body) = await Ask("mind-import-save", J(new() { ["content"] = Chat, ["name"] = "chat.txt", ["me"] = "أحمد" }));
        Assert.True(ok);
        Assert.Equal(2, body.GetProperty("kept").GetInt32());
        Assert.Equal(2, body.GetProperty("style").GetProperty("fromImports").GetInt32());
        Assert.DoesNotContain(Store().Samples("", 10), s => s.Contains("وينك"));   // never the other person

        (ok, body) = await Ask("mind-import-preview", J(new() { ["content"] = "hello", ["name"] = "x.txt" }));
        Assert.False(ok);
        Assert.Equal("not-a-chat", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Examples_SaveOnlyWithConsent()
    {
        var (ok, body) = await Ask("mind-style-examples", J(new() { ["text"] = "يلا منحكي\nتمام" }));
        Assert.False(ok);
        Assert.Equal("no-consent", body.GetProperty("error").GetString());
        SettingsStore.Save(SettingsPath, new SaharSettings { MindLearnExamples = true });
        (ok, body) = await Ask("mind-style-examples", J(new() { ["text"] = "يلا منحكي\nتمام" }));
        Assert.True(ok);
        Assert.Equal(2, body.GetProperty("kept").GetInt32());
        Assert.Equal(2, body.GetProperty("style").GetProperty("fromExamples").GetInt32());
    }

    [Fact]
    public async Task Memories_AddListDelete_AndExport()
    {
        var (ok, body) = await Ask("mind-memory-add", J(new() { ["text"] = "أخوه اسمو سامر", ["kind"] = "fact" }));
        Assert.True(ok);
        var id = body.GetProperty("memories")[0].GetProperty("id").GetString()!;
        (ok, body) = await Ask("mind-memories", J(new() { ["search"] = "سامر" }));
        Assert.Equal(1, body.GetProperty("memories").GetArrayLength());
        (ok, body) = await Ask("mind-export");
        Assert.True(ok);
        Assert.Contains("سامر", body.GetProperty("json").GetString());
        (ok, body) = await Ask("mind-memory-delete", J(new() { ["item"] = id }));
        Assert.True(ok);
        Assert.Equal(0, body.GetProperty("memories").GetArrayLength());
        (ok, _) = await Ask("mind-memory-add", J(new() { ["text"] = "", ["kind"] = "fact" }));
        Assert.False(ok);
    }

    [Fact]
    public async Task Forget_LeavesNothing_KeepsTheModelUnlessAsked()
    {
        SettingsStore.Save(SettingsPath, new SaharSettings { MindLearnExamples = true });
        await Ask("mind-style-examples", J(new() { ["text"] = "يلا" }));
        Directory.CreateDirectory(Models);
        File.WriteAllText(Path.Combine(Models, "gemma.gguf"), "weights");
        Assert.True(File.Exists(Path.Combine(Personal, SecretBox.KeyFile)));

        var (ok, _) = await Ask("mind-forget");
        Assert.True(ok);
        Assert.False(Directory.Exists(Personal));
        Assert.True(File.Exists(Path.Combine(Models, "gemma.gguf")));   // default: the model stays (nothing about him in it)
        Assert.Contains("mind-forgot", _engine.Commands);
        Assert.True(File.Exists(SettingsPath));                          // settings stay

        Directory.CreateDirectory(Runtime);
        (ok, _) = await Ask("mind-forget", J(new() { ["model"] = true }));
        Assert.True(ok);
        Assert.False(Directory.Exists(Models));
        Assert.False(Directory.Exists(Runtime));
    }

    [Fact]
    public void EraseAll_ShredsTheMindFolderKeyFirst()
    {
        var roaming = Path.Combine(_dir, "roaming");
        var box = new SecretBox(Path.Combine(roaming, "mind"), new TestKeyWrap());
        box.Write("store", Encoding.UTF8.GetBytes("x"));
        File.WriteAllText(Path.Combine(roaming, "settings.json"), "{}");
        var order = new List<string>();
        Assert.Equal(2, DataEraser.EraseAll(roaming, order.Add));
        Assert.Equal(SecretBox.KeyFile, order[0]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(roaming));
    }
}
