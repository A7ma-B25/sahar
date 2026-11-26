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

/// <summary>Windows' MediaPlayer, kept out of Windows' media controls (her own music sense would see it and dance).</summary>
internal sealed class MediaRecitation : IRecitation
{
    private readonly MediaPlayer _player = new() { AutoPlay = true, Volume = 1.0 };

    public MediaRecitation()
    {
        _player.CommandManager.IsEnabled = false;
        _player.MediaOpened += (_, _) => Opened?.Invoke();
        _player.MediaEnded += (_, _) => Ended?.Invoke();
        _player.MediaFailed += (_, e) => Failed?.Invoke(e.Error.ToString());
    }

    public event Action? Opened;
    public event Action? Ended;
    public event Action<string>? Failed;

    public void Start(Uri url) => _player.Source = MediaSource.CreateFromUri(url);
    public void Pause() => _player.Pause();
    public void Resume() => _player.Play();

    public void Seek(TimeSpan to)
    {
        var s = _player.PlaybackSession;
        if (s.CanSeek) s.Position = to;
    }

    public TimeSpan Position => _player.PlaybackSession.Position;
    public TimeSpan? Duration => _player.PlaybackSession.NaturalDuration is var d && d > TimeSpan.Zero ? d : null;
    public bool CanSeek => _player.PlaybackSession.CanSeek;

    public void Dispose()
    {
        _player.Pause();
        _player.Source = null;
        _player.Dispose();
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
