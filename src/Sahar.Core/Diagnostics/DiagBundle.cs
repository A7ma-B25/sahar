using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sahar.Core.Diagnostics;

/// <summary>One file in the bundle.</summary>
public sealed record DiagEntry(string Name, string Text);

/// <summary>What the bundle is made of, as read from disk (null = not there). <see cref="Private"/>: words that never
/// leave — the Windows account, the machine's name; his name from the settings is added by <see cref="DiagBundle"/>.</summary>
public sealed record DiagSources
{
    public string? EngineLog { get; init; }
    public string? MindLog { get; init; }
    public string? SettingsLog { get; init; }
    public string? SettingsJson { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Versions { get; init; } = [];
    public IReadOnlyList<string> Private { get; init; } = [];
}

/// <summary>
/// T7 «ابعت سجلّي» (0.15.0): her logs in one zip he can send. about.txt (versions, what is missing, a short summary of
/// her brain's turns from mind.log), engine.log, mind.log, settings.log, settings.json. Nothing about him leaves: every
/// text is scrubbed of paths (they hold the user name), his name and the account's, quoted text (a title, a typed
/// line) and the names of models he added himself; the settings lose his name, his cities and places, his blocked
/// apps and anything secret-like. The logs never hold those by design; this is the second lock.
/// </summary>
public static partial class DiagBundle
{
    /// <summary>The newest part of each log only (engine.log and mind.log are cut at 1 MB anyway).</summary>
    public const int MaxLogChars = 512 * 1024;

    private static readonly string[] Logs = ["engine.log", "mind.log", "settings.log"];

    /// <summary>Settings that are about him: kept only as "set" or not.</summary>
    private static readonly HashSet<string> PrivateKeys = new(StringComparer.Ordinal)
    {
        "userName", "weatherCity", "weatherLatitude", "weatherLongitude", "prayerCity", "prayerLatitude", "prayerLongitude",
    };

    public static string FileName(DateTimeOffset now) => string.Create(CultureInfo.InvariantCulture, $"sahar-log-{now:yyyy-MM-dd-HHmm}.zip");

    public static IReadOnlyList<DiagEntry> Entries(DiagSources s)
    {
        var names = s.Private.Concat(NameIn(s.SettingsJson) is { } n ? [n] : Array.Empty<string>()).ToList();
        var texts = new[] { s.EngineLog, s.MindLog, s.SettingsLog };
        var missing = Logs.Where((_, i) => texts[i] is null).ToList();
        if (s.SettingsJson is null) missing.Add("settings.json");

        var about = new StringBuilder();
        about.AppendLine("Sahar diagnostics («ابعت سجلّي»): counts, kinds and timings only.");
        foreach (var (k, v) in s.Versions) about.AppendLine($"{k}: {Scrub(v, names)}");
        if (missing.Count > 0) about.AppendLine($"missing: {string.Join(", ", missing)}");
        about.AppendLine();
        about.AppendLine("her brain (mind.log):");
        about.Append(TurnSummary(s.MindLog));

        var list = new List<DiagEntry> { new("about.txt", about.ToString()) };
        for (int i = 0; i < Logs.Length; i++)
            if (texts[i] is { } t) list.Add(new(Logs[i], Scrub(t, names)));
        if (s.SettingsJson is { } json) list.Add(new("settings.json", SafeSettings(json, names)));
        return list;
    }

    // ---- scrubbing ----

    [GeneratedRegex(@"(?i)\b[a-z]:[\\/][^\s""'<>|*?«»]*|\\\\[^\s""'<>|*?«»]+")]
    private static partial Regex PathRx();

    [GeneratedRegex(@"«[^»\n]*»")]
    private static partial Regex GuillemetRx();

    [GeneratedRegex(@"""[^""\n]*""")]
    private static partial Regex QuoteRx();

    [GeneratedRegex(@"model=(.*?)(?=\s[A-Za-z_]+=|\r?$)", RegexOptions.Multiline)]
    private static partial Regex ModelRx();

    /// <summary>Her own models' names (the recommended ones and the CI model): any other is a file he added.</summary>
    private static readonly string[] KnownModels =
        [.. Mind.Models.ModelCatalog.Recommended.Select(m => m.Name), Mind.Models.ModelCatalog.CiTiny.Name];

    /// <summary>A log's text without paths, the private names, quoted text, or a model name that is not hers.</summary>
    public static string Scrub(string text, IReadOnlyList<string> names)
    {
        text = PathRx().Replace(text, "<path>");
        text = GuillemetRx().Replace(text, "«…»");
        text = QuoteRx().Replace(text, "\"…\"");
        text = ModelRx().Replace(text, m => KnownModels.Any(k => m.Groups[1].Value.StartsWith(k, StringComparison.Ordinal)) ? m.Value : "model=<own model>");
        return Names(text, names);
    }

    private static string Names(string text, IReadOnlyList<string> names)
    {
        foreach (var raw in names.Select(n => n.Trim()).Where(n => n.Length >= 2).Distinct().OrderByDescending(n => n.Length))
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(raw) + @"(?![\p{L}\p{N}_])", "<name>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return text;
    }

    private static string? NameIn(string? json)
    {
        if (json is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json, Options);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("userName", out var n)
                && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    [GeneratedRegex("(?i)token|secret|password|apikey|api_key")]
    private static partial Regex SecretKeyRx();

    /// <summary>settings.json as it helps a fix: his name, cities, places and blocked apps only as "set", anything
    /// secret-like hidden, every other string scrubbed. A file that is not JSON is only its size.</summary>
    public static string SafeSettings(string json, IReadOnlyList<string> names)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, Options); }
        catch (JsonException) { return $"not JSON ({json.Length} characters)"; }
        using (doc)
        {
            var all = names.Concat(NameIn(json) is { } n ? [n] : Array.Empty<string>()).ToList();
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                Write(w, doc.RootElement, null, all);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static void Write(Utf8JsonWriter w, JsonElement e, string? key, IReadOnlyList<string> names)
    {
        if (key is not null && e.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (SecretKeyRx().IsMatch(key)) { w.WriteStringValue("<hidden>"); return; }
            if (PrivateKeys.Contains(key))
            {
                w.WriteStringValue(e.ValueKind == JsonValueKind.String && e.GetString() is "" ? "" : "<set>");
                return;
            }
            if (key == "blockedApps" && e.ValueKind == JsonValueKind.Array) { w.WriteStringValue($"<{e.GetArrayLength()} apps>"); return; }
        }
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var p in e.EnumerateObject())
                {
                    w.WritePropertyName(p.Name);
                    Write(w, p.Value, p.Name, names);
                }
                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var x in e.EnumerateArray()) Write(w, x, null, names);
                w.WriteEndArray();
                break;
            case JsonValueKind.String:
                w.WriteStringValue(Names(PathRx().Replace(e.GetString()!, "<path>"), names));
                break;
            default:
                e.WriteTo(w);
                break;
        }
    }

    // ---- her brain's turns, from mind.log ----

    [GeneratedRegex(@"^(?:\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} )?(.*)$")]
    private static partial Regex LineRx();

    [GeneratedRegex(@"^turn: ([a-z][\w-]*) (\d+)ms(.*)$")]
    private static partial Regex TurnRx();

    [GeneratedRegex(@"\b([a-z][\w]*)=(\d+)(ms)?(?=\s|$)")]
    private static partial Regex NumberRx();

    [GeneratedRegex(@"\s*\bmodel=.*?(?=\s[A-Za-z_]+=|$)")]
    private static partial Regex ModelPartRx();

    /// <summary>A few lines about her brain: how many turns of each kind, their times (median, p90, max: total and
    /// every <c>key=Nms</c> a turn line carries), token counts, why turns were dropped, the last runtime and bench.</summary>
    public static string TurnSummary(string? mindLog)
    {
        var sources = new Dictionary<string, int>(StringComparer.Ordinal);
        var totals = new List<long>();
        var parts = new Dictionary<string, (bool Ms, List<long> Values)>(StringComparer.Ordinal);
        var dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        int failed = 0;
        string? runtime = null, bench = null;
        foreach (var raw in (mindLog ?? "").Split('\n'))
        {
            var line = LineRx().Match(raw.TrimEnd('\r')).Groups[1].Value;
            if (TurnRx().Match(line) is { Success: true } t)
            {
                sources[t.Groups[1].Value] = sources.GetValueOrDefault(t.Groups[1].Value) + 1;
                totals.Add(long.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture));
                foreach (Match m in NumberRx().Matches(t.Groups[3].Value))
                {
                    var k = m.Groups[1].Value;
                    if (!parts.TryGetValue(k, out var p)) parts[k] = p = (m.Groups[3].Success, []);
                    p.Values.Add(long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
                }
            }
            else if (line.StartsWith("turn: dropped ", StringComparison.Ordinal))
            {
                var kind = line["turn: dropped ".Length..].Split(' ', 2)[0];
                dropped[kind] = dropped.GetValueOrDefault(kind) + 1;
            }
            else if (line.StartsWith("turn: failed", StringComparison.Ordinal)) failed++;
            else if (line.StartsWith("runtime: ready ", StringComparison.Ordinal)) runtime = ModelPartRx().Replace(line["runtime: ready ".Length..], "").Trim();
            else if (line.StartsWith("bench: ", StringComparison.Ordinal)) bench = ModelPartRx().Replace(line["bench: ".Length..], "").Trim();
        }

        var sb = new StringBuilder();
        if (totals.Count == 0) sb.AppendLine("turns: none");
        else
        {
            sb.AppendLine($"turns: {totals.Count} ({string.Join(", ", Ranked(sources).Select(x => $"{x.Key} {x.Value}"))})");
            sb.AppendLine($"total: {Stats(totals, " ms")}");
            foreach (var (k, p) in parts) sb.AppendLine($"{k}: {Stats(p.Values, p.Ms ? " ms" : "")} ({p.Values.Count} turns)");
        }
        if (dropped.Count > 0) sb.AppendLine($"dropped: {dropped.Values.Sum()} ({string.Join(", ", Ranked(dropped).Select(x => $"{x.Key} {x.Value}"))})");
        if (failed > 0) sb.AppendLine($"failed: {failed}");
        if (runtime is not null) sb.AppendLine($"runtime: {runtime}");
        if (bench is not null) sb.AppendLine($"bench: {bench}");
        return sb.ToString();
    }

    private static IEnumerable<KeyValuePair<string, int>> Ranked(Dictionary<string, int> counts) =>
        counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal);

    private static string Stats(List<long> values, string unit)
    {
        var v = values.Order().ToList();
        long median = v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2;
        long p90 = v[Math.Clamp((int)Math.Ceiling(0.9 * v.Count) - 1, 0, v.Count - 1)];
        return $"median {median}{unit}, p90 {p90}{unit}, max {v[^1]}{unit}";
    }

    // ---- the zip, and the files ----

    public static byte[] Zip(IReadOnlyList<DiagEntry> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var e in entries)
            {
                using var w = new StreamWriter(zip.CreateEntry(e.Name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                w.Write(e.Text);
            }
        return ms.ToArray();
    }

    /// <summary>The logs (the newest <see cref="MaxLogChars"/> of each, read while their programs write them) and
    /// settings.json.</summary>
    public static DiagSources Read(string logFolder, string settingsPath, IReadOnlyList<KeyValuePair<string, string>> versions,
        IReadOnlyList<string> privateNames) => new()
    {
        EngineLog = Tail(Path.Combine(logFolder, Logs[0])),
        MindLog = Tail(Path.Combine(logFolder, Logs[1])),
        SettingsLog = Tail(Path.Combine(logFolder, Logs[2])),
        SettingsJson = Tail(settingsPath),
        Versions = versions,
        Private = privateNames,
    };

    private static string? Tail(string path)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var r = new StreamReader(f, Encoding.UTF8);
            var text = r.ReadToEnd();
            if (text.Length <= MaxLogChars) return text;
            text = text[^MaxLogChars..];
            int nl = text.IndexOf('\n');
            return nl >= 0 ? text[(nl + 1)..] : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Writes the zip into <paramref name="folder"/> (a new name when one is there: -2, -3…); its path.</summary>
    public static string Write(string folder, DiagSources s, DateTimeOffset now)
    {
        Directory.CreateDirectory(folder);
        var bytes = Zip(Entries(s));
        var name = FileName(now);
        for (int i = 1; ; i++)
        {
            var path = Path.Combine(folder, i == 1 ? name : $"{name[..^4]}-{i}.zip");
            try
            {
                using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                f.Write(bytes);
                return path;
            }
            catch (IOException) when (File.Exists(path) && i < 100) { }
        }
    }
}
