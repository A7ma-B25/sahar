using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Windows.System;
using Sahar.Core.Quran;
using Q = Sahar.Core.Quran.Quran;

namespace Sahar.Engine.Tools;

/// <summary>
/// 📖 القرآن (spec §7.1): pick a surah (the last one first) and a reciter; it streams from the islamic.network CDN.
/// Any playing music is paused first; while it plays she is quiet and still with the quran face. The end of the
/// surah, a stop or an error (a gentle line, no retry) bring her back.
/// 0.16 (spec §10; owner rulings 5, 7): pause and go on, ±10 s and seek, the surah before and after, and where he
/// stopped (<see cref="QuranResume"/>: kept on pause, stop, every 15 s and at her exit; used when he asks to go on).
/// Logs the surah number, the reciter id and seconds only.
/// </summary>
internal sealed class QuranTool : IDisposable
{
    public const string Failed = "ما قدرت شغّل التلاوة هلق";

    private readonly DispatcherQueue _ui;
    private readonly ListBubble _list;
    private readonly Func<RectF> _body;
    private readonly Media _media;
    private readonly Action<string> _say;
    private readonly Func<SaharSettings> _get;
    private readonly Action<Func<SaharSettings, SaharSettings>> _set;   // load → change → save (two writers)
    private readonly string? _base;
    private readonly DispatcherQueueTimer _openTimeout;
    private IRecitation? _player;
    private int _generation;
    private readonly DispatcherQueueTimer _keep;
    private bool _resumeWanted;

    public QuranTool(DispatcherQueue ui, ListBubble list, Func<RectF> body, Media media, Action<string> say,
        Func<SaharSettings> get, Action<Func<SaharSettings, SaharSettings>> set, string? baseUrl)
    {
        (_ui, _list, _body, _media, _say, _get, _set, _base) = (ui, list, body, media, say, get, set, baseUrl);
        // A recitation that never opens (a network that hangs) gives up once, gently: no endless "playing".
        _openTimeout = ui.CreateTimer();
        _openTimeout.IsRepeating = false;
        _openTimeout.Interval = TimeSpan.FromSeconds(15);
        _openTimeout.Tick += (_, _) => Safe(() => { if (State == "opening") Fail("timed out"); });
        // Where he is, every 15 s while it plays (ruling 5): a crash or a power cut loses 15 s at most.
        _keep = ui.CreateTimer();
        _keep.Interval = QuranBar.KeepEvery;
        _keep.Tick += (_, _) => Safe(() => { if (State == "playing") Keep(); });
    }

    /// <summary>A recitation is on (opening, playing or paused): she is quiet and wears the quran face.</summary>
    public bool Playing { get; private set; }
    public int Surah { get; private set; }
    public string Reciter { get; private set; } = Q.Reciters[0].Id;
    /// <summary>idle, opening, playing, paused, ended, stopped or failed.</summary>
    public string State { get; private set; } = "idle";
    public event Action<bool>? PlayingChanged;
    /// <summary>Anything the bar shows changed (state, surah, a seek).</summary>
    public event Action? Changed;

    public TimeSpan Position => Try(() => _player?.Position) ?? TimeSpan.Zero;
    public TimeSpan? Duration => Try(() => _player?.Duration);
    public bool CanSeek => Try(() => _player?.CanSeek) ?? false;

    public RecitationState Recitation => State switch
    {
        "opening" => RecitationState.Opening,
        "playing" => RecitationState.Playing,
        "paused" => RecitationState.Paused,
        "ended" => RecitationState.Ended,
        "stopped" => RecitationState.Stopped,
        "failed" => RecitationState.Failed,
        _ => RecitationState.Idle,
    };

    /// <summary>What the bar draws.</summary>
    public QuranBarView View => new(Math.Clamp(Surah, 1, 114), Reciter, Recitation, Position, Duration, CanSeek);

    public void Open()
    {
        var s = _get();
        var lines = new List<string>();
        int offset = 0;
        if (s.QuranLastSurah is int last and >= 1 and <= 114) { lines.Add($"▶ {Line(last)}"); offset = 1; }
        for (int n = 1; n <= 114; n++) lines.Add(Line(n));
        string reciter = s.QuranReciter;
        _list.Show(_body(), "📖 اختار سورة", lines, $"القارئ: {Q.ReciterName(reciter)}",
            i =>
            {
                int surah = offset == 1 && i == 0 ? s.QuranLastSurah!.Value : i - offset + 1;
                if (surah is >= 1 and <= 114) _ui.TryEnqueue(() => Safe(() => Play(surah, Valid(_get().QuranReciter))));
            },
            () => _ui.TryEnqueue(() => Safe(OpenReciters)));
    }

    private static string Line(int n) => $"{n}. {Q.SurahName(n)}";

    private static string Valid(string reciter) => Q.Reciters.Any(r => r.Id == reciter) ? reciter : Q.Reciters[0].Id;

    private void OpenReciters()
    {
        var current = Valid(_get().QuranReciter);
        _list.Show(_body(), "اختار القارئ", Q.Reciters.Select(r => (r.Id == current ? "✓ " : "") + r.Name).ToList(), "رجوع",
            i =>
            {
                if (i < 0 || i >= Q.Reciters.Count) return;
                _set(s => s with { QuranReciter = Q.Reciters[i].Id });
                Log.Write($"quran: reciter {Q.Reciters[i].Id}");
                _ui.TryEnqueue(() => Safe(Open));
            },
            () => _ui.TryEnqueue(() => Safe(Open)));
    }

    /// <param name="make">Test mode: a stand-in player (CI's Windows Server has no MP3 decoder).</param>
    /// <param name="resume">Once it opens, seek to where he stopped in this surah with this reciter (ruling 5). Null (any
    /// pick of the surah, the picker's resume row included): when a place is kept for it — a surah heard to its end
    /// keeps none, so it starts again. False: from the start.</param>
    public void Play(int surah, string reciter, Func<IRecitation>? make = null, bool? resume = null)
    {
        if (State is "playing" or "paused") Keep();
        StopPlayer();
        _make = make;
        int gen = ++_generation;
        Surah = surah;
        Reciter = reciter;
        _resumeWanted = resume ?? QuranBar.ResumeFrom(_get().QuranResume, surah, reciter, null) is not null;
        State = "opening";
        SetPlaying(true);
        Log.Write($"quran: play surah {surah} ({reciter}){(_resumeWanted ? " to go on" : "")}");
        Changed?.Invoke();
        _openTimeout.Stop();
        _openTimeout.Start();
        // Music first: pause whatever plays, then start the recitation.
        _media.PauseAllAsync().ContinueWith(_ => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen != _generation) return;
            try { Start(gen, surah, reciter, make?.Invoke() ?? new MediaRecitation()); }
            catch (Exception e) { Fail(e.GetType().Name); }
        })));
    }

    private void Start(int gen, int surah, string reciter, IRecitation player)
    {
        player.Opened += () => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen != _generation) return;
            _openTimeout.Stop();
            State = "playing";
            _set(s => s with { QuranLastSurah = surah });
            if (_resumeWanted && QuranBar.ResumeFrom(_get().QuranResume, surah, reciter, player.Duration) is { } at && player.CanSeek)
            {
                player.Seek(at);
                Log.Write($"quran: on from {(int)at.TotalSeconds} s");
            }
            _resumeWanted = false;
            _keep.Start();
            Changed?.Invoke();
        }));
        player.Ended += () => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen != _generation) return;
            // Heard to the end: nothing to go on from in this surah.
            if (_get().QuranResume is { } r && r.Surah == surah) _set(s => s with { QuranResume = null });
            Finish("ended");
        }));
        player.Failed += why => _ui.TryEnqueue(() => Safe(() => { if (gen == _generation) Fail(why); }));
        _player = player;
        player.Start(Q.Url(reciter, surah, _base));
    }

    private void Fail(string why)
    {
        Log.Write($"quran: failed ({why})");
        Finish("failed");
        _say(Failed);
    }

    public void Stop()
    {
        if (!Playing) return;
        Log.Write("quran: stopped");
        if (State is "playing" or "paused") Keep();
        Finish("stopped");
    }

    // ---- the bar's controls (0.16) ----

    public void Pause()
    {
        if (State != "playing" || _player is null) return;
        _player.Pause();
        State = "paused";
        _keep.Stop();
        Keep();
        Log.Write("quran: paused");
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (State != "paused" || _player is null) return;
        _player.Resume();
        State = "playing";
        _keep.Start();
        Log.Write("quran: on again");
        Changed?.Invoke();
    }

    public void Toggle()
    {
        if (State == "playing") Pause(); else Resume();
    }

    /// <summary>±10 s (or any step), clamped to the surah; nothing when the stream cannot seek.</summary>
    public void Seek(TimeSpan delta) => SeekTo(QuranBar.Seek(Position, delta, Duration));

    public void SeekTo(TimeSpan to)
    {
        if (State is not ("playing" or "paused") || _player is null || !CanSeek) return;
        _player.Seek(QuranBar.Seek(to, TimeSpan.Zero, Duration));
        Log.Write($"quran: seek to {(int)Position.TotalSeconds} s");
        Changed?.Invoke();
    }

    public void Next()
    {
        if (State is "playing" or "paused" && QuranBar.CanNext(Surah)) Play(Surah + 1, Reciter, _make);
    }

    public void Prev()
    {
        if (State is "playing" or "paused" && QuranBar.CanPrev(Surah)) Play(Surah - 1, Reciter, _make);
    }

    /// <summary>«جرّب كمان مرة» after a failure: the same surah, on from where he was.</summary>
    public void Retry()
    {
        if (State == "failed" && Surah is >= 1 and <= 114) Play(Surah, Reciter, _make, resume: true);
    }

    private Func<IRecitation>? _make;

    /// <summary>SEAM with the look stream's surah picker: its resume row's meta for a surah («وقفت عند ١٢:٠٤»), or null.
    /// (If feat/0.16-look declares this property too, keep one declaration.)</summary>
    public Func<int, string?> ResumeMeta { get; set; } = _ => null;

    /// <summary>«وقفت عند ١٢:٠٤» when a place is kept for this surah with the chosen reciter.</summary>
    public string? ResumeLine(int surah) => QuranBar.ResumeLine(_get().QuranResume, surah, Valid(_get().QuranReciter));

    /// <summary>Where he is now, kept for «كمّل» (nothing in the first seconds).</summary>
    private void Keep()
    {
        if (_player is null || Surah is < 1 or > 114) return;
        var keep = QuranBar.Keep(Surah, Reciter, Position);
        if (keep is null || keep == _get().QuranResume) return;
        _set(s => s with { QuranResume = keep });
    }

    private void Finish(string state)
    {
        _openTimeout.Stop();
        _keep.Stop();
        _generation++;
        StopPlayer();
        State = state;
        SetPlaying(false);
        Changed?.Invoke();
    }

    private void StopPlayer()
    {
        var p = _player;
        _player = null;
        if (p is null) return;
        try { p.Dispose(); }
        catch (Exception e) { Log.Write($"quran: player close failed ({e.GetType().Name})"); }
    }

    private void SetPlaying(bool on)
    {
        if (on == Playing) return;
        Playing = on;
        PlayingChanged?.Invoke(on);
    }

    public string Describe() => FormattableString.Invariant(
        $"playing={(Playing ? "true" : "false")} surah={Surah} state={State} reciter={Reciter} position={Position.TotalSeconds:0.0} duration={Duration?.TotalSeconds ?? -1:0.0} canSeek={(CanSeek ? "true" : "false")} resume={_get().QuranResume?.Surah ?? 0}:{_get().QuranResume?.Seconds ?? 0}");

    private static T? Try<T>(Func<T?> read) where T : struct
    {
        try { return read(); }
        catch (Exception) { return null; }
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"quran error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
    }

    public void Dispose()
    {
        _openTimeout.Stop();
        _keep.Stop();
        // Her exit is the end of the session: where he was is kept (ruling 5).
        try { if (State is "playing" or "paused") Keep(); }
        catch (Exception e) { Log.Write($"quran: keep failed ({e.GetType().Name})"); }
        StopPlayer();
    }
}
