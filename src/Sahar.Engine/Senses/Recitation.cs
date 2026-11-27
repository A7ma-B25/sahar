using System.Collections.Concurrent;
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
/// 0.17.2 (run 46045090894: «quran: play surah 1», then her UI thread blocked ~5 s and debug-quran timed out): the
/// player lives on its OWN thread (one MTA worker per recitation, serialized): it is created there and every call —
/// the source, play, pause, seek, the session's reads, close — runs there in order. Her UI thread only posts commands
/// and reads the place, length and seeking kept from the session's events (review I6), the place carried forward by a
/// clock while it plays. A call that hangs (a stream that never opens) holds only this worker.</summary>
internal sealed class MediaRecitation : IRecitation
{
    private readonly BlockingCollection<(Action Job, bool Fatal)> _jobs = new();
    private MediaPlayer? _player;   // only on the worker
    private readonly Stopwatch _since = new();
    private readonly object _gate = new();
    private TimeSpan _pos;
    private TimeSpan? _dur;
    private bool _canSeek, _playing;

    public MediaRecitation()
    {
        var t = new Thread(Run) { IsBackground = true, Name = "sahar-recitation" };
        t.SetApartmentState(ApartmentState.MTA);   // Windows.Media.Playback is agile: an MTA thread with no message loop
        t.Start();
    }

    private void Run()
    {
        foreach (var (job, fatal) in _jobs.GetConsumingEnumerable())
        {
            try { job(); }
            catch (Exception e) { if (fatal) Failed?.Invoke(e.GetType().Name); }   // creating or opening: tell her; else closing
        }
    }

    private void Post(Action job, bool fatal = false)
    {
        try { _jobs.Add((job, fatal)); }
        catch (InvalidOperationException) { }   // closed: nothing more to do
    }

    private void Create()
    {
        var p = new MediaPlayer { AutoPlay = true, Volume = 1.0 };
        p.CommandManager.IsEnabled = false;
        p.MediaOpened += (_, _) => Post(() => { Snapshot(); Opened?.Invoke(); });   // the length is known before she hears it opened
        p.MediaEnded += (_, _) => { lock (_gate) _playing = false; Ended?.Invoke(); };
        p.MediaFailed += (_, e) => Failed?.Invoke(e.Error.ToString());
        var s = p.PlaybackSession;
        s.PositionChanged += (_, _) => Post(Snapshot);
        s.PlaybackStateChanged += (_, _) => Post(Snapshot);
        s.NaturalDurationChanged += (_, _) => Post(Snapshot);
        s.SeekableRangesChanged += (_, _) => Post(Snapshot);
        s.BufferingEnded += (_, _) => Post(Snapshot);
        _player = p;
    }

    /// <summary>On the worker: what the UI thread reads.</summary>
    private void Snapshot()
    {
        if (_player is not { } p) return;
        try
        {
            var s = p.PlaybackSession;
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

    /// <summary>The player is made here, on the worker, once her handlers are on (a failure to make it is told).</summary>
    public void Start(Uri url) => Post(() =>
    {
        if (_player is null) Create();
        _player!.Source = MediaSource.CreateFromUri(url);
    }, fatal: true);
    public void Pause()
    {
        lock (_gate) { _pos = Position; _playing = false; }
        Post(() => _player?.Pause());
    }
    public void Resume() => Post(() => _player?.Play());

    public void Seek(TimeSpan to)
    {
        lock (_gate) { if (!_canSeek) return; _pos = to; _since.Restart(); }
        Post(() => { if (_player?.PlaybackSession is { CanSeek: true } s) s.Position = to; });
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

    /// <summary>Closed on the worker, after whatever it is doing (review I6: a stream that failed or hangs can hold
    /// Pause, Source and Dispose for seconds). Then the worker ends. Its late events find a newer generation and are dropped.</summary>
    public void Dispose()
    {
        Post(() =>
        {
            if (_player is not { } p) return;
            _player = null;
            p.Pause();
            p.Source = null;
            p.Dispose();
        });
        _jobs.CompleteAdding();
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
