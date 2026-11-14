using Sahar.Engine.Diagnostics;
using Sahar.Engine.Menu;
using Sahar.Engine.Tools;
using Windows.System;

namespace Sahar.Engine.Mind;

/// <summary>
/// Her one-tap questions (spec §3.7 "Ask UI"): in the card's header when the card is open (keys 1/2), otherwise in a
/// bubble beside her like the update question. One at a time (others wait their turn); no answer within 2 minutes,
/// a closed card or a dismissed bubble is «لا» — the default never changes anything.
/// </summary>
internal sealed class AskDrill(DispatcherQueue ui, MenuCard menu, ListBubble list, Func<Sahar.Core.Geometry.RectF> body)
{
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(2);
    private readonly Queue<(string Question, Action<bool> Answer)> _waiting = new();
    private DispatcherQueueTimer? _timer;
    private Action<bool>? _current;
    private int _gen;

    /// <summary>"header" or "bubble" while a question is open, else null (tests and debug-mind).</summary>
    public string? Where { get; private set; }
    public string? Text { get; private set; }

    /// <summary>Test mode: how long a question waits (the UI stage shortens it).</summary>
    public TimeSpan Timeout { get; set; } = Wait;

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
        else
        {
            Where = "bubble";
            list.Show(body(), question, ["إي", "لا"], "مش هلق", i => ui.TryEnqueue(() => Done(i == 0)), () => ui.TryEnqueue(() => Done(false)));
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
        else if (list.IsOpen) list.Close();
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
