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
    private readonly Action<string> _express;
    private readonly Action<bool> _setHidden;
    private readonly DispatcherQueueTimer _timer;
    private string _lastKey = "idle";
    private bool _hidden, _fullscreen;
    private DateTimeOffset _hiddenUntil;

    public Director(DispatcherQueue ui, Action<string> express, Action<bool> setHidden)
    {
        _express = express;
        _setHidden = setHidden;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => { CheckFullscreen(); Tick(); };
        _timer.Start();
        Foreground.Changed += () => { CheckFullscreen(); Tick(); };
    }

    public bool Hidden => _hidden;

    public void Post(EventKind k)
    {
        _brain.Post(k, DateTimeOffset.UtcNow);
        Tick();
    }

    /// <summary>"Hide 1 h" from the tray.</summary>
    public void HideFor(TimeSpan span)
    {
        _hiddenUntil = DateTimeOffset.UtcNow + span;
        Tick();
    }

    private void CheckFullscreen()
    {
        bool fs = FullscreenWatcher.Check();
        if (fs == _fullscreen) return;
        _fullscreen = fs;
        _brain.SetQuiet(fs ? QuietReason.Fullscreen : QuietReason.None);
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
