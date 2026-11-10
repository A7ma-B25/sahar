using System.Diagnostics;
using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Design;
using Sahar.Core.Geometry;
using Sahar.Core.Menu;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Render;
using Sahar.Engine.Senses;
using Windows.System;

namespace Sahar.Engine.Menu;

/// <summary>
/// Her menu (spec §5): an opaque Porcelain card beside her — her line and the one most urgent fact in a mood-tinted
/// header, the chat row, her three tiles, four quiet tools and a footer. Opened by a right-click or the global shortcut
/// (a double-click when chosen), closed by Esc, a click outside, another window, or when she hides. Items with a drill
/// (play, hush, timer, listen, music) show it in place; the others close the card and raise <see cref="Pressed"/>.
/// While it is open her orbiting moon rests on the card's corner nearest her (the clasp).
/// </summary>
internal sealed class MenuCard : IDisposable
{
    private readonly CardVisual _visual;
    private readonly ChatField _chat;
    private readonly MenuCatcher _catcher;
    private readonly Func<RectF> _body;
    private readonly SaharVisual _sahar;
    private readonly DispatcherQueueTimer _tick, _replyTimer;
    private CardLayout? _layout;
    private MenuView? _view;
    private CardContent? _content;
    private string? _focus, _hovered, _reply;
    private nint _previous;
    private float _moonBefore = 1;
    private float _dpi = 1;
    private int _openGen;
    private Vector2 _moon;
    private readonly Stopwatch _openClock = new();
    private long _chatAt = -1, _openEnd = -1;
    private RectF _card;

    public Func<MenuState> State { get; set; } = () => new(false, false, false);
    public Func<MenuFacts> Facts { get; set; } = () => new(null, false, null, false, null, false);
    public Func<Mood> Mood { get; set; } = () => Sahar.Core.Art.Mood.Calm;
    public Func<bool> Dark { get; set; } = () => false;
    public Func<bool> MicAvailable { get; set; } = () => false;
    public Func<bool> SettingsAvailable { get; set; } = () => false;
    public IReadOnlySet<string> Visible { get; set; } = MenuCatalog.Visible(null, _ => true);
    public MenuHeader HeaderMode { get; set; } = MenuHeader.Urgent;
    public MenuPlace Placement { get; set; } = MenuPlace.Beside;
    /// <summary>The overlay grows to the card (screen px); the callback runs once it holds it (0.10.1 needs, 0.11 settle).</summary>
    public Action<string, RectF?, Action?> Need { get; set; } = (_, _, then) => then?.Invoke();

    public bool IsOpen { get; private set; }
    /// <summary>"main", or the drill showing.</summary>
    public string View => _view?.View ?? "main";
    public long OpenMs { get; private set; }

    public event Action<string>? Pressed;
    public event Action<bool>? OpenChanged;
    /// <summary>A drill opened (the music drill says what is playing).</summary>
    public event Action<string>? RingOpened;
    /// <summary>A chat line was sent (Enter or send). The text is the user's: never logged.</summary>
    public event Action<string>? ChatSent;

    public MenuCard(CompositionHost host, bool capturable, Func<RectF> body, SaharVisual sahar, DispatcherQueue ui)
    {
        _visual = new CardVisual(host);
        _body = body;
        _sahar = sahar;
        _catcher = new MenuCatcher(capturable)
        {
            Clicked = p => Safe(() => Click(p)),
            Moved = p => Safe(() => Hover(p)),
            Dismissed = () => Safe(Close),
            Deactivated = () => Safe(() => Close(restoreFocus: false)),
            KeyPressed = k => Safe(() => Key(k)),
        };
        _chat = new ChatField(capturable);
        _visual.Opened += () => Safe(() => { if (_openEnd < 0) _openEnd = _openClock.ElapsedMilliseconds; });
        _chat.Submitted += text => Safe(() => Send(text));
        _chat.Escaped += () => Safe(Close);
        _chat.TabbedOut += shift => Safe(() => { _catcher.Focus(); Key(shift ? MenuKey.ShiftTab : MenuKey.Tab); });
        _chat.Deactivated += next => Safe(() => { if (next != _catcher.Hwnd && !Win.Own.Contains(next)) Close(restoreFocus: false); });
        _tick = ui.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += (_, _) => Safe(Refresh);
        _replyTimer = ui.CreateTimer();
        _replyTimer.IsRepeating = false;
        _replyTimer.Interval = TimeSpan.FromSeconds(6);
        _replyTimer.Tick += (_, _) => Safe(() => { _reply = null; Refresh(); });
    }

    /// <summary>Applies the menu settings (Task 7; 0.10c calls it after a settings reload): the items switched on (and
    /// available), what the header shows, where the card opens, light or dark.</summary>
    public void Reload(SaharSettings s, Func<string, bool> available)
    {
        Visible = MenuCatalog.Visible(s.MenuItems, available);
        HeaderMode = s.MenuHeader;
        Placement = s.MenuPlace;
        var appearance = s.Appearance;
        Dark = appearance switch { Appearance.Dark => () => true, Appearance.Light => () => false, _ => Theme.AppsDark };
        SettingsAvailable = () => SettingsLauncher.Available;
        Refresh();
    }

    /// <summary>Windows that may take the focus while the card stays open (the chat field).</summary>
    public HashSet<nint> AllowedFocus => _catcher.AllowedFocus;

    public void Toggle(string how)
    {
        if (IsOpen) Close();
        else Open(how);
    }

    /// <param name="how">right, shortcut or double (logged).</param>
    public void Open(string how = "right", bool caretInChat = false)
    {
        if (IsOpen) return;
        var clock = Stopwatch.StartNew();
        _openClock.Restart();
        _chatAt = _openEnd = -1;
        IsOpen = true;
        var fg = Native.GetForegroundWindow();
        _previous = Win.Own.Contains(fg) ? 0 : fg;
        _focus = caretInChat && Visible.Contains("chat") ? "chat" : null;
        _hovered = null;
        var body = _body();
        var centre = new Vector2(body.X + body.Width / 2, body.Y + body.Height / 2);
        _catcher.Show(centre);
        Build("main");
        var monitor = Monitors.At(centre);
        _dpi = monitor.DpiScale;
        _visual.Draw(_content!, _dpi, fade: false, Key());
        var place = MenuCardLayout.Place(body, monitor.WorkF, new Vector2(MenuCardLayout.Width, _layout!.Height) * _dpi, Placement, _dpi);
        _moon = place.MoonCentre;
        _card = place.Card;
        OpenMs = clock.ElapsedMilliseconds;   // trigger → the card's visuals set up (spec §8: < 100 ms)
        _moonBefore = _sahar.MoonProps.TryGetScalar("moon", out var m) == Windows.UI.Composition.CompositionGetValueStatus.Succeeded ? m : 1;
        int gen = ++_openGen;
        Need("menu", Area(place.Card), () =>
        {
            if (!IsOpen || gen != _openGen) return;
            _sahar.MoonProps.InsertScalar("moon", 0);   // her own moon rests on the card instead
            _visual.Open(place.Card, place.Near, place.MoonCentre);
            ShowChat();
            if (_focus == "chat") FocusChat();
            ShowFocus();
        });
        Log.Write($"menu: open ({how})");
        OpenChanged?.Invoke(true);
        UpdateTick();
    }

    public void Close() => Close(restoreFocus: true);

    /// <param name="restoreFocus">false when another app took over (its click must keep it in front).</param>
    public void Close(bool restoreFocus)
    {
        if (!IsOpen) return;
        IsOpen = false;
        _openGen++;
        _tick.Stop();
        _catcher.Hide();
        _chat.Hide();
        _chat.Clear();
        _visual.Close();
        Need("menu", null, null);
        _sahar.MoonProps.InsertScalar("moon", _moonBefore);
        _view = null;
        _layout = null;
        Log.Write("menu: closed");
        OpenChanged?.Invoke(false);
        // Focus goes back to the window that had it.
        if (restoreFocus && _previous != 0 && Native.IsWindow(_previous) != 0) Native.SetForegroundWindow(_previous);
        _previous = 0;
    }

    public void Press(string id)
    {
        if (!IsOpen) return;
        if (id == "back") { Show("main", fade: true); return; }
        if (id == "fact")
        {
            if (_content?.Fact?.OpensId is { } opens) Press(opens);
            return;
        }
        if (id == "chat") { FocusChat(); ShowFocus(); return; }
        if (id == "send") { Send(_chat.Text); return; }
        if (id == "mic") return;   // deferred to the voice phase (never shown)
        if (MenuCatalog.Drill(id, State()) is not null) { Show(id, fade: true); return; }
        if (id == "dnd" && State().HushOn) id = "dnd-off";
        Log.Write($"menu: {id}");
        Close();
        Pressed?.Invoke(id);
    }

    /// <summary>Sends a chat line as Enter would (the pipe's menu-chat in test mode).</summary>
    public void Send(string text)
    {
        if (!IsOpen) return;
        _chat.Clear();
        if (string.IsNullOrWhiteSpace(text)) return;
        ChatSent?.Invoke(text);
    }

    private void ShowChat()
    {
        if (!IsOpen || _layout is null || _content is null || !_layout.Rects.TryGetValue("chat", out var r)) { _chat.Hide(); return; }
        var px = Screen(r);
        _chat.Show(new RectI((int)MathF.Round(px.X), (int)MathF.Round(px.Y), (int)MathF.Round(px.Right), (int)MathF.Round(px.Bottom)),
            _dpi, _content.T, true);
        if (_chatAt < 0) _chatAt = _openClock.ElapsedMilliseconds;
        AllowedFocus.Add(_chat.Hwnd);
        AllowedFocus.Add(_chat.EditHwnd);
    }

    private void FocusChat()
    {
        _focus = "chat";
        if (_visual.Shown) _chat.Focus();
    }

    /// <summary>Her reply to a chat line: header line 1 for 6 s.</summary>
    public void SetReply(string line)
    {
        _reply = line;
        _replyTimer.Stop();
        _replyTimer.Start();
        Refresh();
    }

    /// <summary>Re-reads facts and state and redraws the open card in place.</summary>
    public void Refresh()
    {
        if (!IsOpen || _view is null) return;
        Build(_view.View);
        _visual.Draw(_content!, _dpi, fade: false, Key());
        if (_visual.Shown) ShowChat();
        ShowFocus();
        UpdateTick();
    }

    private void Show(string view, bool fade)
    {
        if (view != "main") RingOpened?.Invoke(view);
        Build(view);
        _focus = _layout!.FocusOrder.Count > 0 && _focus is not null ? _layout.FocusOrder[0] : null;
        _hovered = null;
        _visual.SetHover(null, false, null);
        _visual.Draw(_content!, _dpi, fade, Key());
        if (_visual.Shown) ShowChat();
        ShowFocus();
    }

    private void Build(string view)
    {
        var state = State();
        var drill = view == "main" ? null : MenuCatalog.Drill(view, state);
        if (view != "main" && drill is null) view = "main";
        var fact = MenuHeaderFact.Pick(Facts(), HeaderMode);
        bool dark = Dark();
        _view = new MenuView(view, Visible, MicAvailable(), SettingsAvailable(), fact is not null, drill);
        _layout = MenuCardLayout.Layout(_view);
        _content = new CardContent(_layout, _view, Tokens.For(dark), Tokens.HeaderTint(Mood(), dark),
            MenuHeaderFact.Greeting(false, _reply, state.HushOn), fact, state.HushOn, true);
    }

    /// <summary>Everything the drawing depends on (the same key = the same pixels).</summary>
    private string Key()
    {
        var c = _content!;
        return string.Join('|', c.View.View, string.Join(',', c.Layout.FocusOrder), c.Line1, c.Fact?.Text ?? "", c.Hushed, c.T == Tokens.Dark,
            c.Tint, _dpi, Address.You);
    }

    /// <summary>The card plus its shadow (e1: 32 DIP blur, 12 down) and the clasp, in screen px.</summary>
    private RectF Area(RectF card)
    {
        float s = 40 * _dpi, below = 56 * _dpi;
        return new RectF(card.X - s, card.Y - s, card.Width + 2 * s, card.Height + s + below);
    }

    private void UpdateTick()
    {
        bool countdown = IsOpen && _content?.Fact?.Kind is "timer" or "hush";
        if (countdown && !_tick.IsRunning) _tick.Start();
        else if (!countdown) _tick.Stop();
    }

    // ---- Pointer and keys ----

    private RectF Screen(RectF r) => new(_card.X + r.X * _dpi, _card.Y + r.Y * _dpi, r.Width * _dpi, r.Height * _dpi);

    private IEnumerable<string> Targets()
    {
        if (_layout is null) yield break;
        foreach (var id in _layout.FocusOrder)
            if (id != "fact" || _content?.Fact?.OpensId is not null) yield return id;
    }

    private string? Hit(Vector2 p)
    {
        if (_layout is null || !_visual.Shown) return null;
        foreach (var id in Targets())
            if (Screen(_layout.Rects[id]).Contains(p.X, p.Y)) return id;
        return null;
    }

    private void Click(Vector2 p)
    {
        if (Hit(p) is { } id)
        {
            if (_layout!.Rects.TryGetValue(id, out var r)) _visual.SetHover(r, true, Radius(id));
            Press(id);
        }
        else if (!_card.Contains(p.X, p.Y)) Close();   // a click on the card between items does nothing
    }

    private void Hover(Vector2 p)
    {
        var id = Hit(p);
        if (id == _hovered) return;
        _hovered = id;
        _visual.SetHover(id is not null ? _layout!.Rects[id] : null, false, id is not null ? Radius(id) : null);
    }

    private void Key(MenuKey key)
    {
        if (_layout is null || _view is null) return;
        var r = MenuKeys.Handle(_layout, _view, _focus, key, rtl: true, inChat: false);
        if (r.Close) { Close(); return; }
        if (r.Back) { Show("main", fade: true); return; }
        if (r.Activate is { } id) { Press(id); return; }
        if (r.Focus != _focus)
        {
            _focus = r.Focus;
            if (_focus == "chat") FocusChat();
            ShowFocus();
        }
    }

    private void ShowFocus()
    {
        if (_layout is not null && _focus is not null && _layout.Rects.TryGetValue(_focus, out var r)) _visual.SetFocus(r, Radius(_focus));
        else _visual.SetFocus(null, 0);
    }

    private static float Radius(string id) => id switch
    {
        "play" or "dnd" or "hide" => 12,
        "chat" => 14,
        "fact" => 8,
        _ when id.StartsWith("dnd-", StringComparison.Ordinal) || id.StartsWith("timer-", StringComparison.Ordinal) && id != "timer-custom" => 9,
        _ => 10,
    };

    /// <summary>"open=true view=main focus=chat caret=false mic=false hush=false dark=false fact=prayer openMs=14 clasp=x,y chat=0
    /// line64=… buttons=id:cx,cy,w,h;…" (screen px; on main the focus order; in a drill its options, then back).</summary>
    public string Describe()
    {
        if (!IsOpen || _layout is null || _content is null)
            return "open=false view=main buttons=";
        IEnumerable<string> ids = _view!.Drill is { } d ? [.. d.Options.Select(o => o.Id), "back"] : _layout.FocusOrder;
        var buttons = string.Join(';', ids.Where(_layout.Rects.ContainsKey).Select(id =>
        {
            var r = Screen(_layout.Rects[id]);
            return FormattableString.Invariant($"{id}:{r.X + r.Width / 2:0},{r.Y + r.Height / 2:0},{r.Width:0},{r.Height:0}");
        }));
        var card = _card;
        string B(bool b) => b ? "true" : "false";
        return FormattableString.Invariant(
            $"open=true view={View} focus={_focus ?? "none"} caret={B(_chat.HasFocus)} mic={B(_view.MicShown)} hush={B(_content.Hushed)} dark={B(_content.T == Tokens.Dark)} ") +
            $"fact={_content.Fact?.Kind ?? "none"} openMs={OpenMs} shown={B(_visual.Shown)} openEnd={_openEnd} chatShown={B(_chat.Shown)} chatAt={_chatAt} chatRect={_chat.Rect()} chatShows={_chat.Shows} " +
            FormattableString.Invariant($"card={card.X:0},{card.Y:0},{card.Width:0},{card.Height:0} clasp={_moon.X:0},{_moon.Y:0} chat={(long)_chat.EditHwnd} draft={_chat.Text.Length} ") +
            $"line64={Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_content.Line1))} buttons={buttons}";
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"menu error: {e}"); }
    }

    /// <summary>The GPU device was lost: close; surfaces are redrawn next time.</summary>
    public void DeviceLost()
    {
        Close();
        _visual.Redraw();
    }

    public void Dispose() => _visual.Dispose();
}
