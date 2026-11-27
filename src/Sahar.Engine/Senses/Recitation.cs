using System.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>One surah playing: opened, ended or failed (raised on any thread). 0.16 (spec §10.2): pause, resume, seek,
/// where it is, how long it is, and whether the stream can seek (the CDN serves byte ranges; if not, ±10 s disable).</summary>
internal interface IRecitation : IDisposable
{
    event Action? Opened;
    event Action? Ended;
    event Action<string>? Failed;
    void Start(Uri url);
    void Pause();
    void Resume();
    void Seek(TimeSpan to);
    TimeSpan Position { get; }
    /// <summary>The surah's length once known.</summary>
    TimeSpan? Duration { get; }
    bool CanSeek { get; }
}

/// <summary>Windows' MediaPlayer, kept out of Windows' media controls (her own music sense would see it and dance).
/// Review I6: its PlaybackSession is never asked on her UI thread (it can hold it for seconds while a stream hangs):
/// place, length and seeking are kept from the session's own events (on its threads), the place carried forward by a
/// clock while it plays; a seek is sent from a worker.</summary>
internal sealed class MediaRecitation : IRecitation
{
    private readonly MediaPlayer _player = new() { AutoPlay = true, Volume = 1.0 };
    private readonly Stopwatch _since = new();
    private readonly object _gate = new();
    private TimeSpan _pos;
    private TimeSpan? _dur;
    private bool _canSeek, _playing;

    public MediaRecitation()
    {
        _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += (_, _) => { Snapshot(); Opened?.Invoke(); };
        _player.MediaEnded += (_, _) => { lock (_gate) _playing = false; Ended?.Invoke(); };
        _player.MediaFailed += (_, e) => Failed?.Invoke(e.Error.ToString());
        var s = _player.PlaybackSession;
        s.PositionChanged += (_, _) => Snapshot();
        s.PlaybackStateChanged += (_, _) => Snapshot();
        s.NaturalDurationChanged += (_, _) => Snapshot();
        s.SeekableRangesChanged += (_, _) => Snapshot();
        s.BufferingEnded += (_, _) => Snapshot();
    }

    /// <summary>On the session's thread: what the UI thread reads.</summary>
    private void Snapshot()
    {
        try
        {
            var s = _player.PlaybackSession;
            var pos = s.Position;
            var dur = s.NaturalDuration;
            bool seek = s.CanSeek, playing = s.PlaybackState == MediaPlaybackState.Playing;
            lock (_gate)
            {
                _pos = pos;
                _dur = dur > TimeSpan.Zero ? dur : null;
                _canSeek = seek;
                _playing = playing;
                _since.Restart();
            }
        }
        catch (Exception) { }   // closing: the last values stay
    }

    public event Action? Opened;
    public event Action? Ended;
    public event Action<string>? Failed;

    public void Start(Uri url) => _player.Source = MediaSource.CreateFromUri(url);
    public void Pause() { lock (_gate) { _pos = Position; _playing = false; } _player.Pause(); }
    public void Resume() => _player.Play();

    public void Seek(TimeSpan to)
    {
        lock (_gate) { if (!_canSeek) return; _pos = to; _since.Restart(); }
        _ = Task.Run(() =>
        {
            try { var s = _player.PlaybackSession; if (s.CanSeek) s.Position = to; }
            catch (Exception) { }
        });
    }

    public TimeSpan Position
    {
        get
        {
            lock (_gate)
            {
                var p = _playing ? _pos + _since.Elapsed : _pos;
                return _dur is { } d && p > d ? d : p;
            }
        }
    }

    public TimeSpan? Duration { get { lock (_gate) return _dur; } }
    public bool CanSeek { get { lock (_gate) return _canSeek; } }

    /// <summary>Closed from a worker, like a seek (review I6): a stream that failed or hangs can hold Pause, Source and
    /// Dispose for seconds — on her UI thread that froze her (CI: «no network» with a refused port, her pipe silent
    /// past 5 s, runs 44771580600 and 45882237735). Its late events find a newer generation and are dropped.</summary>
    public void Dispose()
    {
        var p = _player;
        _ = Task.Run(() =>
        {
            try { p.Pause(); p.Source = null; p.Dispose(); }
            catch (Exception) { }   // closing: nothing left to tell
        });
    }
}

/// <summary>Test mode: "plays" for a set time on a clock that pauses and seeks, so CI (no MP3 decoder on Windows Server)
/// can check her behaviour and the bar. <paramref name="canSeek"/> false: a stream that cannot seek; <paramref name="opening"/>:
/// how long it takes to open (300 ms by default; longer to see the bar's «عم تفتح…»).</summary>
internal sealed class FakeRecitation(DispatcherQueue ui, TimeSpan length, bool canSeek = true, TimeSpan? opening = null) : IRecitation
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _base;
    private bool _opened, _ended;
    private DispatcherQueueTimer? _open, _tick;

    public event Action? Opened;
    public event Action? Ended;
    /// <summary>A stand-in never fails; the network failure is tested with the real player.</summary>
    public event Action<string>? Failed { add { } remove { } }

    public void Start(Uri url)
    {
        _open = ui.CreateTimer();
        _open.IsRepeating = false;
        _open.Interval = opening ?? TimeSpan.FromMilliseconds(300);
        _open.Tick += (_, _) => { _opened = true; _clock.Restart(); Opened?.Invoke(); };
        _open.Start();
        _tick = ui.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(200);
        _tick.Tick += (_, _) =>
        {
            if (_ended || !_opened || Position < length) return;
            _ended = true;
            _clock.Stop();
            _tick.Stop();
            Ended?.Invoke();
        };
        _tick.Start();
    }

    public void Pause()
    {
        _base = Position;
        _clock.Reset();
    }

    public void Resume()
    {
        if (_opened && !_clock.IsRunning) _clock.Restart();
    }

    public void Seek(TimeSpan to)
    {
        if (!canSeek) return;
        bool running = _clock.IsRunning;
        _base = to < TimeSpan.Zero ? TimeSpan.Zero : to > length ? length : to;
        _clock.Reset();
        if (running) _clock.Start();
    }

    public TimeSpan Position
    {
        get
        {
            var p = _base + _clock.Elapsed;
            return p > length ? length : p;
        }
    }

    public TimeSpan? Duration => _opened ? length : null;
    public bool CanSeek => canSeek;

    public void Dispose()
    {
        _open?.Stop();
        _tick?.Stop();
        _clock.Stop();
    }
}
