using Sahar.Core.Brain;
using Sahar.Core.Life;
using Sahar.Engine.Diagnostics;
using Windows.Media.Control;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// Is music or a video playing (any app that reports to Windows' media controls)? Polled every 2 s on the
/// UI thread; music start/stop go to the brain, video only tells her to keep still.
/// </summary>
internal sealed class Media
{
    private readonly MediaEdges _edges = new();
    private readonly Action<EventKind> _post;
    private readonly DispatcherQueueTimer _timer;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private bool _simulated;
    private (bool, bool, bool, bool) _lastLogged;

    public bool VideoPlaying => _edges.VideoPlaying;
    public bool MusicPlaying { get; private set; }

    public Media(DispatcherQueue ui, Action<EventKind> post)
    {
        _post = post;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(2);
        _timer.Tick += (_, _) => Poll();
        try
        {
            GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully) ui.TryEnqueue(() => { _manager = t.Result; _timer.Start(); });
                else Log.Write($"media controls unavailable: {(t.Exception?.GetBaseException() is { } be ? Sahar.Core.Diagnostics.LogRules.Kind(be) : "?")}");
            });
        }
        catch (Exception e)
        {
            Log.Write($"media controls unavailable: {Sahar.Core.Diagnostics.LogRules.Kind(e)}");
        }
    }

    /// <summary>Looks at every media session (not just Windows' "current" one). App ids stay in memory only.</summary>
    private void Poll()
    {
        if (_simulated) return;
        bool music = false, video = false, browser = false;
        try
        {
            if (_manager != null)
                foreach (var session in _manager.GetSessions())
                {
                    var info = session.GetPlaybackInfo();
                    if (info?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                    if (MediaEdges.IsBrowser(session.SourceAppUserModelId ?? "")) browser = true;
                    else if (info.PlaybackType == Windows.Media.MediaPlaybackType.Video) video = true;
                    else music = true;
                }
        }
        catch (Exception e)
        {
            Log.Write($"media poll failed: {Sahar.Core.Diagnostics.LogRules.Kind(e)}");
        }
        Apply(music, video, browser, browser && BrowserInFront());
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

    /// <summary>Pauses every playing session (before the Quran starts).</summary>
    public async Task PauseAllAsync()
    {
        if (_manager is null || _simulated) return;
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
