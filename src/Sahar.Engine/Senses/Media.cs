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

    private void Poll()
    {
        bool playing = false, video = false;
        try
        {
            var info = _manager?.GetCurrentSession()?.GetPlaybackInfo();
            playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            video = info?.PlaybackType == Windows.Media.MediaPlaybackType.Video;
        }
        catch (Exception e)
        {
            Log.Write($"media poll failed: {e.Message}");
        }
        foreach (var e in _edges.Update(playing, video, fromBrowser: false, browserInFront: false)) _post(e);
        MusicPlaying = playing && !video;
    }
}
