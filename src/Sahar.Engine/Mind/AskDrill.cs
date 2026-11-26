using Sahar.Engine.Diagnostics;
using Sahar.Engine.Menu;
using Sahar.Engine.Surfaces;
using Windows.System;

namespace Sahar.Engine.Mind;

/// <summary>
/// Her one-tap questions (spec §3.7 "Ask UI"): in the card's header when the card is open (keys 1/2), otherwise in a
/// bubble above her (V15Lists «عقلها بيسأل»), or inline in the chat while the chat window is open (0.16). One at a time (others wait their turn); no answer within 2 minutes,
/// a closed card or a dismissed bubble is «لا» — the default never changes anything.
/// </summary>
internal sealed class AskDrill(DispatcherQueue ui, MenuCard menu, Panel panel)
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(2);
    private readonly Queue<(string Question, Action<bool> Answer)> _waiting = new();
    private DispatcherQueueTimer? _timer;
    private Action<bool>? _current;
    private int _gen;

    /// <summary>0.16: the chat window is open — the question goes into it (inline «إي» / «لا»); returns false when not open.
    /// The chat calls the answer back itself.</summary>
    public Func<string, Action<bool>, bool>? InChat { get; set; }

    /// <summary>"header", "bubble" or "chat" while a question is open, else null (tests and debug-mind).</summary>
    public string? Where { get; private set; }
    public string? Text { get; private set; }

    /// <summary>Test mode: how long a question waits (the UI stage shortens it).</summary>
    public TimeSpan Timeout { get; set; } = Wait;

    /// <summary>The chat shows «ما جاوبت، فما عملت شي» for a timeout, or collapses the buttons.</summary>
    public Action<bool>? TimedOutInChat { get; set; }

    public void Ask(string question, Action<bool> answer)
    {
        if (_current is not null) { _waiting.Enqueue((question, answer)); return; }
        Show(question, answer);
    }

    private void Show(string question, Action<bool> answer)
    {
        int gen = ++_gen;
        _current = answer;
        Text = question;
        void Done(bool yes)
        {
            if (gen != _gen || _current is null) return;
            Finish(yes);
        }
        if (menu.IsOpen)
        {
            Where = "header";
            menu.MindAsk(question, Done);
        }
        else if (InChat is { } chat && chat(question, yes => ui.TryEnqueue(() => Done(yes))))
        {
            Where = "chat";
        }
        else
        {
            Where = "bubble";
            Cards.Ask(panel, "ask", question, "إي", "لا", yes => ui.TryEnqueue(() => Done(yes)));
        }
        _timer ??= ui.CreateTimer();
        _timer.Stop();
        _timer.IsRepeating = false;
        _timer.Interval = Timeout;
        _timer.Tick += OnTimeout;
        _timer.Start();
        Log.Write($"mind: ask ({Where})");
    }

    private void OnTimeout(DispatcherQueueTimer t, object o)
    {
        t.Tick -= OnTimeout;
        if (_current is null) return;
        Log.Write("mind: ask timed out (no)");
        Finish(false, timedOut: true);
    }

    private void Finish(bool yes, bool timedOut = false)
    {
        var answer = _current!;
        _current = null;
        if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnTimeout; }
        if (Where == "header") menu.MindAskEnd();
        else if (Where == "bubble" && panel.Name == "ask") panel.Done();
        else if (Where == "chat") TimedOutInChat?.Invoke(timedOut);
        Where = null;
        Text = null;
        _gen++;
        Log.Write(yes ? "mind: ask answered yes" : "mind: ask answered no");
        try { answer(yes); }
        catch (Exception e) { Log.Write($"mind: ask error {e.GetType().Name}"); }
        if (_waiting.Count > 0)
        {
            var (q, a) = _waiting.Dequeue();
            Show(q, a);
        }
    }
}
