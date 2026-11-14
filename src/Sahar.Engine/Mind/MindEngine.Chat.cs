using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Sahar.Core.Chat;
using Sahar.Core.Menu;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Autonomy;
using Sahar.Core.Mind.Initiative;
using Sahar.Core.Mind.Log;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Protocol;
using Sahar.Core.Settings;
using Sahar.Core.Text;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Menu;
using Sahar.Engine.Mind.Tools;
using Windows.System;

namespace Sahar.Engine.Mind;

// STREAM C owns this file (feat/0.14-autonomy): chat routing, the tools mapped to her abilities, the Ask drill, the
// action log, initiative, the header's words.
internal sealed partial class MindEngine
{
    /// <summary>The 0.13 sources as tools (the lead's adapter in App.cs); null = weather/Claude/shelf tools not announced.</summary>
    public IActivityTools? ActivityTools { get; set; }
    /// <summary>The once-a-day download question when he chats and no model is present (default: stream A's).</summary>
    public Action? OfferDownload { get; set; }
    /// <summary>«اكتبلي رد بأسلوبي…» (stream B's <c>DraftAsk.Parse(line) is not null</c>, set at integration): such a line
    /// is a turn with source "draft" (Ruling 6); B shows the draft with «بصوتك» and a copy button. Drafting never sends.</summary>
    public Func<string, bool>? IsDraftAsk { get; set; }

    private EngineTools _tools = null!;
    private IToolRegistry _registry = null!;
    private readonly IAutonomyPolicy _policy = MindParts.Policy();
    private readonly IInitiativeGate _gate = MindParts.Gate();
    private IActionLog _log = null!;
    private AskDrill _ask = null!;
    private DispatcherQueueTimer _initiative = null!;

    /// <summary>The turns she is in: id → who started it and his line (clipped; kept only in the encrypted log).</summary>
    private readonly Dictionary<string, (ActSource Source, string? Line, string Kind)> _turns = [];
    private string? _chatTurn;   // the chat turn the header follows
    private int _turnSeq;
    private DateOnly _offeredOn;
    // initiative
    private DateTimeOffset? _lastLine, _awaitingAnswerSince, _pausedUntil;
    private int _dismissed, _linesToday;
    private DateOnly _linesDay;
    private string _lastGate = "none";
    private readonly List<DateTimeOffset> _selfAsks = [];
    private DateTimeOffset _cameraSince = DateTimeOffset.MinValue;
    // the header fact, once a day
    private DateOnly _todayFactDay;
    private bool _todayFactShown;
    // test mode: a fake Mind (the UI stage plays its lines)
    private bool _fake;
    private readonly ConcurrentQueue<string> _sent = new();

    partial void AttachChat()
    {
        _tools = new EngineTools(this, ActivityTools);
        _registry = MindParts.ToolRegistry(_tools.Announced());
        _log = MindParts.ActionLog(MindParts.SecretBox(MindPaths.Personal), name => _tools.Get(name), () => DateTimeOffset.Now);
        _ask = new AskDrill(Ui, Menu, ListBubble, () => Motion.Body);
        OfferDownload ??= OfferDownloadQuestion;
        ChatHandler = OnChat;
        Received += OnMind;
        Menu.ChatTyped += () => { if (Ready()) WarmUp(); };
        Menu.OpenChanged += open => Safe(() => CardOpenChanged(open));
        Menu.TodayFact = TodayFact;
        CameraMic.Changed += on => { if (on) _cameraSince = DateTimeOffset.Now; };
        Director.NeedsYou += () => Ui.TryEnqueue(() => Safe(ClaudeAsking));
        _initiative = Ui.CreateTimer();
        _initiative.Interval = TimeSpan.FromMinutes(1);
        _initiative.Tick += (_, _) => Safe(() => Initiative(force: false));
        _initiative.Start();
        RegisterLogCommands();
        if (TestMode) RegisterTestCommands();
        Log.Write($"mind: {_registry.Announced.Count} tools");
    }

    /// <summary>The first line to the Mind (spec §3.2): only the tools this engine has now; none for a model that cannot
    /// call tools (then commands stay with the chat parser and the Mind only talks).</summary>
    public MindMsg Hello(bool toolCalls = true)
    {
        var s = Settings();
        return new MindMsg
        {
            T = "hello", Version = "1", Lang = s.Language == UiLanguage.English ? "en" : "ar", Address = new MindAddress(s.UserGender, s.UserName),
            Caps = new MindCaps(s.MindInitiative != InitiativeLevel.Off, s.MindLearnChats, s.MindVoice),
            Tools = toolCalls ? _registry.Announced : [],
        };
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"mind: error {e.GetType().Name}"); }
    }

    private bool MindReady() => _fake || Ready();

    private bool SendMind(MindMsg m)
    {
        if (_fake)
        {
            _sent.Enqueue(Brief(m));
            while (_sent.Count > 40) _sent.TryDequeue(out _);
            return true;
        }
        return Send(m);
    }

    // ---- chat (spec §3.3: the fast path first) ----

    /// <summary>A line the fast path did not run goes to her brain: free words, greetings, a timer out of range. Clean
    /// commands stay instant and are logged as she runs them.</summary>
    private bool OnChat(ChatCommand c, string text)
    {
        if (_awaitingAnswerSince is not null) { _awaitingAnswerSince = null; _dismissed = 0; }   // he answered her
        bool draft = MindReady() && IsDraftAsk?.Invoke(text) == true;
        bool free = draft || c.Kind is ChatKind.Unknown or ChatKind.Greeting || (c.Kind == ChatKind.Timer && c.OutOfRange);
        if (!free)
        {
            bool hushBefore = Dnd.On;
            Ui.TryEnqueue(() => Safe(() => LogFastPath(c, text, hushBefore)));
            return false;
        }
        if (!MindReady())
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (c.Kind == ChatKind.Unknown && _offeredOn != today && !Settings().MindEnabled)
            {
                _offeredOn = today;
                Ui.TryEnqueue(() => Safe(() => OfferDownload?.Invoke()));   // after today's reply
            }
            return false;
        }
        var id = "c" + (++_turnSeq).ToString(CultureInfo.InvariantCulture);
        if (_chatTurn is { } running) SendMind(new MindMsg { T = "cancel", Id = running });
        var source = draft ? "draft" : "chat";
        if (!SendMind(new MindMsg { T = "turn", Id = id, Source = source, Text = text, Situation = Moment() })) return false;
        _turns[id] = (ActSource.User, Clip(text, 60), source);
        _chatTurn = id;
        Menu.MindThinking(true);
        Log.Write($"mind: turn {source}");
        return true;
    }

    /// <summary>The fast path ran a command: «شو عملت اليوم» gets its line (with undo while it applies).</summary>
    private void LogFastPath(ChatCommand c, string text, bool hushBefore)
    {
        var why = Clip(text, 60);
        switch (c.Kind)
        {
            case ChatKind.Timer when Timer.Running && c.Minutes is { } m:
                Append("timer.start", Call("timer.start", $$"""{"minutes":{{m}}}"""), ActSource.User, why, ActionOutcome.Done,
                    TimerTokenNow());
                break;
            case ChatKind.Note when c.Text is not null && Notes.Store.Latest(1) is [var n]:
                Append("note.save", Call("note.save", """{"text":"x"}"""), ActSource.User, why, ActionOutcome.Done,
                    "note:" + n.At.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + n.Text);
                break;
            case ChatKind.Hush when Dnd.On && !hushBefore:
                Append("hush.on", c.HushFor is { } h ? Call("hush.on", $$"""{"minutes":{{(int)h.TotalMinutes}}}""") : Call("hush.on", "{}"),
                    ActSource.User, why, ActionOutcome.Done, "hush");
                break;
            case ChatKind.Sleep:
                Append("sleep.hide", Call("sleep.hide", """{"minutes":60}"""), ActSource.User, why, ActionOutcome.Done, "hidden");
                break;
            case ChatKind.Game when c.Game is { } g:
                Append("game.start", Call("game.start", $$"""{"game":"{{g switch { Sahar.Core.Games.GameKind.Chase => "chase", Sahar.Core.Games.GameKind.HideSeek => "hide", Sahar.Core.Games.GameKind.Ball => "ball", _ => "stars" }}}"}"""),
                    ActSource.User, why, ActionOutcome.Done, null);
                break;
            case ChatKind.Move:
                Append("move.play", Call("move.play", $$"""{"move":"{{(c.Move == Sahar.Core.Motion.MotionKind.Dance ? "dance" : c.Move == Sahar.Core.Motion.MotionKind.Stretch ? "stretch" : "hop")}}"}"""),
                    ActSource.User, why, ActionOutcome.Done, null);
                break;
        }
    }

    private string? TimerTokenNow() =>
        Timer.Remaining is { } left ? "timer:" + (DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : null;

    private static ToolCall Call(string tool, string args) => new(tool, JsonDocument.Parse(args).RootElement.Clone());

    private ActionEntry Append(string tool, ToolCall call, ActSource source, string? why, ActionOutcome outcome, string? undo, string? summary = null) =>
        _log.Append(tool, summary ?? ToolTemplates.Done(call), source, why, outcome, undo);

    private static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s.Trim() : s[..max].Trim() + "…";

    private void CardOpenChanged(bool open)
    {
        if (open) return;
        // Esc or a closed card stops her thinking (spec §3.2 cancel); a fact shown today stays shown today.
        if (_chatTurn is { } id)
        {
            SendMind(new MindMsg { T = "cancel", Id = id });
            Log.Write("mind: cancel (card closed)");
            _chatTurn = null;
        }
        if (_todayFactShown) { _todayFactDay = DateOnly.FromDateTime(DateTime.Now); _todayFactShown = false; }
    }

    private HeaderFact? TodayFact()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_todayFactDay == today && !_todayFactShown) return null;
        int n = ActionLogView.DoneToday(_log, DateTimeOffset.Now);
        if (n == 0) return null;
        _todayFactShown = true;
        return new HeaderFact("today", ActionLogView.DoneLine(n), "settings");
    }

    // ---- what the Mind says ----

    private void OnMind(MindMsg m)
    {
        switch (m.T)
        {
            case "state" when m.Id is not null && m.Id == _chatTurn:
                if (m.Phase == "thinking") Menu.MindThinking(true);
                else if (m.Phase == "idle") Menu.MindThinking(false);   // review X1: «عم فكّر…» always ends
                break;
            case "partial" when m.Id is not null && m.Id == _chatTurn && m.Text is { } partial:
                Menu.MindStream(Words(partial));
                break;
            case "call":
                HandleCall(m);
                break;
            case "say":
                Said(m);
                break;
            case "error" when m.Id is not null:
                Log.Write($"mind: turn error {m.Error}");
                if (m.Id == _chatTurn)
                {
                    Menu.MindThinking(false);
                    if (m.Error is "model-missing" or "load-failed" or "oom" or "busy") Menu.SetReply(Address.For(Address.You, ChatReply.Learning));
                    if (m.Error != "timeout") _chatTurn = null;   // a timeout's line follows
                }
                break;
            case "draft" when m.Id is not null && m.Id == _chatTurn:   // stream B shows it («بصوتك» + copy)
                _turns.Remove(m.Id);
                _chatTurn = null;
                Menu.MindThinking(false);
                break;
            case "propose":
                Log.Write($"mind: proposal ({m.Kind})");
                break;
        }
    }

    /// <summary>Her words as he reads them: Arabic-Indic digits in Arabic (her Digits rule); the gender agreement is
    /// applied where they are drawn.</summary>
    private string Words(string text) => Settings().Language == UiLanguage.English ? text : Digits.ToArabicIndic(text);

    private void Said(MindMsg m)
    {
        var id = m.Id ?? "";
        _turns.TryGetValue(id, out var turn);
        var text = Words(m.Text ?? "");
        if (m.Mood == "distress") _pausedUntil = DateTimeOffset.Now.AddHours(24);   // spec §7.4: no initiative for a day
        if (turn.Kind == "initiative")
        {
            _turns.Remove(id);
            if (text.Trim().Length == 0) { Log.Write("mind: initiative, nothing to say"); return; }
            _lastLine = DateTimeOffset.Now;
            _awaitingAnswerSince = _lastLine;
            _linesToday++;
            Log.Write($"mind: initiative line ({text.Length} chars)");
            Speech.Say(text, comment: true);
            return;
        }
        if (id != _chatTurn) { _turns.Remove(id); return; }   // a cancelled turn's late words
        _chatTurn = null;
        _turns.Remove(id);
        Menu.MindThinking(false);
        if (text.Length == 0) return;
        Log.Write($"mind: said ({text.Length} chars)");
        if (Menu.IsOpen && MenuCard.FitsHeader(text)) Menu.SetReply(text);
        else Speech.Say(Address.For(Address.You, text));
    }

    // ---- calls: the engine validates and decides (spec §3.7) ----

    private void HandleCall(MindMsg m)
    {
        var id = m.Id ?? "";
        int n = m.Call ?? 0;
        if (!_turns.TryGetValue(id, out var turn) || turn.Kind == "draft") { Reply(id, n, false, "refused", ToolTemplates.Failed); return; }   // not ours, or a draft: words only
        ToolCall call;
        try { call = new ToolCall(m.Tool ?? "", JsonDocument.Parse(m.Data ?? "{}").RootElement.Clone(), m.Why); }
        catch (JsonException) { Reply(id, n, false, "invalid", ToolTemplates.BadOutput); return; }
        var check = _registry.Validate(call);
        if (!check.Ok)
        {
            Log.Write($"mind: call refused ({check.Error})");
            Reply(id, n, false, check.Error == ToolRegistry.Invalid ? "invalid" : "unavailable", ToolTemplates.BadOutput);
            return;
        }
        var spec = _registry.Announced.First(t => t.Name == call.Tool);
        var cls = _registry.ClassOf(call);
        var source = turn.Source;
        var d = _policy.Decide(spec, call, cls, source, Context(turn.Kind));
        var why = source == ActSource.User ? turn.Line : Clip(call.Why, 80);
        Log.Write($"mind: call {call.Tool} {d.Verdict.ToString().ToLowerInvariant()}{(d.Reason is { } r ? $" ({r})" : "")}");
        switch (d.Verdict)
        {
            case Verdict.Act:
                Run(id, n, call, source, why, ActionOutcome.Done);
                break;
            case Verdict.Ask:
                if (source == ActSource.Self) _selfAsks.Add(DateTimeOffset.Now);
                var question = ToolTemplates.Question(call);
                _ask.Ask(question, yes => Safe(() =>
                {
                    if (yes) Run(id, n, call, source, why, ActionOutcome.AskedYes);
                    else
                    {
                        Append(call.Tool, call, source, why, ActionOutcome.AskedNo, null, "سألتك: " + question);
                        Reply(id, n, false, "asked-no", ToolTemplates.AskedNo);
                    }
                }));
                break;
            default:
                var line = d.PointTo ?? ToolTemplates.Failed;
                Append(call.Tool, call, source, why, ActionOutcome.Refused, null, (source == ActSource.User ? "ما عملتها: " : "ما عملتها لحالي: ") + ToolTemplates.Done(call));
                Reply(id, n, false, "refused", line);
                break;
        }
    }

    private void Run(string id, int n, ToolCall call, ActSource source, string? why, ActionOutcome done)
    {
        if (_tools.Get(call.Tool) is not { } tool) { Reply(id, n, false, "unavailable", ToolTemplates.Unavailable); return; }
        tool.RunAsync(call).ContinueWith(t => Ui.TryEnqueue(() => Safe(() =>
        {
            var o = t.IsCompletedSuccessfully ? t.Result : ToolOutcome.Fail("failed", ToolTemplates.Failed);
            if (!tool.Spec.ReadOnly)
                Append(call.Tool, call, source, why, o.Ok ? done : ActionOutcome.Failed, o.UndoToken,
                    done == ActionOutcome.AskedYes ? o.Summary : o.Ok ? o.Summary : "ما زبطت: " + ToolTemplates.Done(call));
            Reply(id, n, o.Ok, o.Error, o.Summary, o.Data);
        })), TaskScheduler.Default);
    }

    private void Reply(string id, int n, bool ok, string? error, string text, string? data = null) =>
        SendMind(new MindMsg { T = "tool-result", Id = id, Call = n, Ok = ok, Error = ok ? null : error ?? "failed", Text = text, Data = data });

    /// <summary>The state the policy reads (spec §3.7 ctx), from her senses.</summary>
    private AutonomyContext Context(string kind)
    {
        var now = DateTimeOffset.Now;
        _selfAsks.RemoveAll(t => now - t > TimeSpan.FromHours(1));
        var (_, onBattery, percent) = Battery();
        return new AutonomyContext(
            Away: Idle.Away, Locked: false, Fullscreen: Director.Where != Sahar.Core.Brain.FullscreenWhere.None, CameraMic: CameraMic.InUse,
            AdhanQuiet: Director.Adhan, Typing: SinceKeyboard() < TimeSpan.FromSeconds(InitiativeGate.TypingSeconds), MenuOpen: Menu.IsOpen,
            OnBattery: onBattery, BatteryPercent: percent, SelfActionsOn: Settings().MindSelfActions,
            ForConfirmedGoalOrRoutine: kind == "routine", CallStarting: CameraMic.InUse && now - _cameraSince < TimeSpan.FromSeconds(30),
            SelfAsksThisHour: _selfAsks.Count);
    }

    /// <summary>The battery (no battery = a desktop on mains: never "on battery").</summary>
    private static unsafe (bool Has, bool OnBattery, int Percent) Battery()
    {
        SYSTEM_POWER_STATUS s;
        if (Native.GetSystemPowerStatus(&s) == 0 || (s.BatteryFlag & 128) != 0 || s.BatteryLifePercent == 255) return (false, false, 100);
        return (true, s.ACLineStatus != 1, s.BatteryLifePercent);
    }

    // ---- the moment (spec §3.9) and initiative (§3.8) ----

    private Situation Moment()
    {
        var (has, onBattery, percent) = Battery();
        var claude = ActivityTools?.ClaudeStatus();
        var inputs = new SituationInputs(DateTimeOffset.Now)
        {
            NextPrayer = Prayer.Next(), TimerLeft = Timer.Remaining, Hushed = Dnd.On, MusicPlaying = Media.MusicPlaying,
            ClaudeWorking = claude?.Working ?? 0, ClaudeWaiting = claude?.Waiting ?? 0, Weather = ActivityTools?.WeatherNow(),
            BatteryPercent = percent, Charging = !onBattery, HasBattery = has, Mood = Menu.Mood().ToString().ToLowerInvariant(),
            HerLinesToday = LinesToday(),
        };
        return SituationRules.From(inputs, Settings());
    }

    private int LinesToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_linesDay != today) { _linesDay = today; _linesToday = 0; }
        return _linesToday;
    }

    private InitiativeInputs GateInputs()
    {
        var now = DateTimeOffset.Now;
        if (_awaitingAnswerSince is { } since && now - since > TimeSpan.FromMinutes(10)) { _awaitingAnswerSince = null; _dismissed++; }   // no answer: a dismissal
        var s = Settings();
        var (_, onBattery, percent) = Battery();
        var key = Director.CurrentKey;
        double typing = SinceKeyboard() is var k && k == TimeSpan.MaxValue ? double.MaxValue : k.TotalSeconds;
        return new InitiativeInputs(now, s.MindInitiative, Idle.Away, false, typing,
            Busy: Timer.Running || _chatTurn is not null || _ask.Where is not null,
            Fullscreen: Director.Where != Sahar.Core.Brain.FullscreenWhere.None, CameraMic: CameraMic.InUse, Dnd: Dnd.On, QuranPlaying: Quran.Playing,
            AdhanQuiet: Director.Adhan, QuietHours: QuietNow(s, now), MenuOpen: Menu.IsOpen, Hidden: Director.Hidden, GameOrShowRunning: Games.Running,
            ClaudeWaiting: key == "claude-permission", TimerDoneShowing: key == "timer-done",
            MinutesToPrayer: Prayer.Next() is { } p ? p.Left.TotalMinutes : null, MinutesSincePrayer: null,
            LastLine: _lastLine, DismissedInARow: _dismissed, LinesToday: LinesToday(), OnAc: !onBattery, BatteryPercent: percent, PausedUntil: _pausedUntil);
    }

    private static bool QuietNow(SaharSettings s, DateTimeOffset now)
    {
        if (s.QuietFrom is not { } from || s.QuietTo is not { } to) return false;
        var t = TimeOnly.FromDateTime(now.LocalDateTime);
        return from <= to ? t >= from && t < to : t >= from || t < to;
    }

    /// <summary>Each minute: if the gate opens and her brain is there, she may say something (often nothing).</summary>
    private string Initiative(bool force)
    {
        if (!force && !MindReady()) return _lastGate = "no-mind";   // nothing to ask without her brain
        var v = _gate.Check(GateInputs());
        _lastGate = v.Open ? "open" : v.ClosedBy ?? "closed";
        if (!v.Open && !force) return _lastGate;
        if (!MindReady()) return _lastGate = "no-mind";
        var id = "i" + (++_turnSeq).ToString(CultureInfo.InvariantCulture);
        if (!SendMind(new MindMsg { T = "turn", Id = id, Source = "initiative", Situation = Moment() })) return _lastGate = "no-mind";
        _turns[id] = (ActSource.Self, null, "initiative");
        Log.Write("mind: turn initiative");
        return _lastGate;
    }

    /// <summary>Claude Code asks for permission: she only tells him (0.13 notify-only); «شو عملت اليوم» says so.</summary>
    private void ClaudeAsking()
    {
        if (Director.CurrentKey != "claude-permission") return;
        _log.Append("claude.permission", "Claude بدّه إذنك", ActSource.Self, null, ActionOutcome.Refused, null);
        Log.Write("mind: claude permission (notified only)");
    }

    // ---- «شو عملت اليوم» for the settings app (production) ----

    private void RegisterLogCommands()
    {
        Pipe.Register("mind-log", r =>
        {
            var day = r.Args?.TryGetValue("day", out var d) == true ? d : "today";
            var json = ActionLogView.Json(ActionLogView.Pick(_log, day, DateTimeOffset.Now),
                e => e.UndoToken is { } t && _tools.Get(e.Tool) is { } tool && tool.CanUndo(t));
            return new PipeReply(true, "json64=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        });
        Pipe.Register("mind-undo", r =>
        {
            bool ok = r.Args?.TryGetValue("id", out var id) == true && _log.Undo(id);
            Log.Write(ok ? "mind: undone" : "mind: undo too late");
            return new PipeReply(true, ok ? "ok=true" : "ok=false");
        });
        Pipe.Register("mind-feedback", r =>
        {
            if (r.Args?.TryGetValue("id", out var id) != true || r.Args?.TryGetValue("value", out var v) != true
                || !int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return new PipeReply(false, "bad feedback");
            _log.Feedback(id!, value);
            return new PipeReply(true, "ok=true");
        });
    }

    // ---- test mode: the UI stage plays the Mind's lines ----

    private static string Brief(MindMsg m) =>
        $"{m.T}:{m.Id}:{m.Source ?? ""}:{m.Call?.ToString(CultureInfo.InvariantCulture) ?? ""}:{(m.Ok is { } ok ? (ok ? "ok" : "no") : "")}:{m.Error ?? ""}";

    private void RegisterTestCommands()
    {
        Pipe.Register("mind-test-fake", r =>
        {
            _fake = r.Args?.TryGetValue("on", out var on) != true || on == "true";
            if (r.Args?.TryGetValue("askSeconds", out var a) == true && double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double sec))
                _ask.Timeout = TimeSpan.FromSeconds(sec);
            _sent.Clear();
            return new PipeReply(true, $"fake={(_fake ? "true" : "false")}");
        });
        // One Mind line, base64 (the stage writes it as the Mind would): {"v":1,"t":"call",…}
        Pipe.Register("mind-test-msg", r =>
        {
            var line = r.Args?.TryGetValue("line64", out var b) == true ? Encoding.UTF8.GetString(Convert.FromBase64String(b)) : "";
            if (MindWire.Decode(line) is not { } m) return new PipeReply(false, "bad line");
            Raise(m);
            return new PipeReply(true, AutonomyDescribe());
        });
        Pipe.Register("mind-test-sent", _ => new PipeReply(true, "sent=" + string.Join(';', _sent)));
        Pipe.Register("mind-test-initiative", r => new PipeReply(true, "gate=" + Initiative(force: r.Args?.TryGetValue("force", out var f) == true && f == "true")));
        Pipe.Register("debug-autonomy", _ => new PipeReply(true, AutonomyDescribe()));
    }

    private string AutonomyDescribe()
    {
        var tools = string.Join(',', _registry.Announced.Select(t => t.Name));
        return $"chatTurn={_chatTurn ?? "-"} ask={_ask.Where ?? "none"} ask64={Convert.ToBase64String(Encoding.UTF8.GetBytes(_ask.Text ?? ""))} " +
            $"{Menu.MindDescribe()} gate={_lastGate} linesToday={LinesToday()} dismissed={_dismissed} logToday={_log.Day(DateOnly.FromDateTime(DateTime.Now)).Count} " +
            $"timer={(Timer.Running ? "true" : "false")} hush={(Dnd.On ? "true" : "false")} game={(Games.Running ? "true" : "false")} tools={tools}";
    }
}
