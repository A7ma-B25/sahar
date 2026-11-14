using System.Text.Json;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Tests.Mind.Tools;

/// <summary>C1: the tool list (spec §3.4), re-validation in Core, the class of a call, and the model's decision schema (§3.5).</summary>
public sealed class ToolRegistryTests
{
    private static readonly IToolRegistry All = MindParts.ToolRegistry(ToolCatalog.All);

    private static ToolCall Call(string tool, string args) => new(tool, JsonDocument.Parse(args).RootElement.Clone());

    public static TheoryData<string, string, bool> Rows => new()
    {
        { "timer.start", """{"minutes":15}""", true },
        { "timer.start", """{"minutes":15,"label":"ركّز"}""", true },
        { "timer.start", """{"minutes":0}""", false },
        { "timer.start", """{"minutes":601}""", false },
        { "timer.start", """{"minutes":"15"}""", false },
        { "timer.start", """{"minutes":2.5}""", false },
        { "timer.start", """{}""", false },
        { "timer.start", """{"minutes":5,"extra":1}""", false },
        { "timer.start", """{"minutes":5,"label":"0123456789012345678901234567890123456789x"}""", false },
        { "timer.start", """[5]""", false },
        { "timer.cancel", """{}""", true },
        { "timer.cancel", """{"minutes":3}""", false },
        { "reminder.set", """{"inMinutes":30,"text":"اشرب مي"}""", true },
        { "reminder.set", """{"at":"21:30","text":"اتصل بأمك"}""", true },
        { "reminder.set", """{"at":"24:00","text":"x"}""", false },
        { "reminder.set", """{"at":"9:5","text":"x"}""", false },
        { "reminder.set", """{"inMinutes":30,"at":"21:30","text":"x"}""", false },
        { "reminder.set", """{"text":"x"}""", false },
        { "reminder.set", """{"inMinutes":1441,"text":"x"}""", false },
        { "reminder.set", """{"inMinutes":5,"text":"  "}""", false },
        { "note.save", """{"text":"حليب وخبز"}""", true },
        { "note.save", """{"text":""}""", false },
        { "note.open", """{}""", true },
        { "hush.on", """{}""", true },
        { "hush.on", """{"minutes":60}""", true },
        { "hush.on", """{"minutes":null}""", true },
        { "hush.on", """{"minutes":0}""", false },
        { "hush.off", """{}""", true },
        { "sleep.hide", """{"minutes":60}""", true },
        { "sleep.hide", """{}""", true },
        { "sleep.hide", """{"minutes":4}""", false },
        { "sleep.hide", """{"minutes":241}""", false },
        { "game.start", """{"game":"stars"}""", true },
        { "game.start", """{"game":"chess"}""", false },
        { "games.open", """{}""", true },
        { "move.play", """{"move":"dance"}""", true },
        { "move.play", """{"move":"fly"}""", false },
        { "prayer.next", """{}""", true },
        { "prayer.open", """{}""", true },
        { "music.now", """{}""", true },
        { "music.control", """{"op":"pause"}""", true },
        { "music.control", """{"op":"louder"}""", false },
        { "quran.open", """{}""", true },
        { "quran.stop", """{}""", true },
        { "settings.set", """{"key":"size","value":"large"}""", true },
        { "settings.set", """{"key":"weatherEnabled","value":true}""", true },
        { "settings.set", """{"key":"prayerReminderMinutes","value":15}""", true },
        { "settings.set", """{"key":"size"}""", false },
        { "settings.set", """{"key":"size","value":{"a":1}}""", false },
        { "settings.set", """{"key":"size","value":[1]}""", false },
        { "memory.remember", """{"text":"بيحب القهوة","kind":"preference"}""", true },
        { "memory.remember", """{"text":"x","kind":"secret"}""", false },
        { "goal.add", """{"text":"خلّص المشروع"}""", true },
        { "goal.add", """{"text":"خلّص المشروع","checkEvery":"day","until":"2026-11-20"}""", true },
        { "goal.add", """{"text":"x","until":"الجمعة"}""", false },
        { "goal.add", """{"text":"x","checkEvery":"hour"}""", false },
        { "weather.now", """{}""", true },
        { "claude.status", """{}""", true },
        { "shelf.open", """{}""", true },
        { "shelf.tidy", """{}""", true },
        { "shelf.clear", """{}""", true },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EveryTool_GoodAndBadArgs(string tool, string args, bool ok)
    {
        var check = All.Validate(Call(tool, args));
        Assert.True(ok == check.Ok, $"{tool} {args}: {check.Error} {check.Field}");
        if (!ok) Assert.Equal("invalid", check.Error);
    }

    [Fact]
    public void EveryCatalogTool_HasARow()
    {
        var covered = Rows.Select(r => r.Data.Item1).ToHashSet();
        Assert.All(ToolCatalog.All, t => Assert.Contains(t.Name, covered));
    }

    [Theory]
    [InlineData("size", ActionClass.A)]
    [InlineData("activity", ActionClass.A)]
    [InlineData("innerStars", ActionClass.A)]
    [InlineData("autoMood", ActionClass.A)]
    [InlineData("playsAlone", ActionClass.A)]
    [InlineData("appearance", ActionClass.A)]
    [InlineData("menuHeader", ActionClass.A)]
    [InlineData("menuPlace", ActionClass.A)]
    [InlineData("awarenessEnabled", ActionClass.B)]
    [InlineData("weatherEnabled", ActionClass.B)]
    [InlineData("language", ActionClass.B)]
    [InlineData("menuShortcut", ActionClass.B)]
    [InlineData("menuOpen", ActionClass.B)]
    [InlineData("menuItems", ActionClass.B)]
    [InlineData("prayerMethod", ActionClass.B)]
    [InlineData("prayerHanafiAsr", ActionClass.B)]
    [InlineData("prayerReminderOn", ActionClass.B)]
    [InlineData("prayerReminderMinutes", ActionClass.B)]
    [InlineData("quranReciter", ActionClass.B)]
    [InlineData("userName", ActionClass.B)]
    [InlineData("userGender", ActionClass.B)]
    public void SettingsSet_ClassByKey(string key, ActionClass cls)
    {
        var call = Call("settings.set", $$"""{"key":"{{key}}","value":"x"}""");
        Assert.Equal(cls, All.ClassOf(call));
    }

    [Theory]
    [InlineData("updateCheckEnabled")]
    [InlineData("blockedApps")]
    [InlineData("mindSelfActions")]
    [InlineData("mindInitiative")]
    [InlineData("mindLearnChats")]
    [InlineData("mindEnabled")]
    [InlineData("helloDone")]
    [InlineData("actClaude")]
    [InlineData("nope")]
    public void SettingsSet_NotSettableKeysRefused(string key)
    {
        var call = Call("settings.set", $$"""{"key":"{{key}}","value":true}""");
        var check = All.Validate(call);
        Assert.False(check.Ok);
        Assert.Equal("key", check.Field);
        Assert.Equal(ActionClass.C, All.ClassOf(call));
    }

    [Fact]
    public void SettingsSet_EveryAllowedKeyIsARealSetting()
    {
        foreach (var key in ToolCatalog.SettingsA.Concat(ToolCatalog.SettingsB))
            Assert.Contains(key, Sahar.Core.Settings.SettingsPatch.Keys);
        Assert.Empty(ToolCatalog.SettingsA.Intersect(ToolCatalog.SettingsB));
    }

    [Fact]
    public void NotAnnounced_Refused()
    {
        var few = MindParts.ToolRegistry([ToolCatalog.Get("timer.start")!]);
        Assert.True(few.Validate(Call("timer.start", """{"minutes":5}""")).Ok);
        Assert.Equal("not-announced", few.Validate(Call("note.save", """{"text":"x"}""")).Error);
        Assert.Equal("unknown-tool", few.Validate(Call("files.delete", """{}""")).Error);
        Assert.Equal(ActionClass.C, few.ClassOf(Call("note.save", """{"text":"x"}""")));
        Assert.Equal(ActionClass.C, few.ClassOf(Call("files.delete", """{}""")));
    }

    [Fact]
    public void NoClaudePermissionTool()
    {
        string[] words = ["allow", "approve", "permission", "permit", "deny", "accept", "answer", "send", "delete", "file", "install", "pay"];
        foreach (var t in ToolCatalog.All)
            Assert.DoesNotContain(words, w => t.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["claude.status"], ToolCatalog.All.Where(t => t.Name.StartsWith("claude", StringComparison.Ordinal)).Select(t => t.Name));
        Assert.Equal(ActionClass.Read, ToolCatalog.Get("claude.status")!.Class);
    }

    [Fact]
    public void Schema_EnumsOnlyAnnouncedTools()
    {
        var some = MindParts.ToolRegistry([ToolCatalog.Get("timer.start")!, ToolCatalog.Get("prayer.next")!, ToolCatalog.Get("settings.set")!]);
        using var doc = JsonDocument.Parse(some.DecisionSchema());
        var root = doc.RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        // actions first, say after them: she streams her words only once she knows whether she acts (truthful confirmations).
        Assert.Equal(["actions", "next", "say", "mood", "remember", "risk"], root.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        var actions = root.GetProperty("properties").GetProperty("actions");
        Assert.Equal(3, actions.GetProperty("maxItems").GetInt32());
        var names = actions.GetProperty("items").GetProperty("anyOf").EnumerateArray()
            .Select(a => a.GetProperty("properties").GetProperty("tool").GetProperty("const").GetString()).ToList();
        Assert.Equal(["timer.start", "prayer.next", "settings.set"], names);
        var timer = actions.GetProperty("items").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("args");
        Assert.Equal(600, timer.GetProperty("properties").GetProperty("minutes").GetProperty("maximum").GetInt32());
        Assert.Equal(["minutes"], timer.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        var keys = actions.GetProperty("items").GetProperty("anyOf")[2].GetProperty("properties").GetProperty("args")
            .GetProperty("properties").GetProperty("key").GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("size", keys);
        Assert.Contains("weatherEnabled", keys);
        Assert.DoesNotContain("mindSelfActions", keys);
        Assert.Equal(220, root.GetProperty("properties").GetProperty("say").GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Schema_NoToolsStillParses()
    {
        using var doc = JsonDocument.Parse(MindParts.ToolRegistry([]).DecisionSchema());
        Assert.Equal(0, doc.RootElement.GetProperty("properties").GetProperty("actions").GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void Announced_IsWhatTheEngineGave()
    {
        var list = new[] { ToolCatalog.Get("hush.on")!, ToolCatalog.Get("note.save")! with { Undoable = false } };
        var r = MindParts.ToolRegistry(list);
        Assert.Equal(list, r.Announced);
        Assert.True(r.Validate(Call("note.save", """{"text":"x"}""")).Ok);
    }

    [Fact]
    public void Schema_HasTheOptionalRiskTag()
    {
        using var doc = JsonDocument.Parse(All.DecisionSchema());
        var props = doc.RootElement.GetProperty("properties");
        Assert.Equal(Sahar.Core.Mind.SayGuard.RiskValues, props.GetProperty(Sahar.Core.Mind.SayGuard.RiskField).GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(Sahar.Core.Mind.SayGuard.RiskField, doc.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
    }
}
