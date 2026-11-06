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
                else Log.Write($"media controls unavailable: {t.Exception?.GetBaseException().Message}");
            });
        }
        catch (Exception e)
        {
            Log.Write($"media controls unavailable: {e.Message}");
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
            Log.Write($"media poll failed: {e.Message}");
        }
        Apply(music, video, browser, browser && BrowserInFront());
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
