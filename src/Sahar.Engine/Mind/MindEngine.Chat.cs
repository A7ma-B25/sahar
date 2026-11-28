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
using Sahar.Core.Mind.Online;
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
    /// <summary>The line for a turn that never reached her model (0.18: the big brain fell back, or is too big for his RAM).</summary>
    private static string? ErrorLine(string? error) => error switch
    {
        "model-missing" or "load-failed" or "oom" or "busy" => ChatReply.Learning,
        "big-failed" => Sahar.Core.Mind.Runtime.RuntimeText.BigFailed,
        "too-big" => Sahar.Core.Mind.Runtime.RuntimeText.TooBig,
        _ => null,
    };

    public Func<string, bool>? IsDraftAsk { get; set; }
    /// <summary>0.17 (ruling R2): a card line that asks her to write or explain, with online usable: App moves it to the chat window.</summary>
    public Action<string>? MoveToChat { get; set; }

    private EngineTools _tools = null!;
    private Sahar.Core.Mind.Memory.ISecretBox _box = null!;
    private IToolRegistry _registry = null!;
    private readonly IAutonomyPolicy _policy = MindParts.Policy();
    private readonly IInitiativeGate _gate = MindParts.Gate();
    private IActionLog _log = null!;
    private AskDrill _ask = null!;
    private DispatcherQueueTimer _initiative = null!;

    /// <summary>The turns she is in: id → who started it and his line (clipped; kept only in the encrypted log).</summary>
    private readonly Dictionary<string, (ActSource Source, string? Line, string Kind)> _turns = [];
    private string? _chatTurn;   // the chat turn the header follows
    private readonly Dictionary<string, string> _ran = [];   // 0.15.0: what the turn's last action really did (Core template)
    private readonly HashSet<string> _acting = [];            // review I2: chat turns that sent a call (no bubble streaming)
    private readonly HashSet<string> _tainted = [];           // 0.19 spec §7: turns that already got untrusted tool output
    private BubbleStream? _bubble;                            // review I2: the chat turn's words in her bubble (card closed)
    private int _bubbleClicks;                                // his bubble clicks when this turn first drew there
    private int _turnSeq;
    private DateOnly _offeredOn;
    // initiative
    private DateTimeOffset? _lastLine, _awaitingAnswerSince, _pausedUntil;
    private int _dismissed, _linesToday;
    private DateOnly _linesDay;
    private string _lastGate = "none";
    // 0.15.0: when she may even ask her brain (spaced checks, ≥ 20 min between lines, a cap per hour, no repeated topic).
    private readonly InitiativeSchedule _schedule = new(DateTimeOffset.Now);
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
        _box = MindParts.SecretBox(MindPaths.Personal);
        _tools = new EngineTools(this, ActivityTools, _box, () => _gate.Check(GateInputs()).Open);
        _registry = MindParts.ToolRegistry(_tools.Announced());
        _log = MindParts.ActionLog(_box, name => _tools.Get(name), () => DateTimeOffset.Now);
        _pausedUntil = LoadPause();
        IsDraftAsk ??= t => Sahar.Core.Mind.Drafts.DraftAsk.Parse(t) is not null;
        Ui.TryEnqueue(() => Safe(_tools.ArmSaved));   // her reminders from before a restart
        _ask = new AskDrill(Ui, Menu, Panel)
        {
            InChat = (q, a) => Chat?.Ask(q, a) ?? false,   // 0.16: while the chat window is open the ask goes there
            TimedOutInChat = t => Chat?.AskEnded(t),
        };
        OfferDownload ??= OfferDownloadQuestion;
        Surfaces.ChatHub.Clipboard = Copy;
        if (Chat is { } chatHub)
        {
            chatHub.OpenFolder = () => Safe(OpenPictures);   // 0.19: «افتح المجلد» on her picture and voice cards
            // 0.17.2: warm the online connection when the window opens (no chat quota; the Mind throttles it). Only a
            // running Mind (opening the window never starts her brain; a Mind that starts asks OpenRouter's counter anyway).
            chatHub.Opened = () => Safe(() => { if (Sahar.Core.Mind.Online.OnlineChain.Usable(Settings())) SendIfRunning(new MindMsg { T = "online", Op = "warm" }); });
            chatHub.SetBrain = v => { Save(x => x with { MindChatBrain = v }); Ui.TryEnqueue(() => Safe(chatHub.Refresh)); };
            // Review I1: a new or deleted conversation: the online brain's pairs go (same box, the Mind reads it fresh).
            chatHub.Cleared = () => Safe(() =>
            {
                new Sahar.Core.Mind.Turn.TurnHistory(_box, () => DateTimeOffset.Now, () => Settings().MindEpisodeDays).Clear("online");
                Log.Write("mind: online history cleared");
            });
        }   // review minor: an older draft (no copy of its own) copies too
        ChatHandler = OnChat;
        SettingsChanged += _ => Ui.TryEnqueue(() => Safe(() => Chat?.Refresh()));   // 0.17: the chip follows «العقل أونلاين»
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
            Caps = new MindCaps(s.MindInitiative != InitiativeLevel.Off, s.MindLearnChats && !(_pausedUntil > DateTimeOffset.Now), s.MindVoice),
            Tools = toolCalls ? _registry.Announced : [],
        };
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"mind: error {e.GetType().Name}"); }
    }

    private bool MindReady() => _fake || Ready();

    /// <summary>0.16: the turn he started from the chat window (its words, thinking and errors go back there, and closing
    /// the card never cancels it).</summary>
    private string? _windowTurn;

    /// <summary>A line from the chat window: like the card's row, but her answer comes back to the window.</summary>
    public bool TakeWindowChat(ChatCommand c, string text)
    {
        // 0.17 spec §9: with the chip on «محلي», a writing line gets one suggestion per conversation (never a silent switch).
        var s = Settings();
        if (_brainOnce is null && c.Kind == ChatKind.Unknown && s.MindChatBrain == "local" && OnlineChain.Usable(s) && Intents.Writing(text)
            && !Intents.Action(text) && IsDraftAsk?.Invoke(text) != true && Chat is { } ch && ch.SuggestOnce()
            && ch.Ask(Address.For(Address.You, SuggestLine), yes => Ui.TryEnqueue(() => Safe(() =>
            {
                Log.Write($"mind: online suggested ({(yes ? "yes" : "no")})");
                _brainOnce = yes ? "online" : "local";
                try { TakeWindowChat(c, text); }
                finally { _brainOnce = null; }
            })), "إي، أونلاين", "لا، محلي", TimeSpan.FromMinutes(2)))
            return true;
        _fromWindowNext = true;   // review I3: the card's «عم فكّر…» is not for this turn
        try { if (!TakeChat(c, text)) return false; }
        finally { _fromWindowNext = false; }
        _windowTurn = _chatTurn;
        Chat?.Thinking(true);
        return true;
    }

    private bool _fromWindowNext;
    /// <summary>His answer to the suggestion, for the one line it was about.</summary>
    private string? _brainOnce;
    private const string SuggestLine = "هيدا بيطلع أحلى أونلاين، بدّك جاوبك أونلاين؟";

    /// <summary>0.17 spec §8: her focus face («•••») while a chat turn thinks — only over her own face, never an event's.</summary>
    private bool _thinkingFace;
    private void ThinkingFace(bool on)
    {
        if (on == _thinkingFace) return;
        if (on && Director.CurrentKey is not ("idle" or "music")) return;
        _thinkingFace = on;
        // Review: «off» goes back to the director's own face — a real Claude Code face that came meanwhile stays.
        if (!on && Director.CurrentKey == "claude-working") return;
        Express(on ? "claude-working" : "", false);
    }

    /// <summary>Review I3: a window turn ended (answered, replaced, failed): its «عم تكتب…» goes and nothing points at it.</summary>
    private void EndWindowTurn(string id)
    {
        if (id != _windowTurn) return;
        _windowTurn = null;
        Chat?.Thinking(false);
    }

    private bool FromWindow(string? id) => id is not null && id == _windowTurn;

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
        var s0 = Settings();
        bool online = OnlineChain.Usable(s0);
        // 0.17 (ruling R2): writing or explaining from the card → the chat window (never online from the card itself).
        if (!_fromWindowNext && !draft && online && s0.MindChatBrain != "local" && MoveToChat is { } move && Intents.Writing(text) && !Intents.Action(text))
        {
            Log.Write("mind: card line moved to the chat window (writing)");
            move(text);
            return true;
        }
        // 0.17: the chat window can talk online even before her local brain is there (actions still need it).
        if (!MindReady() && !(_fromWindowNext && online))
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
        if (_chatTurn is { } running) { SendMind(new MindMsg { T = "cancel", Id = running }); _turns.Remove(running); Forget(running); EndWindowTurn(running); }
        var source = draft ? "draft" : "chat";
        var turnMsg = new MindMsg { T = "turn", Id = id, Source = source, Text = text, Situation = Moment() };
        if (_fromWindowNext) turnMsg = turnMsg with { Window = true, Brain = _brainOnce ?? s0.MindChatBrain };
        if (!SendMind(turnMsg)) return false;
        ThinkingFace(true);   // 0.17 spec §8: her body answers before any word
        _turns[id] = (ActSource.User, Clip(text, 60), source);
        _chatTurn = id;
        if (!_fromWindowNext) Menu.MindThinking(true);
        else Menu.MindThinking(false);   // a card turn it replaced was thinking
        Log.Write($"mind: turn {source}");
        return true;
    }

    /// <summary>The fast path ran a command: «شو عملت اليوم» gets its line (with undo while it applies).</summary>
    private void LogFastPath(ChatCommand c, string text, bool hushBefore)
    {
        var why = Clip(text, 60);
        switch (c.Kind)
        {
            case ChatKind.Timer when Timer.Running && ChatReply.TimerLength(c) is { } t:
                Append("timer.start", Call("timer.start", t.TotalSeconds % 60 == 0 ? $$"""{"minutes":{{(int)t.TotalMinutes}}}""" : $$"""{"seconds":{{(int)t.TotalSeconds}}}"""),
                    ActSource.User, why, ActionOutcome.Done,
                    TimerTokenNow());
                break;
            case ChatKind.Note when c.Text is not null && Notes.Store.Latest(1) is [var n]:
                Append("note.save", Call("note.save", """{"text":"x"}"""), ActSource.User, why, ActionOutcome.Done,
                    "note:" + n.At.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + n.Text);
                break;
            case ChatKind.Hush when Dnd.On && !hushBefore:
                Append("hush.on", c.HushFor is { } h ? Call("hush.on", $$"""{"minutes":{{(int)h.TotalMinutes}}}""") : Call("hush.on", "{}"),
                    ActSource.User, why, ActionOutcome.Done, Dnd.Remaining is { } left
                        ? "hush:" + (DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : "hush:off");
                break;
            case ChatKind.Sleep:
                Append("sleep.hide", Call("sleep.hide", """{"minutes":60}"""), ActSource.User, why, ActionOutcome.Done, null);
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

    private ActionEntry Append(string tool, ToolCall call, ActSource source, string? why, ActionOutcome outcome, string? undo, string? summary = null)
    {
        var e = _log.Append(tool, summary ?? ToolTemplates.Done(call), source, why, outcome, undo);
        if (outcome is ActionOutcome.Done or ActionOutcome.AskedYes && _doneDay == DateOnly.FromDateTime(DateTime.Now)) _doneToday++;
        return e;
    }

    private DateOnly _doneDay;
    private int _doneToday;

    private static string? Clip(string? s, int max) => s is null ? null : s.Length <= max ? s.Trim() : s[..max].Trim() + "…";

    private void CardOpenChanged(bool open)
    {
        if (open) return;
        // 0.15.0 (owner: «mind: cancel (card closed)» ×4): a closed card — Esc, a click away, or a game or move that
        // closes it — no longer stops her reply: it goes on in her bubble (ReplyRoute). A new line still replaces it.
        if (_chatTurn is not null && !ReplyRoute.CardCloseCancels) Log.Write("mind: card closed, reply goes on in her bubble");
        if (_todayFactShown) { _todayFactDay = DateOnly.FromDateTime(DateTime.Now); _todayFactShown = false; }
    }

    /// <summary>The once-a-day fact, only with her brain there (it is about what her brain did). The count is read from
    /// the box once a day and kept up to date by her own appends, never on every redraw.</summary>
    private HeaderFact? TodayFact()
    {
        if (!MindReady()) return null;
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_todayFactDay == today && !_todayFactShown) return null;
        if (_doneDay != today) { _doneDay = today; _doneToday = ActionLogView.DoneToday(_log, DateTimeOffset.Now); }
        int n = _doneToday;
        if (n == 0) return null;
        _todayFactShown = true;
        return new HeaderFact("today", ActionLogView.DoneLine(n), "settings");
    }

    // ---- what the Mind says ----

    private void OnMind(MindMsg m)
    {
        switch (m.T)
        {
            case "state" when m.Id is not null && m.Id == _chatTurn && FromWindow(m.Id):
                Chat?.Thinking(m.Phase == "thinking");
                break;
            case "partial" when m.Id is not null && m.Id == _chatTurn && FromWindow(m.Id) && m.Text is { } windowPartial:
                ThinkingFace(false);
                Chat?.Partial(windowPartial.Length == 0 ? "" : Address.For(Address.You, ChatWords(windowPartial)));
                break;
            case "say" when m.Final == false && m.Kind == "note" && m.Id is not null && m.Id == _chatTurn && FromWindow(m.Id):
                // 0.17: «خلّصت طلبات الأونلاين لليوم» before her local answer: a line of its own; the turn goes on.
                Chat?.Said(Address.For(Address.You, ChatWords(m.Text ?? "")), brain: "local");
                Chat?.Thinking(true);
                Log.Write("mind: online is out for today (told)");
                break;
            case "state" when m.Id is not null && m.Id == _chatTurn:
                if (m.Phase == "thinking") Menu.MindThinking(true);
                else if (m.Phase == "idle") Menu.MindThinking(false);   // review X1: «عم فكّر…» always ends
                break;
            case "partial" when m.Id is not null && m.Id == _chatTurn && m.Text is { } partial:
                var words = Words(partial);
                if (ReplyRoute.Where(Menu.IsOpen, MenuCard.FitsHeader(words)) == ReplyPlace.Header) Menu.MindStream(words);
                else if (!Menu.IsOpen)
                {
                    // 0.15.0: the card closed, her words go on in her bubble — short ones only, never for a turn that acts,
                    // and not after he clicked it away (review I2).
                    _bubble ??= new BubbleStream();
                    bool first = !_bubble.Shown;
                    if (_bubble.Partial(words, _acting.Contains(m.Id), BubbleDismissed()) is { } shown)
                    {
                        if (first) _bubbleClicks = Speech.Clicks;
                        Speech.Say(Address.For(Address.You, shown));
                    }
                }
                break;
            case "call":
                HandleCall(m);
                break;
            case "tool-out" when m.Id is { } outId:
                ToolOut(outId, m);
                break;
            case "say":
                Said(m);
                break;
            case "error" when m.Id is not null:
                Log.Write($"mind: turn error {m.Error}");
                if (m.Id == _chatTurn) ThinkingFace(false);
                if (m.Id == _chatTurn && FromWindow(m.Id))
                {
                    if (ErrorLine(m.Error) is { } errLine) Chat?.Said(Address.For(Address.You, errLine));
                    if (m.Error != "timeout") { EndWindowTurn(m.Id); _chatTurn = null; }   // review I3: a timeout's line follows
                }
                else if (m.Id == _chatTurn)
                {
                    Menu.MindThinking(false);
                    if (ErrorLine(m.Error) is { } errLine)
                    {
                        var learning = Address.For(Address.You, errLine);
                        if (Menu.IsOpen) Menu.SetReply(learning); else Speech.Say(learning);   // review (a): the card may be closed
                    }
                    if (m.Error != "timeout") _chatTurn = null;   // a timeout's line follows
                }
                if (m.Error != "timeout") { _turns.Remove(m.Id); Forget(m.Id); }
                break;
            case "draft" when m.Id is not null && m.Id == _chatTurn:   // stream B shows it («بصوتك» + copy)
                _turns.Remove(m.Id);
                _chatTurn = null;
                if (m.Id == _windowTurn) _windowTurn = null;   // the draft card ends «عم تكتب…» itself
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

    /// <summary>0.17 review I4: in the chat window her digits stay as written in code, links and Latin words (range(10),
    /// v1.2, gpt-4o); prose numbers still read Arabic.</summary>
    private string ChatWords(string text) => Settings().Language == UiLanguage.English ? text : Digits.ForChat(text);

    private void Said(MindMsg m)
    {
        var id = m.Id ?? "";
        _turns.TryGetValue(id, out var turn);
        var text = Words(m.Text ?? "");
        if (m.Mood == "distress") { _pausedUntil = DateTimeOffset.Now.AddHours(24); SavePause(); }   // spec §7.4: no initiative for a day, across restarts
        if (turn.Kind == "initiative")
        {
            _turns.Remove(id);
            if (text.Trim().Length == 0) { Log.Write("mind: initiative, nothing to say"); return; }
            var gate = _gate.Check(GateInputs());
            if (!gate.Open) { _lastGate = gate.ClosedBy ?? "closed"; Log.Write($"mind: initiative line dropped ({_lastGate})"); return; }   // the moment passed
            if (_schedule.Repeats(text, DateTimeOffset.Now)) { Log.Write("mind: initiative line dropped (repeat)"); return; }   // a topic she raised lately
            _schedule.Spoke(DateTimeOffset.Now, text, Settings().MindInitiative);
            _lastLine = DateTimeOffset.Now;
            _awaitingAnswerSince = _lastLine;
            _linesToday++;
            Log.Write($"mind: initiative line ({text.Length} chars)");
            Speech.Say(text, comment: true);
            return;
        }
        _ran.Remove(id, out var ran);
        if (id == _chatTurn) ThinkingFace(false);
        if (id != _chatTurn) { _turns.Remove(id); Forget(id); Log.Write("mind: said dropped (late)"); return; }   // a replaced turn's late words
        _chatTurn = null;
        _turns.Remove(id);
        if (FromWindow(id))
        {
            _windowTurn = null;
            text = ChatWords(m.Text ?? "");
            if (text.Length == 0) { Chat?.Thinking(false); Log.Write("mind: said dropped (empty)"); return; }
            Log.Write($"mind: said to the chat ({text.Length} chars, {(m.Brain == "online" ? "online" : "local")})");
            Chat?.Said(Address.For(Address.You, text), brain: m.Brain == "online" ? "online" : "local");
            return;
        }
        Menu.MindThinking(false);
        if (text.Length == 0) { Forget(id); Log.Write("mind: said dropped (empty)"); return; }
        // 0.15.0: the card closed meanwhile (a game or a move closes it): a long reply becomes the short line of what ran.
        var line = ReplyRoute.Final(text, ran is null ? null : Words(ran), Menu.IsOpen);
        var place = ReplyRoute.Where(Menu.IsOpen, MenuCard.FitsHeader(line));
        bool dismissed = BubbleDismissed();
        Forget(id);
        Log.Write($"mind: said ({line.Length} chars, {(place == ReplyPlace.Header ? "card" : Menu.IsOpen ? "bubble" : "bubble, card closed")}{(line != text ? ", confirmation" : "")}{(place == ReplyPlace.Bubble && dismissed ? ", he clicked it away" : "")})");
        if (place == ReplyPlace.Header) Menu.SetReply(line);
        else if (new BubbleStream().Final(line, dismissed) is { } shown) Speech.Say(Address.For(Address.You, shown));
    }

    /// <summary>Review I2: he clicked this turn's bubble away since it first drew there.</summary>
    private bool BubbleDismissed() => _bubble is { Shown: true } && Speech.Clicks != _bubbleClicks;

    /// <summary>A chat turn ended (said, error, replaced): its per-turn state goes (review g).</summary>
    private void Forget(string id)
    {
        _ran.Remove(id);
        _acting.Remove(id);
        _tainted.Remove(id);
        if (id == _chatTurn || _chatTurn is null) _bubble = null;
    }

    // ---- calls: the engine validates and decides (spec §3.7) ----

    private void HandleCall(MindMsg m)
    {
        var id = m.Id ?? "";
        int n = m.Call ?? 0;
        if (!_turns.TryGetValue(id, out var turn) || turn.Kind == "draft") { Reply(id, n, false, "refused", ToolTemplates.Failed); return; }   // not ours, or a draft: words only
        if (id == _chatTurn) _acting.Add(id);
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
        var ctx = Context(turn.Kind) with { Tainted = _tainted.Contains(id) };   // 0.19: untrusted output in this turn → ask again
        var d = _policy.Decide(spec, call, cls, source, ctx);
        var why = source == ActSource.User ? turn.Line : Clip(call.Why, 80);
        bool callPause = ctx.CallStarting && call.Tool == "music.control" && EngineTools.Str(call, "op") == "pause";
        if (source == ActSource.Self && d.Verdict != Verdict.Refuse && !callPause && _gate.Check(GateInputs()) is { Open: false } closed)
            d = new AutonomyDecision(Verdict.Refuse, "gate:" + closed.ClosedBy);   // her own action waits for a calm moment, re-checked now
        if (d.Verdict == Verdict.Act && call.Tool is "timer.start" or "timer.cancel" && _tools.HisTimerRunning)
            d = new AutonomyDecision(Verdict.Ask, "his-timer");   // his timer: never replaced or stopped without his yes
        Log.Write($"mind: call {call.Tool} {d.Verdict.ToString().ToLowerInvariant()}{(d.Reason is { } r ? $" ({r})" : "")}");
        if (spec.MindSide && d.Verdict != Verdict.Refuse)
        {
            // 0.19: an MCP or media tool runs in the Mind: the engine's yes is an answer{yes}; his «لا» a refusal.
            void Go(ActionOutcome how)
            {
                Append(call.Tool, call, source, why, how, null);
                SendMind(new MindMsg { T = "answer", Id = id, Call = n, Yes = true });
            }
            if (d.Verdict == Verdict.Act) { Go(ActionOutcome.Done); return; }
            var ask = ToolTemplates.Ask(spec);
            _ask.Ask(ask, yes => Safe(() =>
            {
                if (yes) Go(ActionOutcome.AskedYes);
                else
                {
                    Append(call.Tool, call, source, why, ActionOutcome.AskedNo, null, "سألتك: " + ask);
                    Reply(id, n, false, "asked-no", ToolTemplates.AskedNo);
                }
            }));
            return;
        }
        switch (d.Verdict)
        {
            case Verdict.Act:
                Run(id, n, call, source, why, ActionOutcome.Done);
                break;
            case Verdict.Ask:
                if (source == ActSource.Self) _selfAsks.Add(DateTimeOffset.Now);
                var question = d.Reason == "his-timer"
                    ? (call.Tool == "timer.start" ? "بغيّر مؤقتك؟ (" + ToolTemplates.Done(call) + ")" : "بوقّف مؤقتك؟")
                    : ToolTemplates.Question(call);
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
        _tools.Source = source;
        tool.RunAsync(call).ContinueWith(t => Ui.TryEnqueue(() => Safe(() =>
        {
            var o = t.IsCompletedSuccessfully ? t.Result : ToolOutcome.Fail("failed", ToolTemplates.Failed);
            if (!tool.Spec.ReadOnly)
                Append(call.Tool, call, source, why, o.Ok ? done : ActionOutcome.Failed, o.UndoToken,
                    done == ActionOutcome.AskedYes ? o.Summary : o.Ok ? o.Summary : "ما زبطت: " + ToolTemplates.Done(call));
            if (o.Ok && !tool.Spec.ReadOnly && id == _chatTurn) _ran[id] = o.Summary;   // her short line if the card closes
            Reply(id, n, o.Ok, o.Error, o.Summary, o.Data);
        })), TaskScheduler.Default);
    }

    /// <summary>0.19: what a Mind-side tool returned, shown in the chat window as the tool's (a text card, her picture,
    /// her voice clip); text marks the turn tainted. Logged: the tool name, the kind and ok/code only.</summary>
    private void ToolOut(string id, MindMsg m)
    {
        var tool = m.Tool ?? "";
        var kind = m.Kind ?? "none";
        if (m.Text is { Length: > 0 }) _tainted.Add(id);
        Log.Write($"mind: tool-out {tool} {kind} {(m.Ok == true ? "ok" : m.Error ?? "failed")}");
        if (Chat is not { } chat) return;
        if (m.Text is { Length: > 0 } text) chat.Card(Sahar.Core.Chat.ChatLine.Tool, tool, text);
        else if (m.Ok == true && m.Data is { } file && MediaTools.IsMediaFile(file))
            chat.Card(kind == "voice" ? Sahar.Core.Chat.ChatLine.Voice : Sahar.Core.Chat.ChatLine.Image, tool, file);
    }

    /// <summary>«افتح المجلد»: Pictures\Sahar in Explorer.</summary>
    private static void OpenPictures()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Sahar");
        Directory.CreateDirectory(dir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { dir }, UseShellExecute = false });
        Log.Write("mind: pictures folder opened");
    }

    /// <summary>0.19: her tools (the Mind's tools-status): the engine validates and decides MCP and media calls too.</summary>
    partial void ToolsSeen(ToolsInfo info)
    {
        if (_tools is null) return;   // before the chat side is attached
        _registry = MindParts.ToolRegistry([.. _tools.Announced(), .. info.Announced.Where(t => t.MindSide)]);
        Log.Write($"mind: {_registry.Announced.Count} tools ({info.Announced.Count} of hers)");
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
        var level = Settings().MindInitiative;
        if (!force && !_schedule.MayCheck(DateTimeOffset.Now, level)) return _lastGate = "schedule";   // 0.15.0: not every minute
        var v = _gate.Check(GateInputs());
        _lastGate = v.Open ? "open" : v.ClosedBy ?? "closed";
        if (!v.Open && !force) return _lastGate;
        if (!MindReady()) return _lastGate = "no-mind";
        var id = "i" + (++_turnSeq).ToString(CultureInfo.InvariantCulture);
        if (!SendMind(new MindMsg { T = "turn", Id = id, Source = "initiative", Situation = Moment() })) return _lastGate = "no-mind";
        _turns[id] = (ActSource.Self, null, "initiative");
        _schedule.Asked(DateTimeOffset.Now, level);
        Log.Write($"mind: turn initiative (next check {_schedule.NextCheck:HH:mm})");
        return _lastGate;
    }

    /// <summary>Claude Code asks for permission: she only tells him (0.13 notify-only); «شو عملت اليوم» says so.</summary>
    private void ClaudeAsking()
    {
        if (Director.CurrentKey != "claude-permission") return;
        _log.Append("claude.permission", "Claude بدّه إذنك", ActSource.Self, null, ActionOutcome.Refused, null);
        Log.Write("mind: claude permission (notified only)");
    }

    // ---- the distress pause, kept across restarts (spec §7.4) ----

    private const string PauseFile = "autonomy-pause";

    private DateTimeOffset? LoadPause()
    {
        try
        {
            var b = _box.Read(PauseFile);
            if (b is { Length: > 0 } && long.TryParse(Encoding.UTF8.GetString(b), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            {
                var until = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                return until > DateTimeOffset.Now ? until : null;
            }
        }
        catch (Exception) { }
        return null;
    }

    private void SavePause()
    {
        try { _box.Write(PauseFile, Encoding.UTF8.GetBytes((_pausedUntil ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture))); }
        catch (Exception e) { Log.Write($"mind: pause not kept ({e.GetType().Name})"); }
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
        return $"chatTurn={_chatTurn ?? "-"} windowTurn={_windowTurn ?? "-"} ask={_ask.Where ?? "none"} ask64={Convert.ToBase64String(Encoding.UTF8.GetBytes(_ask.Text ?? ""))} " +
            $"{Menu.MindDescribe()} gate={_lastGate} linesToday={LinesToday()} dismissed={_dismissed} logToday={_log.Day(DateOnly.FromDateTime(DateTime.Now)).Count} " +
            $"timer={(Timer.Running ? "true" : "false")} hush={(Dnd.On ? "true" : "false")} game={(Games.Running ? "true" : "false")} tools={tools}";
    }
}
