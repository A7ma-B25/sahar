using Sahar.Core.Brain;
using Sahar.Engine.Diagnostics;
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
    private bool _hidden, _fullscreen, _dnd, _cameraMic, _adhan, _quran;
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

    /// <summary>The camera or microphone is in use: quiet (no comments, no sounds).</summary>
    public void SetCameraMic(bool on)
    {
        _cameraMic = on;
        ApplyQuiet();
    }

    /// <summary>The minutes after a prayer's time: quiet (no comments, no sounds, small things only).</summary>
    public void SetAdhan(bool on)
    {
        if (on == _adhan) return;
        _adhan = on;
        ApplyQuiet();
    }

    public bool Adhan => _adhan;

    /// <summary>The Quran is playing: quiet, and her resting face is the quran face (events still show above it).</summary>
    public void SetQuran(bool on)
    {
        if (on == _quran) return;
        _quran = on;
        ApplyQuiet();
        Tick();
    }

    public bool Quran => _quran;

    public bool Quiet => _fullscreen || _dnd || _cameraMic || _adhan || _quran;

    private void ApplyQuiet() =>
        _brain.SetQuiet((_fullscreen ? QuietReason.Fullscreen : QuietReason.None) | (_dnd ? QuietReason.DoNotDisturb : QuietReason.None) |
            (_cameraMic ? QuietReason.CameraOrMic : QuietReason.None) | (_adhan ? QuietReason.Adhan : QuietReason.None) |
        (_quran ? QuietReason.Quran : QuietReason.None));

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
        // Why she hid: the window class only (never a title), so "she vanished over X" can be traced.
        Log.Write(fs ? $"fullscreen: hiding ({FullscreenWatcher.ForegroundClass()})" : "fullscreen: back");
        ApplyQuiet();
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        var output = _brain.Tick(now);
        var key = QuranFace.Over(output.ExpressionKey, _quran);
        if (key != _lastKey)
        {
            _lastKey = key;
            _express(key);
        }
        bool hidden = output.Hidden || now < _hiddenUntil;
        if (hidden != _hidden)
        {
            _hidden = hidden;
            _setHidden(hidden);
        }
    }
}
