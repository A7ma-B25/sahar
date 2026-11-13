using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Sahar.Core.Chat;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Tools;

namespace Sahar.Core.Mind.Turn;

/// <summary>
/// One turn in structured steps (spec §3.5): at most 3 steps of at most 3 actions, each step a grammar-constrained
/// decision (<see cref="IToolRegistry.DecisionSchema"/>). Every action is re-validated against the announced tools
/// before it is sent as a <c>call</c>; the engine decides and runs it and answers with a <c>tool-result</c> (or an
/// <c>answer</c> to its Ask). Her final line is the model's words only when they do not contradict what really
/// happened; otherwise the engine's own confirmations (Core templates). A distress cue gets the fixed reply with no
/// model and no tools. The chat budget (20 s) counts model time only: waiting for his «إي»/«لا» does not use it.
/// Sends state, partial, call, propose, say and error; never logs text.
/// </summary>
public sealed class TurnRunner(ILlm llm, IPromptParts prompts, IMindStore store) : ITurnRunner
{
    public const int MaxSteps = 3, MaxActions = 3;
    public TimeSpan ChatBudget { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan ReflectBudget { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>How long a call may wait for the engine (an Ask waits up to 2 min for him).</summary>
    public TimeSpan ReplyWait { get; init; } = TimeSpan.FromSeconds(150);
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;

    private readonly List<(string User, string Her)> _history = [];

    internal sealed record Outcome(string Tool, bool Ok, string Summary, string? Data, bool Read);

    public string Prefix(MindMsg hello) =>
        PromptBuilder.Prefix(prompts.Persona(hello.Caps?.Voice ?? MindVoice.Complementary, hello.Address ?? new MindAddress(Settings.Gender.Unknown, null)),
            hello.Tools ?? []);

    public async Task RunAsync(MindMsg turn, MindMsg hello, ITurnIO io, CancellationToken ct)
    {
        var id = turn.Id ?? "";
        var source = turn.Source ?? "chat";
        bool chat = source is "chat" or "voice";
        var line = turn.Text ?? "";
        io.Send(new MindMsg { T = "state", Id = id, Phase = "thinking" });
        try
        {
            if (chat && prompts.Distress(line) is { } distress)
            {
                var text = distress.Help.Count > 0 ? distress.Text + "\n" + string.Join("\n", distress.Help) : distress.Text;
                io.Send(new MindMsg { T = "say", Id = id, Text = text, Mood = "distress", Final = true });
                return;
            }
            var registry = MindParts.ToolRegistry(hello.Tools ?? []);
            var schema = registry.DecisionSchema();
            var messages = PromptBuilder.Build(Prefix(hello), _history,
                chat || source == "initiative" ? prompts.PersonalBlock(line, PromptBuilder.PersonalTokens) : "", turn.Situation, source, line);
            var budget = source == "reflect" ? ReflectBudget : ChatBudget;
            var clock = new Stopwatch();
            var outcomes = new List<Outcome>();
            int calls = 0;
            bool invalid = false;
            string say = "";
            string? mood = null;

            for (int step = 1; step <= MaxSteps; step++)
            {
                var left = budget - clock.Elapsed;
                if (left <= TimeSpan.Zero) { TimedOut(io, id); return; }
                var stream = new SayStream(t => io.Send(new MindMsg { T = "partial", Id = id, Text = t }));
                LlmReply reply;
                using (var limit = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    limit.CancelAfter(left);
                    clock.Start();
                    try { reply = await llm.CompleteAsync(new LlmRequest(messages, schema, Sampling.Chat), stream.Add, limit.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { TimedOut(io, id); return; }
                    catch (MindRuntimeException e)
                    {
                        io.Send(new MindMsg { T = "error", Id = id, Error = e.Code });
                        return;
                    }
                    finally { clock.Stop(); }
                }
                ct.ThrowIfCancellationRequested();
                if (Decision.Parse(reply.Text) is not { } d) { invalid = true; break; }
                say = d.Say;
                mood = d.Mood;
                if (chat) Remember(io, id, d.Remember);

                bool reads = false;
                foreach (var action in d.Actions.Take(MaxActions))
                {
                    var call = new ToolCall(action.Tool, action.Args, action.Why);
                    if (!registry.Validate(call).Ok) { invalid = true; continue; }
                    var spec = registry.Announced.First(t => t.Name == call.Tool);
                    if (ToolCatalog.MindLocal.Contains(call.Tool))
                    {
                        outcomes.Add(Local(io, id, call, chat));
                        continue;
                    }
                    int n = ++calls;
                    io.Send(new MindMsg { T = "call", Id = id, Call = n, Tool = call.Tool, Data = action.Args.GetRawText(), Why = action.Why });
                    var o = await Reply(io, id, n, call, spec.ReadOnly, ct);
                    outcomes.Add(o);
                    reads |= o.Read && o.Ok;
                }
                if (d.Next != "need-results" || step == MaxSteps || invalid) break;
                var results = string.Join('\n', outcomes.Where(o => o.Read).Select(o => $"{o.Tool}: {(o.Ok ? o.Data ?? o.Summary : "ما زبط")}"));
                messages.Add(new ChatMessage("assistant", reply.Text));
                messages.Add(new ChatMessage("user", PromptBuilder.Data("نتايج الأدوات", results.Length > 0 ? results : "ما في")));
                if (!reads && results.Length == 0) break;
            }

            var final = Final(say, outcomes, invalid);
            if (chat) Keep(line, final);
            io.Send(new MindMsg { T = "say", Id = id, Text = final, Mood = mood, Final = true });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { /* Esc or a closed card: nothing more */ }
        finally
        {
            io.Send(new MindMsg { T = "state", Id = id, Phase = "idle" });
        }
    }

    private static void TimedOut(ITurnIO io, string id)
    {
        io.Send(new MindMsg { T = "error", Id = id, Error = "timeout" });
        io.Send(new MindMsg { T = "say", Id = id, Text = ToolTemplates.Timeout, Final = true });
    }

    private async Task<Outcome> Reply(ITurnIO io, string id, int n, ToolCall call, bool read, CancellationToken ct)
    {
        MindMsg r;
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(ReplyWait);
        try { r = await io.ReplyAsync(id, n, wait.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(call.Tool, false, ToolTemplates.NoAnswer, null, read); }
        if (r.T == "answer") return new(call.Tool, false, ToolTemplates.AskedNo, null, read);   // a yes comes as the tool-result of the run
        bool ok = r.Ok == true;
        var summary = r.Text is { Length: > 0 } t ? t
            : ok ? ToolTemplates.Done(call, r.Data)
            : r.Error switch { "asked-no" => ToolTemplates.AskedNo, "unavailable" => ToolTemplates.Unavailable, _ => ToolTemplates.Failed };
        return new(call.Tool, ok, summary, r.Data, read);
    }

    /// <summary>memory.remember / goal.add: proposals into the store, for his review on «عنك»; never the engine's.</summary>
    private Outcome Local(ITurnIO io, string id, ToolCall call, bool chat)
    {
        string S(string k) => call.Args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        if (!chat) return new(call.Tool, false, ToolTemplates.Failed, null, false);
        var goal = call.Tool == "goal.add";
        var text = S("text");
        var item = store.Propose(text, goal ? ProfileCategory.Goals : Category(S("kind")), new ItemSource(LearnSource.Chats, Now()),
            prompts.Sensitive(text, Sensitivity.None));
        if (item is null) return new(call.Tool, false, ToolTemplates.Failed, null, false);
        io.Send(new MindMsg { T = "propose", Id = id, Kind = goal ? "goal" : "profile", Item = item });
        return new(call.Tool, true, ToolTemplates.Done(call), null, false);
    }

    private void Remember(ITurnIO io, string id, IReadOnlyList<Decision.Memory> items)
    {
        foreach (var m in items.Take(3))
        {
            var item = store.Propose(m.Text, Category(m.Kind), new ItemSource(LearnSource.Chats, Now()), prompts.Sensitive(m.Text, Tag(m.Sensitive)));
            if (item is not null) io.Send(new MindMsg { T = "propose", Id = id, Kind = "profile", Item = item });
        }
    }

    private static ProfileCategory Category(string kind) => kind switch
    {
        "preference" => ProfileCategory.Likes,
        "goal" => ProfileCategory.Goals,
        "event" => ProfileCategory.Routines,
        _ => ProfileCategory.Values,
    };

    private static Sensitivity Tag(string s) => s switch
    {
        "health" => Sensitivity.Health,
        "religion" => Sensitivity.Religion,
        "politics" => Sensitivity.Politics,
        "sexuality" => Sensitivity.Sexuality,
        "money" => Sensitivity.Money,
        "family" => Sensitivity.Family,
        "other" => Sensitivity.Other,
        _ => Sensitivity.None,
    };

    private void Keep(string line, string her)
    {
        if (line.Length == 0) return;
        _history.Add((line, her));
        if (_history.Count > PromptBuilder.HistoryTurns) _history.RemoveRange(0, _history.Count - PromptBuilder.HistoryTurns);
    }

    /// <summary>Her truthful last line: the model's words when nothing contradicts them; else what really happened.</summary>
    internal static string Final(string say, IReadOnlyList<Outcome> outcomes, bool invalid)
    {
        var acted = outcomes.Where(o => !o.Read).ToList();
        if (invalid && acted.Count == 0) return ToolTemplates.BadOutput;
        if (acted.Count == 0) return Claims(say) ? ToolTemplates.NothingDone : say;
        if (acted.All(o => o.Ok) && !invalid) return say.Length > 0 ? say : string.Join("، ", acted.Select(o => o.Summary));
        return string.Join("، ", acted.Select(o => o.Summary));
    }

    /// <summary>Does a line claim she did something ("I set…", «شغّلت…»)? Then it needs an outcome behind it.</summary>
    public static bool Claims(string say)
    {
        if (say.Length == 0) return false;
        var n = " " + ChatCommands.Normalize(say) + " ";
        foreach (var w in ClaimWords)
            if (n.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly string[] ClaimWords =
    [
        "شغلت", "حفظت", "وقفت", "سكرت", "فتحت", "سجلت", "غيرت", "كبرت", "صغرت", "طفيت", "حطيت", "ضبطت", "لغيت", "مسحت",
        "بعتت", "رتبت", "فضيت", "ذكرتك", "عملتلك", "سكتت", "سكت ", "i set ", "i started", "i saved", "i stopped", "i turned",
        "i changed", "i sent", "i deleted", "i opened", "i've ", "done!",
    ];

    // ---- the decision JSON ----

    internal sealed record Decision(IReadOnlyList<Decision.Act> Actions, string Next, string Say, string? Mood, IReadOnlyList<Decision.Memory> Remember)
    {
        internal sealed record Act(string Tool, JsonElement Args, string? Why);
        internal sealed record Memory(string Text, string Kind, string Sensitive);

        public static Decision? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("actions", out var acts) || acts.ValueKind != JsonValueKind.Array) return null;
                var actions = new List<Act>();
                foreach (var a in acts.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("tool", out var t) || t.ValueKind != JsonValueKind.String) return null;
                    var args = a.TryGetProperty("args", out var g) ? g.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                    var why = a.TryGetProperty("why", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
                    actions.Add(new(t.GetString()!, args, why));
                }
                string Str(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                var mem = new List<Memory>();
                if (r.TryGetProperty("remember", out var rem) && rem.ValueKind == JsonValueKind.Array)
                    foreach (var m in rem.EnumerateArray())
                        if (m.ValueKind == JsonValueKind.Object && m.TryGetProperty("text", out var mt) && mt.ValueKind == JsonValueKind.String && mt.GetString()!.Trim().Length > 0)
                            mem.Add(new(mt.GetString()!, m.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : "fact",
                                m.TryGetProperty("sensitive", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : "other"));
                var say = Str("say");
                if (say.Length > 220) say = say[..220];
                var mood = Str("mood");
                return new(actions, Str("next") == "need-results" ? "need-results" : "done", say, ToolRegistry.Moods.Contains(mood) ? mood : null, mem);
            }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>Streams her words while the model writes them — only once the actions are known to be empty (the
    /// schema puts them first), only whole sentences, and never a line that claims an action.</summary>
    private sealed class SayStream(Action<string> send)
    {
        private readonly StringBuilder _raw = new();
        private string _sent = "";
        private bool _stopped;

        public void Add(string chunk)
        {
            if (_stopped) return;
            _raw.Append(chunk);
            var raw = _raw.ToString();
            int sayAt = raw.IndexOf("\"say\"", StringComparison.Ordinal);
            if (sayAt < 0) return;
            int nextAt = raw.IndexOf("\"next\"", StringComparison.Ordinal);
            int actsAt = raw.IndexOf("\"actions\"", StringComparison.Ordinal);
            if (actsAt < 0 || nextAt < actsAt) { _stopped = true; return; }
            var acts = raw[(actsAt + 9)..nextAt].Trim().TrimStart(':').TrimEnd(',').Trim();
            if (acts != "[]") { _stopped = true; return; }
            int q = raw.IndexOf('"', raw.IndexOf(':', sayAt) + 1);
            if (q < 0) return;
            var (text, closed) = Decode(raw, q + 1);
            int cut = closed ? text.Length : LastBoundary(text);
            if (cut <= _sent.Length) return;
            var part = text[..cut].TrimEnd();
            if (Claims(part)) { _stopped = true; return; }
            if (part.Length <= _sent.Length) return;
            _sent = part;
            send(part);
            if (closed) _stopped = true;
        }

        private static int LastBoundary(string s)
        {
            for (int i = s.Length - 1; i >= 0; i--)
                if (s[i] is '.' or '!' or '?' or '؟' or '،' or '…' or '\n' or '!') return i + 1;
            return 0;
        }

        /// <summary>The JSON string from <paramref name="at"/>: decoded so far, and whether its closing quote came.</summary>
        private static (string, bool) Decode(string raw, int at)
        {
            var b = new StringBuilder();
            for (int i = at; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '"') return (b.ToString(), true);
                if (c != '\\') { b.Append(c); continue; }
                if (i + 1 >= raw.Length) break;
                char e = raw[++i];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append(' '); break;
                    case 'u':
                        if (i + 4 >= raw.Length) return (b.ToString(), false);
                        b.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: b.Append(e); break;
                }
            }
            return (b.ToString(), false);
        }
    }
}
