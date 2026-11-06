using Sahar.Core.Brain;
using Sahar.Engine.Senses;
using Windows.System;
using BrainCore = Sahar.Core.Brain.Brain;

namespace Sahar.Engine.Host;

/// <summary>
/// Ties senses to the brain and the brain to what she shows. Ticks every 250 ms and on every event;
/// plays the brain's expression when it changes and hides her when something is fullscreen.
/// </summary>
internal sealed class Director
{
    private readonly BrainCore _brain = new();
    private readonly FullscreenDebounce _debounce = new();
    private readonly Action<string> _express;
    private readonly Action<bool> _setHidden;
    private readonly DispatcherQueueTimer _timer;
    private string _lastKey = "idle";
    private bool _hidden, _fullscreen, _dnd;
    private DateTimeOffset _hiddenUntil;

    public Director(DispatcherQueue ui, Action<string> express, Action<bool> setHidden)
    {
        _express = express;
        _setHidden = setHidden;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => Safe(() => { CheckFullscreen(); Tick(); });
        _timer.Start();
        Foreground.Changed += () => Safe(() => { CheckFullscreen(); Tick(); });
    }

    public bool Hidden => _hidden;
    /// <summary>The expression the brain wants right now (what transient expressions return to).</summary>
    public string CurrentKey => _lastKey;

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Diagnostics.Log.Write($"director error: {e}"); }
    }

    public void Post(EventKind k) => Post(k, null);

    public void Post(EventKind k, TimeSpan? ttl)
    {
        _brain.Post(k, DateTimeOffset.UtcNow, ttl);
        Tick();
    }

    /// <summary>Ends an event now (a poke dismisses the timer's face).</summary>
    public void End(EventKind k)
    {
        _brain.End(k);
        Tick();
    }

    /// <summary>Her own "do not disturb" (quiet, alongside fullscreen).</summary>
    public void SetDnd(bool on)
    {
        _dnd = on;
        ApplyQuiet();
    }

    private void ApplyQuiet() =>
        _brain.SetQuiet((_fullscreen ? QuietReason.Fullscreen : QuietReason.None) | (_dnd ? QuietReason.DoNotDisturb : QuietReason.None));

    /// <summary>"Hide 1 h" from the tray; <see cref="TimeSpan.Zero"/> shows her again (tray click).</summary>
    public void HideFor(TimeSpan span)
    {
        _hiddenUntil = DateTimeOffset.UtcNow + span;
        Tick();
    }

    private void CheckFullscreen()
    {
        bool fs = _debounce.Update(FullscreenWatcher.Check(), DateTimeOffset.UtcNow);
        if (fs == _fullscreen) return;
        _fullscreen = fs;
        ApplyQuiet();
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        var output = _brain.Tick(now);
        if (output.ExpressionKey != _lastKey)
        {
            _lastKey = output.ExpressionKey;
            _express(output.ExpressionKey);
        }
        bool hidden = output.Hidden || now < _hiddenUntil;
        if (hidden != _hidden)
        {
            _hidden = hidden;
            _setHidden(hidden);
        }
    }
}
