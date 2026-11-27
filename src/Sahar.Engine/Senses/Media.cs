using Sahar.Core.Brain;
using Sahar.Core.Life;
using Sahar.Engine.Diagnostics;
using Windows.Media.Control;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>0.17.2: Windows' media session manager, asked for once and shared (her music sense and this one had two).</summary>
internal static class MediaSessions
{
    public static readonly Task<GlobalSystemMediaTransportControlsSessionManager?> Manager = Request();

    private static async Task<GlobalSystemMediaTransportControlsSessionManager?> Request()
    {
        try { return await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ConfigureAwait(false); }
        catch (Exception e)
        {
            Log.Write($"media controls unavailable: {Sahar.Core.Diagnostics.LogRules.Kind(e)}");
            return null;
        }
    }
}

/// <summary>
/// Is music or a video playing (any app that reports to Windows' media controls)? 0.17.2 (owner: other apps lagged):
/// no poll on her UI thread any more (GetSessions + GetPlaybackInfo every 2 s were synchronous cross-process calls);
/// the flags come from her music sense's event-driven refresh (off the UI thread, <see cref="Feed"/>), and «the
/// browser in front» is looked at again when the foreground changes. Music start/stop go to the brain, video only
/// tells her to keep still.
/// </summary>
internal sealed class Media
{
    private readonly MediaEdges _edges = new();
    private readonly Action<EventKind> _post;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _simulated;
    private (bool Music, bool Video, bool Browser) _seen;
    private (bool, bool, bool, bool) _lastLogged;

    public bool VideoPlaying => _edges.VideoPlaying;
    public bool MusicPlaying { get; private set; }

    public Media(DispatcherQueue ui, Action<EventKind> post)
    {
        _post = post;
        MediaSessions.Manager.ContinueWith(t => { if (t.Result is { } m) ui.TryEnqueue(() => _manager = m); }, TaskScheduler.Default);
        Foreground.Changed += () => { if (!_simulated && _seen.Browser) Safe(() => Apply(_seen.Music, _seen.Video, true, BrowserInFront())); };
    }

    /// <summary>On the UI thread: what the sessions say now (from MediaSource's refresh). App ids never reach here.</summary>
    public void Feed(bool music, bool video, bool browser)
    {
        if (_simulated) return;
        _seen = (music, video, browser);
        Apply(music, video, browser, browser && BrowserInFront());
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"media error: {Sahar.Core.Diagnostics.LogRules.Kind(e)}"); }
    }

    /// <summary>The session the 🎵 controls act on: one that is playing, else Windows' current one.</summary>
    private GlobalSystemMediaTransportControlsSession? Session()
    {
        if (_manager is null || _simulated) return null;
        try
        {
            foreach (var session in _manager.GetSessions())
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return session;
            return _manager.GetCurrentSession();
        }
        catch (Exception e)
        {
            Log.Write($"media session failed: {Sahar.Core.Diagnostics.LogRules.Kind(e)}");
            return null;
        }
    }

    public bool HasSession => Session() is not null;

    /// <summary>The title and artist of what plays (shown in her bubble, never logged); null with nothing to show.</summary>
    public async Task<(string Title, string Artist)?> NowPlayingAsync()
    {
        if (Session() is not { } session) return null;
        try
        {
            var p = await session.TryGetMediaPropertiesAsync().AsTask();
            return string.IsNullOrWhiteSpace(p?.Title) ? null : (p.Title, p.Artist ?? "");
        }
        catch (Exception e)
        {
            Log.Write($"media properties failed: {e.GetType().Name}");
            return null;
        }
    }

    /// <summary>⏮️ ⏯️ ⏭️ on the session; false when there is none or the app refuses.</summary>
    public async Task<bool> ControlAsync(MusicControl control)
    {
        if (Session() is not { } session) return false;
        try
        {
            return control switch
            {
                MusicControl.Previous => await session.TrySkipPreviousAsync().AsTask(),
                MusicControl.Next => await session.TrySkipNextAsync().AsTask(),
                _ => await session.TryTogglePlayPauseAsync().AsTask(),
            };
        }
        catch (Exception e)
        {
            Log.Write($"media control failed: {e.GetType().Name}");
            return false;
        }
    }

    /// <summary>Pauses every playing session (before the Quran starts). 0.17.2: off her UI thread (GetSessions and
    /// GetPlaybackInfo are synchronous cross-process calls).</summary>
    public Task PauseAllAsync()
    {
        var manager = _manager;
        if (manager is null || _simulated) return Task.CompletedTask;
        return Task.Run(() => PauseAll(manager));
    }

    private static async Task PauseAll(GlobalSystemMediaTransportControlsSessionManager _manager)
    {
        try
        {
            foreach (var session in _manager.GetSessions())
                if (session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await session.TryPauseAsync().AsTask();
        }
        catch (Exception e)
        {
            Log.Write($"media pause failed: {e.GetType().Name}");
        }
    }

    /// <summary>Test mode: pretend something is playing (real sessions are ignored from then on).</summary>
    public void Simulate(bool playing, bool video, bool browser, bool browserInFront)
    {
        _simulated = true;
        Apply(playing && !video && !browser, playing && video && !browser, playing && browser, browserInFront);
    }

    private void Apply(bool music, bool video, bool browser, bool browserInFront)
    {
        var now = (music, video, browser, browserInFront);
        if (now != _lastLogged)
        {
            _lastLogged = now;
            // Flags only: no app names or titles in the log.
            Log.Write(music || video || browser
                ? $"media: playing{(music ? " music" : "")}{(video ? " video" : "")}{(browser ? " browser" : "")}{(browser ? (browserInFront ? " (browser in front)" : " (browser behind)") : "")}"
                : "media: nothing playing");
        }
        var events = music ? _edges.Update(true, false, false, false)
            : video ? _edges.Update(true, true, false, false)
            : _edges.Update(browser, false, browser, browserInFront);
        foreach (var e in events) _post(e);
        MusicPlaying = music || browser;
    }

    /// <summary>A web browser in front. Checked by its program, not its window class: Electron apps (VS Code,
    /// Discord, Slack…) share Chromium's class but are not browsers.</summary>
    private static bool BrowserInFront() => MediaEdges.IsBrowserProcess(Native.WindowProcessPath(Native.GetForegroundWindow()));
}

internal enum MusicControl { Previous, Toggle, Next }
