using Sahar.Core.Activities;
using Sahar.Core.Art;
using Sahar.Core.Life;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.System;

namespace Sahar.Engine.Activities;

/// <summary>
/// Music (spec 0.13 §2): Windows' media sessions by their events (sessions, properties, playback, timeline; WinRT
/// calls off the UI thread), the most recently playing one that is not a video, its cover decoded at 64×64 for the tag
/// and the halo colour, the card's controls, and lyrics only after you said yes. Logs say "playing / paused / none".
/// </summary>
internal sealed class MediaSource
{
    private readonly DispatcherQueue _ui;
    private readonly ActivityHub _hub;
    private readonly Func<SaharSettings> _settings;
    private readonly Action<Func<SaharSettings, SaharSettings>> _save;
    private readonly string _lyricsDir;
    private readonly Dictionary<string, DateTimeOffset> _lastPlaying = [];
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _hooked = [];
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private Song? _song;
    private string? _logged;
    private LyricsView _lyricsView = LyricsView.Off;
    private IReadOnlyList<(TimeSpan At, string Line)> _lines = [];
    private string? _lyricsFor;
    private int _refreshGen;

    private sealed record Song(string Key, string Title, string Artist, string Album, string App, Pixels? Cover, Rgba? Colour, bool Playing,
        TimeSpan Position, DateTimeOffset Updated, TimeSpan End, DateTimeOffset? PausedAt);

    public MediaSource(DispatcherQueue ui, ActivityHub hub, Func<SaharSettings> settings, Action<Func<SaharSettings, SaharSettings>> save, string lyricsDir)
    {
        _ui = ui;
        _hub = hub;
        _settings = settings;
        _save = save;
        _lyricsDir = lyricsDir;
        hub.Pressed += (id, a) => { if (a?.Id == "music" || id.StartsWith("music-", StringComparison.Ordinal) || id.StartsWith("lyrics", StringComparison.Ordinal)) Press(id); };
        hub.CoverOf = a => a.Id == "music" && _song is { } s ? (s.Cover, s.Colour) : (null, null);
        var t = ui.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1);
        t.Tick += (_, _) => Safe(Tick);
        t.Start();
        try
        {
            GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().ContinueWith(task =>
            {
                if (!task.IsCompletedSuccessfully) { Log.Write("music: media controls unavailable"); return; }
                _manager = task.Result;
                _manager.SessionsChanged += (_, _) => Changed();
                _manager.CurrentSessionChanged += (_, _) => Changed();
                Changed();
            }, TaskScheduler.Default);
        }
        catch (Exception e) { Log.Write($"music: media controls unavailable ({e.GetType().Name})"); }
    }

    /// <summary>Music is playing now (her headphones and sway, 0.13 §1.5).</summary>
    public bool Playing => _song is { Playing: true };
    /// <summary>The cover's colour, for her halo (null without a cover).</summary>
    public Rgba? Colour => _song?.Colour;
    public event Action? PlayingChanged;

    private void Changed() => Task.Run(RefreshAsync);

    /// <summary>Off the UI thread: every session's state, the pick, and the picked one's song.</summary>
    private async Task RefreshAsync()
    {
        int gen = Interlocked.Increment(ref _refreshGen);
        try
        {
            if (_manager is null) return;
            var sessions = _manager.GetSessions().ToList();
            var infos = new List<SessionInfo>();
            var now = DateTimeOffset.Now;
            foreach (var s in sessions)
            {
                Hook(s);
                var info = s.GetPlaybackInfo();
                string app = s.SourceAppUserModelId ?? "";
                bool playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                lock (_lastPlaying) { if (playing) _lastPlaying[app] = now; }
                DateTimeOffset last;
                lock (_lastPlaying) last = _lastPlaying.GetValueOrDefault(app, DateTimeOffset.MinValue);
                infos.Add(new SessionInfo(app, playing, info?.PlaybackType == Windows.Media.MediaPlaybackType.Video, MediaEdges.IsBrowser(app), last));
            }
            int pick = MediaNow.Pick(infos);
            if (pick < 0) { _ui.TryEnqueue(() => Safe(() => Apply(null, null))); return; }
            var session = sessions[pick];
            var props = await session.TryGetMediaPropertiesAsync().AsTask();
            var timeline = session.GetTimelineProperties();
            string title = props?.Title ?? "", artist = props?.Artist ?? "", album = props?.AlbumTitle ?? "";
            string key = $"{infos[pick].App}\n{artist}\n{title}";
            Pixels? cover = _song?.Key == key ? _song.Cover : null;
            Rgba? colour = _song?.Key == key ? _song.Colour : null;
            if (cover is null && props?.Thumbnail is { } thumb)
            {
                try
                {
                    using var stream = await thumb.OpenReadAsync().AsTask();
                    var decoder = await BitmapDecoder.CreateAsync(stream).AsTask();
                    var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        new BitmapTransform { ScaledWidth = 64, ScaledHeight = 64, InterpolationMode = BitmapInterpolationMode.Fant },
                        ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask();
                    var bytes = data.DetachPixelData();
                    cover = new Pixels(bytes, 64, 64);
                    colour = MediaNow.CoverColourOrNull(bytes);
                }
                catch (Exception e) { Log.Write($"music: cover not read ({e.GetType().Name})"); }
            }
            bool isPlaying = infos[pick].Playing;
            var song = new Song(key, title, artist, album, MediaNow.AppLabel(infos[pick].App), cover, colour, isPlaying,
                timeline?.Position ?? TimeSpan.Zero, timeline?.LastUpdatedTime ?? now, timeline?.EndTime ?? TimeSpan.Zero, null);
            if (gen != Volatile.Read(ref _refreshGen)) return;   // a newer refresh is on its way
            _ui.TryEnqueue(() => Safe(() => Apply(session, song)));
        }
        catch (Exception e) { Log.Write($"music: refresh failed ({e.GetType().Name})"); }
    }

    private void Hook(GlobalSystemMediaTransportControlsSession s)
    {
        lock (_hooked)
        {
            if (!_hooked.Add(s)) return;
        }
        s.MediaPropertiesChanged += (_, _) => Changed();
        s.PlaybackInfoChanged += (_, _) => Changed();
        s.TimelinePropertiesChanged += (_, _) => Changed();
    }

    private void Apply(GlobalSystemMediaTransportControlsSession? session, Song? song)
    {
        bool wasPlaying = Playing;
        if (song is not null && _song is { } old && old.Key == song.Key && !song.Playing)
            song = song with { PausedAt = old.PausedAt ?? (old.Playing ? DateTimeOffset.Now : old.PausedAt) };
        else if (song is { Playing: false, PausedAt: null }) song = song with { PausedAt = DateTimeOffset.Now };
        _session = session;
        _song = song is { Title.Length: > 0 } ? song : null;
        var state = _song is null ? "none" : _song.Playing ? "playing" : "paused";
        if (state != _logged) { _logged = state; Log.Write($"music: session {state}"); }
        if (_song?.Key != _lyricsFor) { _lines = []; _lyricsFor = null; if (_lyricsView != LyricsView.Asking) _lyricsView = LyricsView.Off; }
        Show();
        if (Playing != wasPlaying) PlayingChanged?.Invoke();
    }

    private void Show()
    {
        if (_song is not { } s) { _hub.Remove("music"); return; }
        var now = DateTimeOffset.Now;
        if (!s.Playing && s.PausedAt is { } at && !MediaNow.KeepPaused(at, now, _settings().MusicPausedKeep)) { _hub.Remove("music"); return; }
        var pos = MediaNow.Position(s.Position, s.Updated, now, s.Playing, s.End);
        float? progress = s.End > TimeSpan.Zero ? (float)(pos / s.End) : null;
        var a = s.Playing
            ? new Activity("music", ActivityKind.Music, s.Title, null, "notes", progress)
            : new Activity("music", ActivityKind.MusicPaused, s.Title, ActivityText.Paused, "notes");
        _hub.Set(a, Card);
    }

    private CardData? Card()
    {
        if (_song is not { } s) return null;
        var now = DateTimeOffset.Now;
        var pos = MediaNow.Position(s.Position, s.Updated, now, s.Playing, s.End);
        var (prev, line) = _lyricsView == LyricsView.Lines ? Lrc.At(_lines, pos) : (null, null);
        return new CardData(CardKind.Music, Music: new MusicCard(s.Title, s.Artist, s.App, s.Cover, s.Colour, s.Playing, pos, s.End, _lyricsView, prev, line));
    }

    private void Tick()
    {
        if (_song is null) return;
        Show();
        _hub.CardChanged();
    }

    private void Press(string id)
    {
        var s = _settings();
        switch (id)
        {
            case "music-prev": Control(x => x.TrySkipPreviousAsync()); break;
            case "music-next": Control(x => x.TrySkipNextAsync()); break;
            case "music-toggle": Control(x => x.TryTogglePlayPauseAsync()); break;
            case "lyrics":
                if (_lyricsView is LyricsView.Lines or LyricsView.Loading or LyricsView.None) _lyricsView = LyricsView.Off;
                else if (!s.LyricsConsented) _lyricsView = LyricsView.Asking;   // the inline question first (spec 0.13 §2)
                else
                {
                    if (!s.LyricsEnabled) _save(x => x with { LyricsEnabled = true });
                    Fetch();
                }
                break;
            case "lyrics-yes":
                _save(x => x with { LyricsEnabled = true, LyricsConsented = true });
                Log.Write("music: lyrics allowed");
                Fetch();
                break;
            case "lyrics-no":
                _lyricsView = LyricsView.Off;
                break;
        }
        _hub.CardChanged();
    }

    private void Control(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> op)
    {
        if (_session is not { } session) return;
        Task.Run(async () =>
        {
            try { await op(session).AsTask(); }
            catch (Exception e) { Log.Write($"music: control failed ({e.GetType().Name})"); }
        });
    }

    private void Fetch()
    {
        if (_song is not { } song) return;
        var s = _settings();   // read after the save above: the client still refuses without both switches
        _lyricsView = LyricsView.Loading;
        _lyricsFor = song.Key;
        var client = new LyricsClient(new HttpClientHandler(), _lyricsDir);
        client.GetAsync(song.Title, song.Artist, song.Album, song.End, s.LyricsEnabled, s.LyricsConsented).ContinueWith(t =>
        {
            _ui.TryEnqueue(() => Safe(() =>
            {
                if (_lyricsFor != song.Key) return;
                var r = t.IsCompletedSuccessfully ? t.Result : LyricsResult.Of(LyricsStatus.Failed);
                Log.Write($"music: lyrics {r.Status}");
                _lines = r.Lines;
                _lyricsView = r.Status == LyricsStatus.Found ? LyricsView.Lines : LyricsView.None;
                _hub.CardChanged();
            }));
        }, TaskScheduler.Default);
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"music error: {e.GetType().Name}"); }
    }
}
