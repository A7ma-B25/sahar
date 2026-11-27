using System.Globalization;
using Sahar.Core.Chat;
using Sahar.Core.Geometry;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Memory;
using Sahar.Core.Settings;
using Sahar.Core.Surfaces;
using Sahar.Engine.Diagnostics;
using Windows.System;
using D = Sahar.Core.Text.Digits;

namespace Sahar.Engine.Surfaces;

/// <summary>
/// The chat window (0.16; V15Chats, owner rulings 1–2): he on the left, she on the right; her streamed words, receipts,
/// drafts («بصوتك» + «انسخها») and inline asks; the conversation list grouped by day. History lives encrypted in her
/// SecretBox (<see cref="ChatStore"/>), follows MindEpisodeDays, and is wiped by «انسي كل شي». Nothing of it is logged
/// (kinds and lengths only), notified or shown in the tray.
/// </summary>
internal sealed class ChatHub
{
    public const string Surface = "chat";
    private static readonly TimeSpan Continue = TimeSpan.FromHours(6);
    private readonly DispatcherQueue _ui;
    private readonly SurfaceClient _client;
    private readonly Func<RectF> _body;
    private readonly Func<SaharSettings> _settings;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<ISecretBox> _box;
    private ChatStore _store;
    private string? _conv;
    private string _view = "thread";
    private bool _open, _typing;
    private string? _partial;
    private int _seq;
    private readonly HashSet<int> _copied = [];
    private (string Conv, int Index, Action<bool> Answer)? _ask;
    private (string Yes, string No) _askLabels = ("إي", "لا");
    private readonly Dictionary<int, Func<string, bool>> _copy = [];

    /// <summary>A line he sent from the window: App runs it like the card's chat row, with her answer coming back here.</summary>
    public Action<string>? Send { get; set; }
    /// <summary>He was answered while the window was closed: her content-free say bubble («ردّيت عليك»).</summary>
    public Action? AnsweredWhileClosed { get; set; }

    public bool IsOpen => _open;
    public int Drafts { get; private set; }
    public bool Saving => _store.Saving;

    public ChatHub(DispatcherQueue ui, SurfaceClient client, Func<RectF> body, Func<SaharSettings> settings, Func<ISecretBox>? box = null, Func<DateTimeOffset>? now = null)
    {
        (_ui, _client, _body, _settings) = (ui, client, body, settings);
        _now = now ?? (() => DateTimeOffset.Now);
        _box = box ?? (() => MindParts.SecretBox(MindPaths.Personal));
        _store = NewStore();
        try { _store.Prune(); } catch (Exception e) { Log.Write($"chat: prune failed ({e.GetType().Name})"); }
        // Review I5: past the days it goes while she runs for weeks too, not only at her start.
        _prune = ui.CreateTimer();
        _prune.Interval = TimeSpan.FromHours(6);
        _prune.Tick += (_, _) => Prune();
        _prune.Start();
        _partialTimer = ui.CreateTimer();
        _partialTimer.IsRepeating = false;
        _partialTimer.Tick += (_, _) => { if (_partialGate.Take(_now())) SendPartial(); };
    }

    private readonly DispatcherQueueTimer _prune;

    private ChatStore NewStore() => new(_box(), _now, () => _settings().MindEpisodeDays);

    // ---- opening ----

    /// <summary>Opens beside her on her monitor: the conversation of the last 6 hours, or the empty state.</summary>
    public void Open(bool fresh = false)
    {
        if (!fresh && _conv is null && _store.List().FirstOrDefault() is { } last && _now() - last.Last < Continue) _conv = last.Id;
        if (fresh) _conv = null;
        _view = "thread";
        if (_open) { Push(); _client.Focus(Surface); return; }
        _open = true;
        int seq = 0;
        seq = _client.Show(Surface, "chat", "window", Model(), _body(), e => Event(seq, e));
        _seq = seq;
        Log.Write("chat: open");
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _client.Hide(Surface, _seq);
        Log.Write("chat: closed");
    }

    private void Event(int seq, SurfaceEvent e)
    {
        if (seq != _seq) return;
        try
        {
            switch (e.Kind)
            {
                case "closed" or "failed":
                    _open = false;
                    if (e.Kind == "failed") Log.Write("chat: no window (host missing)");
                    break;
                case "close": Close(); break;
                case "send" when !string.IsNullOrWhiteSpace(e.Value):
                    var text = e.Value!.Trim();
                    if (text.Length > 500) text = text[..500];
                    if (_view != "thread") _view = "thread";
                    Mine(text);
                    Log.Write($"chat: sent ({text.Length} chars)");
                    Send?.Invoke(text);
                    break;
                case "all": _view = _view == "list" ? "thread" : "list"; Push(); break;
                case "new": _conv = null; _view = "thread"; Push(); break;
                case "open" when e.Value is { } id: _conv = id; _view = "thread"; Push(); break;
                case "rename" when e.Value is { } v && v.Split('\n', 2) is [var rid, var title]: _store.Rename(rid, title); Push(); break;
                case "delete" when e.Value is { } did:
                    _store.Delete(did);
                    if (_conv == did) _conv = null;
                    Log.Write("chat: conversation deleted");
                    Push();
                    break;
                case "copy" when e.Value is { } mid && int.TryParse(mid.TrimStart('m'), out int i): CopyDraft(i); break;
                case "ask" when e.Value is { } a: AnswerAsk(a); break;
            }
        }
        catch (Exception x) { Log.Write($"chat: error {x.GetType().Name}"); }
    }

    // ---- what goes in ----

    public void Mine(string text) => _conv = _store.Append(_conv, new ChatEntry(_now(), ChatWho.Me, ChatLine.Text, text));

    public void Thinking(bool on)
    {
        _typing = on;
        if (!on) { _partial = null; _partialTimer.Stop(); }
        if (_open) Push();
    }

    /// <summary>Her words so far. Review I8: only the words go (never the whole model, no store reads), at most ~10 a
    /// second; the last one always arrives (a trailing send).</summary>
    public void Partial(string words)
    {
        bool first = !_typing || _partial is null;
        _partial = words;
        _typing = true;
        if (!_open) return;
        if (first) { _partialGate.Take(_now()); Push(); return; }   // the typing row appears with the full model
        if (_partialGate.Take(_now())) { SendPartial(); return; }
        if (_partialTimer.IsRunning) return;
        _partialTimer.Interval = _partialGate.Wait(_now()) is var w && w > TimeSpan.Zero ? w : TimeSpan.FromMilliseconds(1);
        _partialTimer.Start();
    }

    private readonly Throttle _partialGate = new(TimeSpan.FromMilliseconds(100));
    private DispatcherQueueTimer _partialTimer = null!;

    private void SendPartial()
    {
        if (!_open || _partial is null) return;
        _client.Update(Surface, _seq, JModel.Of("chat-partial").S("partial", _partial).T("typing", _typing), mode: "partial");
    }

    /// <summary>Her answer to a line he sent here (or the fast path's reply), with an optional receipt line.</summary>
    public void Said(string text, string? receipt = null, string? receiptIcon = null)
    {
        _typing = false;
        _partial = null;
        _partialTimer.Stop();
        var m = new ChatEntry(_now(), ChatWho.Her, receipt is null ? ChatLine.Text : ChatLine.Receipt,
            receipt is null ? text : text + "\n" + receiptIcon + "\u0001" + receipt);
        _conv = _store.Append(_conv, m);
        if (_open) Push();
        else AnsweredWhileClosed?.Invoke();
    }

    /// <summary>«بصوتك»: her draft card in the thread (the window opens for it). She never sends it.</summary>
    public void Draft(string text, string label, Func<string, bool> copy)
    {
        _typing = false;
        _partial = null;
        _conv = _store.Append(_conv, new ChatEntry(_now(), ChatWho.Her, ChatLine.Draft, text) { Answer = label });
        _copy[_store.Load(_conv).Count - 1] = copy;
        Drafts++;
        if (_open) Push(); else Open();
    }

    private void CopyDraft(int index)
    {
        if (_conv is null) return;
        var msgs = _store.Load(_conv);
        if (index < 0 || index >= msgs.Count || msgs[index].Kind != ChatLine.Draft) return;
        var copy = _copy.GetValueOrDefault(index) ?? Clipboard;
        bool ok = copy(msgs[index].Text);
        Log.Write($"chat: draft {(ok ? "copied" : "not copied")} ({msgs[index].Text.Length} chars)");
        if (!ok) return;
        _copied.Add(index);
        Push();
        var t = _ui.CreateTimer();
        t.IsRepeating = false;
        t.Interval = TimeSpan.FromSeconds(2);
        t.Tick += (_, _) => { _copied.Remove(index); if (_open) Push(); };
        t.Start();
    }

    /// <summary>Fallback copy (Unicode text) when the draft came without one.</summary>
    public static Func<string, bool> Clipboard { get; set; } = _ => false;

    /// <summary>An ask while the window is open goes inline (not to the bubble); false when it is closed.</summary>
    /// <param name="timeout">Review minor: an ask nobody else times out (the update and download questions) ends as «لا»
    /// after this, so its caller is never left waiting.</param>
    public bool Ask(string question, Action<bool> answer, string yes = "إي", string no = "لا", TimeSpan? timeout = null)
    {
        if (!_open) return false;
        _askLabels = (yes, no);
        _conv = _store.Append(_conv, new ChatEntry(_now(), ChatWho.Her, ChatLine.Ask, question));
        var ask = (_conv, _store.Load(_conv).Count - 1, answer);
        _ask = ask;
        Push();
        if (timeout is { } after)
        {
            var t = _ui.CreateTimer();
            t.IsRepeating = false;
            t.Interval = after;
            t.Tick += (_, _) => { if (_ask == ask) { AskEnded(timedOut: true); answer(false); } };
            t.Start();
        }
        return true;
    }

    private void AnswerAsk(string value)
    {
        // review minor: only the ask that is live, in the conversation shown (an old one in another chat never answers it)
        if (_ask is not { } a || a.Conv != _conv || value.Split(':') is not [var mid, var ans] || mid != "m" + a.Index.ToString(CultureInfo.InvariantCulture)) return;
        bool yes = ans == "yes";
        _ask = null;
        _store.ReplaceAt(a.Conv, a.Index, m => m with { Answer = yes ? "yes" : "no", At = m.At });
        Push();
        a.Answer(yes);
    }

    /// <summary>The ask ended without a tap (a timeout is «لا»: «ما جاوبت، فما عملت شي»).</summary>
    public void AskEnded(bool timedOut)
    {
        if (_ask is not { } a) return;
        _ask = null;
        _store.ReplaceAt(a.Conv, a.Index, m => m with { Answer = timedOut ? "timeout" : "no" });
        if (_open) Push();
    }

    /// <summary>«انسي كل شي», first half: nothing more is written until the box is new.</summary>
    public void Pause() => _store = new ChatStore(new NoBox(), _now, () => _settings().MindEpisodeDays);

    /// <summary>«انسي كل شي» done: a fresh box, an empty window at once.</summary>
    public void Forget()
    {
        _store = NewStore();
        _conv = null;
        _ask = null;
        _copy.Clear();
        _view = "thread";
        if (_open) Push();
        Log.Write("chat: forgot");
    }

    public void Prune()
    {
        try { _store.Prune(); } catch (Exception e) { Log.Write($"chat: prune failed ({e.GetType().Name})"); }
        if (_open) Push();
    }

    private sealed class NoBox : ISecretBox
    {
        public byte[]? Read(string name) => null;
        public void Write(string name, ReadOnlySpan<byte> data) => throw new MindStoreException("forgotten");
        public void Delete(string name) { }
    }

    // ---- the model ----

    private void Push()
    {
        if (!_open) return;
        _client.Update(Surface, _seq, Model());
    }

    private bool Arabic => _settings().Language != UiLanguage.English;
    private string Num(string s) => Arabic ? D.ToArabicIndic(s) : s;

    private string Clock(DateTimeOffset at, bool suffix)
    {
        var l = at.ToLocalTime();
        int h = l.Hour % 12 == 0 ? 12 : l.Hour % 12;
        return Num($"{h}:{l.Minute:00}") + (suffix ? (l.Hour < 12 ? " ص" : " م") : "");
    }

    private static readonly string[] Months = ["كانون التاني", "شباط", "آذار", "نيسان", "أيار", "حزيران", "تموز", "آب", "أيلول", "تشرين الأول", "تشرين التاني", "كانون الأول"];
    private static readonly string[] Weekdays = ["الأحد", "الاتنين", "التلاتا", "الأربعا", "الخميس", "الجمعة", "السبت"];

    private string DayLabel(DateTime day)
    {
        var today = _now().ToLocalTime().Date;
        if (day == today) return "اليوم";
        if (day == today.AddDays(-1)) return "مبارح";
        return Num(day.Day.ToString(CultureInfo.InvariantCulture)) + " " + Months[day.Month - 1];
    }

    private string ListTime(DateTimeOffset at)
    {
        var day = at.ToLocalTime().Date;
        var today = _now().ToLocalTime().Date;
        if (day == today) return Clock(at, false);
        if (day == today.AddDays(-1)) return "مبارح";
        if (day > today.AddDays(-7)) return Weekdays[(int)day.DayOfWeek];
        return DayLabel(day);
    }

    internal JModel Model()
    {
        var you = Address.ToYou;
        var j = JModel.Of("chat").S("view", _view).S("conv", _conv ?? "").S("name", "سحورة").N("max", 500).T("typing", _typing).S("partial", _partial);
        j.O("labels").S("all", "كل الحكي").S("new", "حكي جديد").S("close", "سكّر").S("typing", "عم تكتب…").S("send", you("ابعت"))
            .S("composer", "احكي مع سحورة").S("placeholder", you("احكي معي…")).S("newBelow", "جديد").S("back", "رجوع").S("dialog", "الحكي مع سحورة").End();
        var info = _conv is null ? null : _store.List().FirstOrDefault(c => c.Id == _conv);
        j.S("status", info?.Title ?? "حكي جديد");
        var msgs = _conv is null ? [] : _store.Load(_conv);
        if (_view == "thread" && msgs.Count == 0 && _partial is null && !_typing) j.S("view", "empty");
        // the thread: days → groups (one side, within 5 minutes) → messages
        j.A("days");
        DateTime? day = null;
        bool inGroup = false, inDay = false;
        ChatWho? who = null;
        DateTimeOffset groupAt = default;
        for (int i = 0; i < msgs.Count; i++)
        {
            var m = msgs[i];
            var d = m.At.ToLocalTime().Date;
            bool newDay = day != d;
            bool newGroup = newDay || who != m.Who || m.At - groupAt > TimeSpan.FromMinutes(5) || m.Kind == ChatLine.Draft;
            if (newGroup && inGroup) { j.End().S("time", Clock(groupAt, d != _now().ToLocalTime().Date)).End(); inGroup = false; }
            if (newDay)
            {
                if (inDay) { j.End().End(); }
                j.Item().S("label", DayLabel(d)).A("groups");
                inDay = true;
                day = d;
            }
            if (newGroup)
            {
                j.Item().S("who", m.Who == ChatWho.Me ? "me" : "her").A("msgs");
                inGroup = true;
                who = m.Who;
            }
            groupAt = m.At;
            Message(j, m, i);
        }
        if (inGroup) j.End().S("time", Clock(groupAt, day != _now().ToLocalTime().Date)).End();
        if (inDay) j.End().End();
        j.End();
        // the empty state
        j.O("empty").S("title", "لسا ما حكينا").S("hint", you("احكيلي شو بدك، أو جرّب وحدة من هدول:")).A("starters")
            .Item().S("icon", "clock").S("text", "شغّلي مؤقت ربع ساعة").End()
            .Item().S("icon", "pen").S("text", "اكتبلي رد بأسلوبي").End()
            .Item().S("icon", "moon").S("text", "إيمتى الصلاة الجاية؟").End()
            .End().End();
        // the list
        int days = Math.Clamp(_settings().MindEpisodeDays, 1, 3650);
        j.O("list").S("title", "حكينا").S("sure", "أكيد؟").S("deleteYes", "إي، امسحه").S("no", "لا").S("rename", "سمّيه").S("del", "امسحه").T("unsaved", !_store.Saving)
            .S("footer", _store.Saving ? Num($"محفوظ عندك بس، ومقفول. بينمسح لحاله بعد {days} يوم.") : "ما قدرت احفظ الحكي هلق.");
        j.A("sections");
        var today = _now().ToLocalTime().Date;
        foreach (var group in _store.List().GroupBy(c => c.Last.ToLocalTime().Date == today ? 0 : c.Last.ToLocalTime().Date > today.AddDays(-7) ? 1 : 2))
        {
            j.Item().S("title", group.Key switch { 0 => "اليوم", 1 => "هالأسبوع", _ => "أقدم" }).A("rows");
            foreach (var c in group)
                j.Item().S("id", c.Id).S("title", c.Title).S("last", Line(c.LastLine)).S("time", ListTime(c.Last)).T("sel", c.Id == _conv).End();
            j.End().End();
        }
        j.End().End();
        return j;
    }

    private static string Line(string text)
    {
        var one = text.Split('\n')[0].Trim();
        return one.Length <= 60 ? one : one[..60] + "…";
    }

    private void Message(JModel j, ChatEntry m, int i)
    {
        string id = "m" + i.ToString(CultureInfo.InvariantCulture);
        j.Item().S("id", id);
        switch (m.Kind)
        {
            case ChatLine.Draft:
                j.S("kind", "draft").S("text", m.Text).O("draft").S("label", m.Answer ?? "بصوتك").S("copy", "انسخها").S("copiedLabel", "نسختها")
                    .S("note", Address.ToYou("ما ببعتها لحدا، إنت بتلصقها")).T("copied", _copied.Contains(i)).End();
                break;
            case ChatLine.Ask:
                // review minor: an ask that is no longer live (her restart, another one since) shows as unanswered, inactive
                bool live = _ask is { } la && la.Conv == _conv && la.Index == i;
                var answer = m.Answer ?? (live ? null : "timeout");
                j.S("kind", "ask").S("text", m.Text).O("ask").S("yes", _askLabels.Yes).S("no", _askLabels.No).T("answered", answer is not null).End();
                if (answer is { } a)
                    j.O("receipt").S("icon", a == "yes" ? "check" : "x").S("text", a switch
                    {
                        "yes" => Address.ToYou("سمحتلها") + " · " + Clock(m.At, false),
                        "timeout" => Address.ToYou("ما جاوبت، فما عملت شي"),
                        _ => Address.ToYou("ما سمحتلها"),
                    }).End();
                break;
            case ChatLine.Receipt:
                var parts = m.Text.Split('\n', 2);
                var r = parts.Length == 2 ? parts[1].Split('\u0001', 2) : ["", ""];
                j.S("kind", "text").S("text", parts[0]).O("receipt").S("icon", r[0].Length > 0 ? r[0] : "clock").S("text", r.Length > 1 ? r[1] : "").End();
                break;
            default:
                j.S("kind", "text").S("text", m.Text);
                break;
        }
        j.End();
    }

    public string Describe() =>
        $"open={(_open ? "true" : "false")} view={_view} conv={(_conv is null ? "none" : "some")} count={(_conv is null ? 0 : _store.Load(_conv).Count)} " +
        $"chats={_store.List().Count} saving={(_store.Saving ? "true" : "false")} typing={(_typing ? "true" : "false")} drafts={Drafts}";
}
