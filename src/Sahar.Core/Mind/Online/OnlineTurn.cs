using System.Diagnostics;
using System.Text;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;
using Sahar.Core.Mind.Turn;

namespace Sahar.Core.Mind.Online;

/// <summary>How an online turn ended: answered (by whom), or not (then the local brain answers). <see cref="Note"/>: a
/// line she says first (once a day «out for today», or once per rest «no internet»), else null. Timings and tokens are
/// numbers for the log.</summary>
public sealed record OnlineOutcome(bool Answered, string? Provider, string? LastError, string? Note, int FirstMs, int TotalMs, int OutTokens);

/// <summary>
/// One online turn (spec 0.17 §4, §6, §7): ONE plain-text streamed chat-completions request per usable provider of the
/// chain, in order — no tools, no decision JSON. 429/5xx are tried once more after Retry-After (≤ 3 s) or 800 ms; every
/// attempt counts. Review C1: a 429 after the retry is a cooldown, not the day's end — only the provider's own daily
/// numbers (Groq's headers, OpenRouter's key counter) end its day. Review I5: when every provider failed to connect,
/// online rests 3 minutes and she says so once; no first word within <see cref="FirstWordCap"/> ends the online try.
/// Review I1: only earlier ONLINE pairs go in the prompt. Review I3: words stream as cumulative <c>partial</c>s through
/// the same SayGuard as her say, and the guarded line is what history keeps. Distress never reaches here (the router
/// sends it to the local runner); the check below is a last net. Never logs text.
/// </summary>
public sealed class OnlineTurn(Func<ProviderProfile, string, ILlm> client, IPromptParts prompts, IMindStore store, OnlineState state, TurnHistory history)
{
    public static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(3), RetryWait = TimeSpan.FromMilliseconds(800);
    public const string ToldLine = "خلّصت طلبات الأونلاين لليوم 🌙 رح كمّل من عقلي المحلي.";
    public const string OfflineLine = "ما في نت هلق، عم جاوبك من عندي 🌙";
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.Now;
    public Action<string>? Log { get; init; }
    /// <summary>Partials at most this often (a 1500-token answer is not 1500 pipe lines); the last words always go in the say.</summary>
    public TimeSpan PartialEvery { get; init; } = TimeSpan.Zero;
    /// <summary>No first word from any provider within this: the online try ends and her local brain answers.</summary>
    public TimeSpan FirstWordCap { get; init; } = TimeSpan.FromSeconds(45);

    public async Task<OnlineOutcome> RunAsync(MindMsg turn, MindMsg hello, IReadOnlyList<(ProviderProfile P, string Key)> chain, ITurnIO io, CancellationToken ct)
    {
        var id = turn.Id ?? "";
        var line = turn.Text ?? "";
        bool english = hello.Lang == "en";
        var sw = Stopwatch.StartNew();
        if (prompts.Distress(line) is { } distress)
        {
            var text = distress.Help.Count > 0 ? distress.Text + "\n" + string.Join("\n", distress.Help) : distress.Text;
            io.Send(new MindMsg { T = "say", Id = id, Text = text, Mood = "distress", Final = true, Brain = "local" });
            return new(true, null, null, null, 0, (int)sw.ElapsedMilliseconds, 0);
        }
        if (chain.Count == 0 || state.Offline) return new(false, null, null, null, 0, 0, 0);
        var usable = chain.Where(c => state.Usable(c.P.Id)).ToList();
        if (usable.Count == 0) return new(false, null, OnlineErrors.Quota, AllOut(chain) && state.TellOnce() ? ToldLine : null, 0, 0, 0);
        io.Send(new MindMsg { T = "state", Id = id, Phase = "thinking" });
        var persona = prompts.Persona(hello.Caps?.Voice ?? MindVoice.Complementary, hello.Address ?? new MindAddress(Settings.Gender.Unknown, null));
        var personal = OnlinePolicy.Personal(store, line, PromptBuilder.PersonalTokens);
        var onlinePairs = history.Recent().Where(t => t.Brain == "online").ToList();
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(FirstWordCap);
        bool firstWord = false;
        string? last = null;
        bool spoke = false, allUnreachable = true;
        foreach (var (p, key) in usable)
        {
            var request = new LlmRequest(OnlinePrompt.Build(persona, onlinePairs, personal, turn.Situation, line, p.PromptCap), null, Sampling.Online);
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
                    if (first < 0) { first = (int)sw.ElapsedMilliseconds; if (!firstWord) { firstWord = true; cap.CancelAfter(Timeout.InfiniteTimeSpan); } }
                    long nowMs = sw.ElapsedMilliseconds;
                    if (lastSent >= 0 && nowMs - lastSent < PartialEvery.TotalMilliseconds) return;
                    lastSent = nowMs;
                    spoke = true;
                    io.Send(new MindMsg { T = "partial", Id = id, Text = Guard(words.ToString(), english).Text, Brain = "online" });
                }
                try
                {
                    var reply = await client(p, key).CompleteAsync(request, Partial, cap.Token);
                    if (p.Quota == QuotaKind.Headers) state.Seen(p.Id, reply.Rate);
                    var said = reply.Text.Trim();
                    if (said.Length == 0) { last = OnlineErrors.BadOutput; allUnreachable = false; Log?.Invoke($"{p.Id} {last}"); break; }
                    var guarded = Guard(said, english);
                    io.Send(new MindMsg { T = "say", Id = id, Text = guarded.Text, Mood = guarded.Mood, Final = true, Brain = "online" });
                    history.Add(line, guarded.Text ?? said, "online");
                    return new(true, p.Id, null, null, first < 0 ? (int)sw.ElapsedMilliseconds : first, (int)sw.ElapsedMilliseconds, reply.OutputTokens);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && cap.IsCancellationRequested)
                {
                    Log?.Invoke($"{p.Id} no first word in {FirstWordCap.TotalSeconds:0}s");
                    Wipe();
                    return new(false, null, OnlineErrors.Timeout, Rest(), 0, (int)sw.ElapsedMilliseconds, 0);
                }
                catch (Exception x) when (x is HttpRequestException or IOException)
                {
                    // A transport failure the client did not map (the body stream, a reset): offline, next provider.
                    last = OnlineErrors.Offline;
                    Log?.Invoke($"{p.Id} offline {x.GetType().Name} (attempt {attempt})");
                    Wipe();
                    break;
                }
                catch (MindRuntimeException e)
                {
                    last = e.Code;
                    var rate = e.Data["rate"] as RateInfo;
                    if (p.Quota == QuotaKind.Headers) state.Seen(p.Id, rate);
                    Log?.Invoke($"{p.Id} {e.Code}{(e.Detail is { } d ? " " + d : "")} (attempt {attempt})");
                    Wipe();
                    if (e.Code is not (OnlineErrors.Offline or OnlineErrors.Timeout)) allUnreachable = false;
                    if (OnlineErrors.Retryable(e.Code) && attempt == 1 && !state.Exhausted(p.Id))
                    {
                        var wait = rate?.RetryAfterS is { } s && s > 0 ? TimeSpan.FromSeconds(Math.Min(s, MaxRetryWait.TotalSeconds)) : RetryWait;
                        await Delay(wait, ct);
                        continue;
                    }
                    if (e.Code == OnlineErrors.Quota && !state.Exhausted(p.Id)) state.Cool(p.Id, rate?.RetryAfterS);
                    break;
                }
            }
        }
        string? note = allUnreachable ? Rest() : AllOut(chain) && state.TellOnce() ? ToldLine : null;
        return new(false, null, last, note, 0, (int)sw.ElapsedMilliseconds, 0);

        void Wipe()
        {
            if (!spoke) return;
            io.Send(new MindMsg { T = "partial", Id = id, Text = "", Brain = "online" });   // a half answer never stays
            spoke = false;
        }
    }

    /// <summary>Every provider of the chain is out for today by its own numbers.</summary>
    private bool AllOut(IReadOnlyList<(ProviderProfile P, string Key)> chain) => chain.All(c => state.Exhausted(c.P.Id));

    /// <summary>No provider could be reached: online rests a few minutes; she says it once per rest.</summary>
    private string? Rest()
    {
        bool already = state.Offline;
        state.GoOffline();
        return already ? null : OfflineLine;
    }

    /// <summary>Her words through the same last check as every say (labels and diagnoses become the listening line).</summary>
    private static MindMsg Guard(string text, bool english) =>
        SayGuard.Check(new MindMsg { T = "say", Text = text }, english).Msg;
}
