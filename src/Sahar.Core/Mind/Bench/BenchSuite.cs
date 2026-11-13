using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sahar.Core.Chat;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Mind.Bench;

/// <summary>One golden row (assets/mind/bench.jsonl): a dialect prompt, or a command with its exact tool and args.</summary>
public sealed record BenchRow(string Id, string Kind, string Text, string? Tool, JsonElement Args);

/// <summary>The benchmark's rows and the tools it offers (the subset of spec §3.4 the rows need).</summary>
public static class BenchSuite
{
    private static readonly ArgSpec Minutes = new("minutes", ArgType.Int, Min: 1, Max: 600);

    public static IReadOnlyList<ToolSpec> Tools { get; } =
    [
        new("timer.start", "شغّلي مؤقت بالدقايق", [Minutes, new ArgSpec("label", ArgType.String, Required: false, MaxLength: 40)], ActionClass.A, Undoable: true),
        new("timer.cancel", "وقّفي المؤقت", [], ActionClass.A),
        new("reminder.set", "ذكّريني بشي بعد دقايق", [new ArgSpec("inMinutes", ArgType.Int, Min: 1, Max: 1440), new ArgSpec("text", ArgType.String, MaxLength: 80)], ActionClass.A, Undoable: true),
        new("note.save", "احفظي ملاحظة", [new ArgSpec("text", ArgType.String, MaxLength: 500)], ActionClass.A, Undoable: true),
        new("hush.on", "اسكتي (بالدقايق، أو لحتى قلك)", [Minutes with { Required = false }], ActionClass.A, Undoable: true),
        new("hush.off", "ارجعي احكي", [], ActionClass.A),
        new("sleep.hide", "اختفي شوي", [new ArgSpec("minutes", ArgType.Int, Min: 5, Max: 240)], ActionClass.A, Undoable: true),
        new("game.start", "نلعب", [new ArgSpec("game", ArgType.Enum, Values: ["chase", "hide", "ball", "stars"])], ActionClass.A),
        new("move.play", "حركة", [new ArgSpec("move", ArgType.Enum, Values: ["dance", "stretch", "hop"])], ActionClass.A),
        new("music.control", "تحكّم بالموسيقى", [new ArgSpec("op", ArgType.Enum, Values: ["toggle", "next", "prev", "pause"])], ActionClass.A),
        new("music.now", "شو عم يسمع", [], ActionClass.Read, ReadOnly: true),
        new("prayer.next", "الصلاة الجاية", [], ActionClass.Read, ReadOnly: true),
    ];

    public static IReadOnlyList<BenchRow> Rows { get; } = Load();

    private static List<BenchRow> Load()
    {
        using var s = typeof(BenchSuite).Assembly.GetManifestResourceStream("Sahar.bench.jsonl")!;
        using var r = new StreamReader(s);
        var rows = new List<BenchRow>();
        while (r.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;
            using var d = JsonDocument.Parse(line);
            var e = d.RootElement;
            rows.Add(new BenchRow(e.GetProperty("id").GetString()!, e.GetProperty("kind").GetString()!, e.GetProperty("text").GetString()!,
                e.TryGetProperty("tool", out var t) ? t.GetString() : null, e.TryGetProperty("args", out var a) ? a.Clone() : default));
        }
        return rows;
    }

    /// <summary>The benchmark's short system prompt: who she is, the reply shape, the tools (stable, so the cache works).</summary>
    public static string SystemPrompt(IReadOnlyList<ToolSpec> tools) =>
        "إنتِ سحر، رفيقة صغيرة على شاشة الكمبيوتر. احكي شامي بسيط، قصير، ودافي. إذا انطلب منك شي من أدواتك، حطّيه بـ actions.\n" +
        "رجّعي JSON بس: {\"say\": نص قصير, \"actions\": [{\"tool\": اسم, \"args\": {…}}]}.\nالأدوات:\n" +
        string.Join('\n', tools.Select(t => $"- {t.Name}: {t.DescriptionAr} ({string.Join(", ", t.Args.Select(a => a.Name + (a.Values is { } v ? "=" + string.Join('|', v) : "")))})"));
}

/// <summary>Spec §9.3 chat row: Syrian markers expected, MSA markers penalised, ≤ 220 characters, Arabic script
/// (English only for an English line).</summary>
public static class DialectScore
{
    private static readonly string[] Syrian = ["شو", "هلق", "منيح", "منيحة", "كتير", "بدي", "هيك", "لسا", "كمان", "تمام", "شي", "فيك", "فيني", "عم", "رح", "ليش", "وين", "هون", "مو", "مش", "يعني", "يلا", "شوي", "هاد", "هاي", "بس", "خلص", "ماشي", "إلك", "الك", "معك", "حلو", "اشتقت"];
    private static readonly string[] Msa = ["سوف", "لماذا", "ذلك", "هذا", "هذه", "الذي", "التي", "ليس", "لقد", "إنني", "ماذا", "لسؤالك", "أستطيع", "يمكنك", "يمكنني"];

    public static bool Pass(string reply, bool english = false)
    {
        var text = (reply ?? "").Trim();
        if (text.Length is 0 or > 220) return false;
        if (english) return text.Any(char.IsAsciiLetter);
        if (!text.Any(c => c is >= '؀' and <= 'ۿ')) return false;
        var words = ChatCommands.Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('،', '.', '!', '؟', '?', ',', '«', '»', ':')).ToHashSet();
        var norm = Msa.Select(ChatCommands.Normalize);
        return Syrian.Select(ChatCommands.Normalize).Any(words.Contains) && !norm.Any(words.Contains);
    }

    /// <summary>The <c>say</c> of a decision JSON, or the reply itself when it is not JSON.</summary>
    public static string SayOf(string reply)
    {
        try
        {
            using var d = JsonDocument.Parse(reply);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.TryGetProperty("say", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "";
        }
        catch (JsonException) { return reply; }
    }
}

/// <summary>Spec §9.3 command row: the first action's tool is exact and every expected arg equals (numbers by value,
/// text normalised and without end punctuation); extra optional args are fine.</summary>
public static class ToolScore
{
    public static bool Pass(BenchRow row, string reply)
    {
        try
        {
            using var d = JsonDocument.Parse(reply);
            if (d.RootElement.ValueKind != JsonValueKind.Object || !d.RootElement.TryGetProperty("actions", out var acts)
                || acts.ValueKind != JsonValueKind.Array || acts.GetArrayLength() == 0) return false;
            var first = acts[0];
            if (!first.TryGetProperty("tool", out var tool) || tool.GetString() != row.Tool) return false;
            if (row.Args.ValueKind != JsonValueKind.Object) return true;
            first.TryGetProperty("args", out var args);
            foreach (var want in row.Args.EnumerateObject())
            {
                if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(want.Name, out var got) || !Same(want.Value, got)) return false;
            }
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool Same(JsonElement want, JsonElement got)
    {
        if (want.ValueKind == JsonValueKind.Number)
        {
            var g = got.ValueKind == JsonValueKind.Number ? got.GetDouble()
                : got.ValueKind == JsonValueKind.String && double.TryParse(Sahar.Core.Text.Digits.ToLatin(got.GetString()!), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
            return g == want.GetDouble();
        }
        return got.ValueKind == JsonValueKind.String && ChatCommands.Normalize(got.GetString()!) == ChatCommands.Normalize(want.GetString()!);
    }
}

/// <summary>Runs «جرّبيني»: loads the model (timed), one fixed reply for speed, then (golden) every row. Returns
/// numbers only; no prompt or reply leaves this method.</summary>
public static class BenchRunner
{
    public const string SpeedPrompt = "احكيلي بجملتين شو بتحبي تعملي بيوم هادي.";

    public static async Task<BenchResult> RunAsync(ILlm llm, string model, string schema, Func<Task<RuntimeReady>> warm, Func<(int VramMB, int RamMB)> memory,
        bool golden, IProgress<int>? rowsDone, Func<DateTimeOffset> now, CancellationToken ct)
    {
        var load = Stopwatch.StartNew();
        var ready = await warm();
        int loadMs = ready.LoadMs > 0 ? ready.LoadMs : (int)load.ElapsedMilliseconds;
        var system = new ChatMessage("system", BenchSuite.SystemPrompt(BenchSuite.Tools));
        var speed = await llm.CompleteAsync(new LlmRequest([system, new ChatMessage("user", SpeedPrompt)], null, Sampling.Chat with { MaxTokens = 96 }), null, ct);
        double decodeSec = Math.Max(0.001, (speed.Total - speed.FirstToken).TotalSeconds);
        double tps = speed.OutputTokens > 1 ? (speed.OutputTokens - 1) / decodeSec : 0;
        double prefill = speed.FirstToken > TimeSpan.Zero ? speed.PromptTokens / speed.FirstToken.TotalSeconds : 0;
        var (vram, ram) = memory();
        int dPass = 0, dTotal = 0, tPass = 0, tTotal = 0, done = 0;
        if (golden)
        {
            foreach (var row in BenchSuite.Rows)
            {
                ct.ThrowIfCancellationRequested();
                var reply = await llm.CompleteAsync(new LlmRequest([system, new ChatMessage("user", row.Text)], schema, Sampling.Chat with { Temperature = 0.2 }), null, ct);
                if (row.Kind == "tool") { tTotal++; if (ToolScore.Pass(row, reply.Text)) tPass++; }
                else { dTotal++; if (DialectScore.Pass(DialectScore.SayOf(reply.Text), english: row.Text.Any(char.IsAsciiLetter))) dPass++; }
                rowsDone?.Report(++done);
            }
        }
        return new BenchResult(model, loadMs, (int)speed.FirstToken.TotalMilliseconds, Math.Round(tps, 1), Math.Round(prefill, 1), vram, ram,
            dPass, dTotal, tPass, tTotal, now());
    }
}
