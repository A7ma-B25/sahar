using System.Diagnostics;
using System.Text;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind.Online;

/// <summary>How an online turn ended: answered (by whom), or not (then the local brain answers); <see cref="Tell"/>:
/// every online provider is out for today and he was not told yet today. Timings and tokens are numbers for the log.</summary>
public sealed record OnlineOutcome(bool Answered, string? Provider, string? LastError, bool Tell, int FirstMs, int TotalMs, int OutTokens);

/// <summary>
/// One online turn (spec 0.17 §4, §6, §7): a distress cue gets the fixed reply and nothing leaves; otherwise ONE
/// plain-text streamed chat-completions request per provider of the chain, in order — no tools, no decision JSON.
/// 429/5xx are tried once more after Retry-After (≤ 3 s) or 800 ms; every attempt counts; a 429 after the retry puts
/// the provider out for today. Words stream as cumulative <c>partial</c>s (a half answer is wiped before the next
/// provider speaks); the one <c>say</c> carries <c>Brain = "online"</c>. Nothing is said when every provider failed:
/// the host runs the local turn. Never logs text (<see cref="Log"/> gets kinds and codes only).
/// </summary>
public sealed class OnlineTurn(Func<ProviderProfile, string, ILlm> client, IPromptParts prompts, IMindStore store, OnlineState state, TurnHistory history)
{
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(3), RetryWait = TimeSpan.FromMilliseconds(800);
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    public Action<string>? Log { get; init; }
    /// <summary>Partials at most this often (a 1500-token answer is not 1500 pipe lines); the last words always go in the say.</summary>
    public TimeSpan PartialEvery { get; init; } = TimeSpan.Zero;

    public async Task<OnlineOutcome> RunAsync(MindMsg turn, MindMsg hello, IReadOnlyList<(ProviderProfile P, string Key)> chain, ITurnIO io, CancellationToken ct)
    {
        var id = turn.Id ?? "";
        var line = turn.Text ?? "";
        var sw = Stopwatch.StartNew();
        if (prompts.Distress(line) is { } distress)
        {
            var text = distress.Help.Count > 0 ? distress.Text + "\n" + string.Join("\n", distress.Help) : distress.Text;
            io.Send(new MindMsg { T = "say", Id = id, Text = text, Mood = "distress", Final = true, Brain = "local" });
            return new(true, null, null, false, 0, (int)sw.ElapsedMilliseconds, 0);
        }
        var usable = chain.Where(c => !state.Exhausted(c.P.Id)).ToList();
        if (usable.Count == 0) return new(false, null, chain.Count > 0 ? OnlineErrors.Quota : null, chain.Count > 0 && state.TellOnce(), 0, 0, 0);
        io.Send(new MindMsg { T = "state", Id = id, Phase = "thinking" });
        var persona = prompts.Persona(hello.Caps?.Voice ?? MindVoice.Complementary, hello.Address ?? new MindAddress(Settings.Gender.Unknown, null));
        var messages = OnlinePrompt.Build(persona, history.Recent(), OnlinePolicy.Personal(store, line, PromptBuilder.PersonalTokens), turn.Situation, line);
        var request = new LlmRequest(messages, null, Sampling.Online);
        string? last = null;
        bool spoke = false;
        foreach (var (p, key) in usable)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                state.Count(p.Id);
                var words = new StringBuilder();
                long lastSent = -1;
                int first = -1;
                void Partial(string chunk)
                {
                    words.Append(chunk);
                    if (first < 0) first = (int)sw.ElapsedMilliseconds;
                    long nowMs = sw.ElapsedMilliseconds;
                    if (lastSent >= 0 && nowMs - lastSent < PartialEvery.TotalMilliseconds) return;
                    lastSent = nowMs;
                    spoke = true;
                    io.Send(new MindMsg { T = "partial", Id = id, Text = words.ToString(), Brain = "online" });
                }
                try
                {
                    var reply = await client(p, key).CompleteAsync(request, Partial, ct);
                    state.Seen(p.Id, reply.Rate);
                    var said = reply.Text.Trim();
                    if (said.Length == 0) { last = OnlineErrors.BadOutput; Log?.Invoke($"{p.Id} {last}"); break; }
                    io.Send(new MindMsg { T = "say", Id = id, Text = said, Final = true, Brain = "online" });
                    history.Add(line, said, "online");
                    return new(true, p.Id, null, false, first < 0 ? (int)sw.ElapsedMilliseconds : first, (int)sw.ElapsedMilliseconds, reply.OutputTokens);
                }
                catch (Exception x) when (x is HttpRequestException or IOException)
                {
                    // Review: a transport failure the client did not map (the body stream, a reset): offline, next provider.
                    last = OnlineErrors.Offline;
                    Log?.Invoke($"{p.Id} offline {x.GetType().Name} (attempt {attempt})");
                    if (spoke) { io.Send(new MindMsg { T = "partial", Id = id, Text = "", Brain = "online" }); spoke = false; }
                    break;
                }
                catch (MindRuntimeException e)
                {
                    last = e.Code;
                    var rate = e.Data["rate"] as RateInfo;
                    state.Seen(p.Id, rate);
                    Log?.Invoke($"{p.Id} {e.Code}{(e.Detail is { } d ? " " + d : "")} (attempt {attempt})");
                    if (spoke) { io.Send(new MindMsg { T = "partial", Id = id, Text = "", Brain = "online" }); spoke = false; }
                    if (OnlineErrors.Retryable(e.Code) && attempt == 1)
                    {
                        var wait = rate?.RetryAfterS is { } s && s > 0 ? TimeSpan.FromSeconds(Math.Min(s, MaxRetryWait.TotalSeconds)) : RetryWait;
                        await Delay(wait, ct);
                        continue;
                    }
                    if (e.Code == OnlineErrors.Quota) state.Exhaust(p.Id);
                    break;
                }
            }
        }
        bool allOut = chain.All(c => state.Exhausted(c.P.Id));
        return new(false, null, last, allOut && state.TellOnce(), 0, (int)sw.ElapsedMilliseconds, 0);
    }

    /// <summary>Her line before the local answer when every online provider is out for today (once a day).</summary>
    public const string ToldLine = "خلّصت طلبات الأونلاين لليوم 🌙 رح كمّل من عقلي المحلي.";
}
