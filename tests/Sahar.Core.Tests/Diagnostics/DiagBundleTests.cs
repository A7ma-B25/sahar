using System.IO.Compression;
using System.Text;
using Sahar.Core.Diagnostics;

namespace Sahar.Core.Tests.Diagnostics;

/// <summary>T7 «ابعت سجلّي» (0.15.0): what goes in the zip, and that nothing about him does — his name, the Windows
/// account, paths (they hold the user name), file names of his own models, typed or quoted text, his cities.</summary>
public class DiagBundleTests
{
    private static readonly IReadOnlyList<KeyValuePair<string, string>> Versions =
        [new("sahar", "0.15.0"), new("windows", "10.0.26100"), new("made by", "engine")];

    private static DiagSources Sources(string? engine = null, string? mind = null, string? settingsLog = null, string? json = null,
        params string[] names) => new()
    {
        EngineLog = engine, MindLog = mind, SettingsLog = settingsLog, SettingsJson = json, Versions = Versions, Private = names,
    };

    [Fact]
    public void Entries_AreTheSummaryTheThreeLogsAndTheSettings_InOrder()
    {
        var e = DiagBundle.Entries(Sources("e", "m", "s", "{}"));
        Assert.Equal(["about.txt", "engine.log", "mind.log", "settings.log", "settings.json"], e.Select(x => x.Name));
    }

    [Fact]
    public void AMissingLog_IsNamedInTheSummary_NotAnEmptyFile()
    {
        var e = DiagBundle.Entries(Sources(engine: "x", json: null));
        Assert.Equal(["about.txt", "engine.log"], e.Select(x => x.Name));
        var about = e[0].Text;
        Assert.Contains("missing: mind.log, settings.log, settings.json", about);
        Assert.Contains("sahar: 0.15.0", about);
        Assert.Contains("windows: 10.0.26100", about);
    }

    [Theory]
    [InlineData(@"2026-11-20 10:00:00.000 load failed C:\Users\Ammar\AppData\Local\Sahar\x.gguf here", "2026-11-20 10:00:00.000 load failed <path> here")]
    [InlineData(@"open \\server\share\Ammar\file.txt", "open <path>")]
    [InlineData("from C:/Users/Ammar/Desktop/song.mp3", "from <path>")]
    [InlineData("hello Ammar, AMMAR and ammar's", "hello <name>, <name> and <name>'s")]
    [InlineData("Ammarboom stays (not the same word)", "Ammarboom stays (not the same word)")]
    [InlineData("سلام سامي كيفك", "سلام <name> كيفك")]
    [InlineData("said «مرحبا يا قلبي» to him", "said «…» to him")]
    [InlineData("title \"Inbox - Gmail\" seen", "title \"…\" seen")]
    [InlineData("2026-11-20 12:52:15.155 turn: chat 2300ms", "2026-11-20 12:52:15.155 turn: chat 2300ms")]
    [InlineData("https://huggingface.co/unsloth/x stays", "https://huggingface.co/unsloth/x stays")]
    public void Scrub_TakesOutPathsNamesAndQuotedText(string line, string want) =>
        Assert.Equal(want, DiagBundle.Scrub(line, ["Ammar", "سامي"]));

    [Fact]
    public void Scrub_IgnoresEmptyAndOneLetterNames() =>
        Assert.Equal("a b c", DiagBundle.Scrub("a b c", ["", " ", "a"]));

    [Theory]
    [InlineData("runtime: ready model=Gemma 4 E4B Q4_K_M backend=Vulkan load=900ms", "runtime: ready model=Gemma 4 E4B Q4_K_M backend=Vulkan load=900ms")]
    [InlineData("runtime: ready model=my-secret-finetune Q4 backend=Cpu load=900ms", "runtime: ready model=<own model> backend=Cpu load=900ms")]
    [InlineData("turn: chat 2000ms model=lina_private", "turn: chat 2000ms model=<own model>")]
    public void Scrub_KeepsOnlyHerOwnModelNames(string line, string want) => Assert.Equal(want, DiagBundle.Scrub(line, []));

    [Fact]
    public void Logs_AreScrubbed_AndHisNameFromTheSettingsGoesToo()
    {
        var json = """{ "userName": "Lina", "size": "large" }""";
        var e = DiagBundle.Entries(Sources(engine: "hi Lina at C:\\Users\\WinUser\\a", mind: "Lina", settingsLog: "WinUser here", json: json, "WinUser"));
        string all = string.Concat(e.Select(x => x.Text));
        Assert.DoesNotContain("Lina", all);
        Assert.DoesNotContain("WinUser", all);
        Assert.Equal("hi <name> at <path>", e.Single(x => x.Name == "engine.log").Text);
    }

    [Fact]
    public void Settings_LoseHisNameCitiesPlacesAndApps_ButKeepWhatHelps()
    {
        var json = """
            {
              "version": 1, "size": "large", "userName": "Lina", "userGender": "female",
              "weatherCity": "حلب", "weatherLatitude": 36.2, "weatherLongitude": 37.16,
              "prayerCity": "Aleppo", "prayerLatitude": 36.2, "prayerLongitude": 37.16,
              "blockedApps": ["secret.exe", "bank.exe"], "menuItems": ["play", "radio"],
              "apiToken": "abc123", "mindEnabled": true, "quranLastSurah": null
            }
            """;
        var safe = DiagBundle.SafeSettings(json, []);
        foreach (var gone in new[] { "Lina", "حلب", "Aleppo", "36.2", "37.16", "secret.exe", "bank.exe", "abc123" })
            Assert.DoesNotContain(gone, safe);
        Assert.Contains("\"size\": \"large\"", safe);
        Assert.Contains("\"userName\": \"<set>\"", safe);
        Assert.Contains("\"prayerLatitude\": \"<set>\"", safe);
        Assert.Contains("\"blockedApps\": \"<2 apps>\"", safe);
        Assert.Contains("\"apiToken\": \"<hidden>\"", safe);
        Assert.Contains("\"mindEnabled\": true", safe);
        Assert.Contains("\"play\"", safe);
        Assert.Contains("\"quranLastSurah\": null", safe);
    }

    [Fact]
    public void Settings_AnUnsetPrivateValueStaysNull()
    {
        var safe = DiagBundle.SafeSettings("""{ "userName": null, "weatherCity": "" }""", []);
        Assert.Contains("\"userName\": null", safe);
        Assert.Contains("\"weatherCity\": \"\"", safe);
    }

    [Fact]
    public void Settings_NotJson_IsOnlyItsSize() =>
        Assert.Equal("not JSON (11 characters)", DiagBundle.SafeSettings("{ Lina oops", ["Lina"]));

    [Fact]
    public void Settings_OtherStringsAreScrubbedToo() =>
        Assert.DoesNotContain("WinUser", DiagBundle.SafeSettings("""{ "extra": "C:\\Users\\WinUser\\x", "who": "WinUser" }""", ["WinUser"]));

    private const string MindLog = """
        2026-11-20 10:00:00.000 mind: start model
        2026-11-20 10:00:05.000 runtime: ready model=Gemma 4 E4B Q4_K_M backend=Vulkan device=RTX 4050 tier=Gpu6 load=4200ms vram=3900MB ram=800MB
        2026-11-20 10:00:06.000 turn: chat 1000ms
        2026-11-20 10:00:07.000 turn: chat 3000ms model=Gemma 4 E4B wait=200ms first=900ms tokens=40
        2026-11-20 10:00:08.000 turn: dropped draft-empty
        2026-11-20 10:00:08.500 turn: draft 2000ms
        2026-11-20 10:00:09.000 turn: dropped timeout
        2026-11-20 10:00:09.100 turn: dropped timeout
        2026-11-20 10:00:10.000 turn: initiative 5000ms wait=400ms first=1100ms tokens=60
        2026-11-20 10:00:11.000 turn: failed (InvalidOperationException)
        2026-11-20 10:00:12.000 bench: load=4000ms first=800ms tps=21.5 dialect=5/6 tools=8/10
        """;

    [Fact]
    public void TurnSummary_CountsTimesDropsAndTheRuntime()
    {
        var s = DiagBundle.TurnSummary(MindLog);
        Assert.Contains("turns: 4 (chat 2, draft 1, initiative 1)", s);
        Assert.Contains("total: median 2500 ms, p90 5000 ms, max 5000 ms", s);
        Assert.Contains("wait: median 300 ms, p90 400 ms, max 400 ms (2 turns)", s);
        Assert.Contains("first: median 1000 ms, p90 1100 ms, max 1100 ms (2 turns)", s);
        Assert.Contains("tokens: median 50, p90 60, max 60 (2 turns)", s);
        Assert.Contains("dropped: 3 (timeout 2, draft-empty 1)", s);
        Assert.Contains("failed: 1", s);
        Assert.Contains("runtime: backend=Vulkan device=RTX 4050 tier=Gpu6 load=4200ms vram=3900MB ram=800MB", s);
        Assert.Contains("bench: load=4000ms first=800ms tps=21.5 dialect=5/6 tools=8/10", s);
        Assert.DoesNotContain("model=", s);   // the model's name is in mind.log (scrubbed); the summary is numbers only
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-11-20 10:00:00.000 mind: start model")]
    public void TurnSummary_NoTurns(string? log) => Assert.Contains("turns: none", DiagBundle.TurnSummary(log));

    [Fact]
    public void TheSummaryHoldsTheTurnSummary() =>
        Assert.Contains("turns: 4", DiagBundle.Entries(Sources(mind: MindLog))[0].Text);

    [Fact]
    public void Zip_HoldsEveryEntry_AsUtf8()
    {
        var entries = DiagBundle.Entries(Sources("engine سحر", "m", "s", """{ "size": "large" }"""));
        using var zip = new ZipArchive(new MemoryStream(DiagBundle.Zip(entries)), ZipArchiveMode.Read);
        Assert.Equal(entries.Select(e => e.Name), zip.Entries.Select(e => e.FullName));
        using var r = new StreamReader(zip.GetEntry("engine.log")!.Open(), Encoding.UTF8);
        Assert.Equal("engine سحر", r.ReadToEnd());
    }

    [Fact]
    public void FileName_IsTheTimeOnly() =>
        Assert.Equal("sahar-log-2026-11-20-1405.zip", DiagBundle.FileName(new DateTimeOffset(2026, 11, 20, 14, 5, 9, TimeSpan.FromHours(3))));

    [Fact]
    public void Write_PutsTheZipInTheFolder_WithANewNameIfOneIsThere()
    {
        var dir = Directory.CreateTempSubdirectory("sahar-diag-").FullName;
        try
        {
            var at = new DateTimeOffset(2026, 11, 20, 14, 5, 0, TimeSpan.Zero);
            var first = DiagBundle.Write(dir, Sources("e"), at);
            var second = DiagBundle.Write(dir, Sources("e"), at);
            Assert.Equal(Path.Combine(dir, "sahar-log-2026-11-20-1405.zip"), first);
            Assert.Equal(Path.Combine(dir, "sahar-log-2026-11-20-1405-2.zip"), second);
            using var zip = ZipFile.OpenRead(second);
            Assert.NotNull(zip.GetEntry("engine.log"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_TakesTheLogsFromTheFolder_EvenWhileAnotherProgramWritesThem()
    {
        var dir = Directory.CreateTempSubdirectory("sahar-diag-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "engine.log"), "engine line\n");
            File.WriteAllText(Path.Combine(dir, "mind.log"), "mind line\n");
            var settings = Path.Combine(dir, "settings.json");
            File.WriteAllText(settings, "{}");
            using var held = new FileStream(Path.Combine(dir, "engine.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            var s = DiagBundle.Read(dir, settings, Versions, ["x"]);
            Assert.Equal("engine line\n", s.EngineLog);
            Assert.Equal("mind line\n", s.MindLog);
            Assert.Null(s.SettingsLog);
            Assert.Equal("{}", s.SettingsJson);
            Assert.Equal(["x"], s.Private);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Read_KeepsOnlyTheNewestPartOfAHugeLog()
    {
        var dir = Directory.CreateTempSubdirectory("sahar-diag-").FullName;
        try
        {
            var big = new string('a', DiagBundle.MaxLogChars) + "\nnewest line\n";
            File.WriteAllText(Path.Combine(dir, "engine.log"), big);
            var s = DiagBundle.Read(dir, Path.Combine(dir, "none.json"), Versions, []);
            Assert.True(s.EngineLog!.Length <= DiagBundle.MaxLogChars);
            Assert.EndsWith("newest line\n", s.EngineLog);
            Assert.Null(s.SettingsJson);
        }
        finally { Directory.Delete(dir, true); }
    }
}
