using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Q = Sahar.Core.Quran.Quran;

namespace Sahar.Engine.Tools;

/// <summary>
/// 📖 القرآن (spec §7.1): pick a surah (the last one first) and a reciter; it streams from the islamic.network CDN.
/// Any playing music is paused first; while it plays she is quiet and still with the quran face. The end of the
/// surah, a stop or an error (a gentle line, no retry) bring her back.
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
    private readonly Action<SaharSettings> _set;
    private readonly string? _base;
    private readonly DispatcherQueueTimer _openTimeout;
    private MediaPlayer? _player;
    private int _generation;

    public QuranTool(DispatcherQueue ui, ListBubble list, Func<RectF> body, Media media, Action<string> say,
        Func<SaharSettings> get, Action<SaharSettings> set, string? baseUrl)
    {
        (_ui, _list, _body, _media, _say, _get, _set, _base) = (ui, list, body, media, say, get, set, baseUrl);
        // A recitation that never opens (a network that hangs) gives up once, gently: no endless "playing".
        _openTimeout = ui.CreateTimer();
        _openTimeout.IsRepeating = false;
        _openTimeout.Interval = TimeSpan.FromSeconds(30);
        _openTimeout.Tick += (_, _) => Safe(() => { if (State == "opening") Fail("timed out"); });
    }

    public bool Playing { get; private set; }
    public int Surah { get; private set; }
    /// <summary>idle, opening, playing, ended, stopped or failed.</summary>
    public string State { get; private set; } = "idle";
    public event Action<bool>? PlayingChanged;

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
                _set(_get() with { QuranReciter = Q.Reciters[i].Id });
                Log.Write($"quran: reciter {Q.Reciters[i].Id}");
                _ui.TryEnqueue(() => Safe(Open));
            },
            () => _ui.TryEnqueue(() => Safe(Open)));
    }

    public void Play(int surah, string reciter)
    {
        StopPlayer();
        int gen = ++_generation;
        Surah = surah;
        State = "opening";
        SetPlaying(true);
        Log.Write($"quran: play surah {surah} ({reciter})");
        _openTimeout.Stop();
        _openTimeout.Start();
        // Music first: pause whatever plays, then start the recitation.
        _media.PauseAllAsync().ContinueWith(_ => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen != _generation) return;
            try { Start(gen, surah, reciter); }
            catch (Exception e) { Fail(e.GetType().Name); }
        })));
    }

    private void Start(int gen, int surah, string reciter)
    {
        var player = new MediaPlayer { AutoPlay = true, Volume = 1.0 };
        player.CommandManager.IsEnabled = false;   // not a session in Windows' media controls (she would dance to it)
        player.MediaOpened += (_, _) => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen != _generation) return;
            State = "playing";
            _set(_get() with { QuranLastSurah = surah });
        }));
        player.MediaEnded += (_, _) => _ui.TryEnqueue(() => Safe(() => { if (gen == _generation) Finish("ended"); }));
        player.MediaFailed += (_, e) => _ui.TryEnqueue(() => Safe(() =>
        {
            if (gen == _generation) Fail(e.Error.ToString());
        }));
        _player = player;
        player.Source = MediaSource.CreateFromUri(Q.Url(reciter, surah, _base));
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
        Finish("stopped");
    }

    private void Finish(string state)
    {
        _openTimeout.Stop();
        _generation++;
        StopPlayer();
        State = state;
        SetPlaying(false);
    }

    private void StopPlayer()
    {
        var p = _player;
        _player = null;
        if (p is null) return;
        try { p.Pause(); p.Source = null; p.Dispose(); }
        catch (Exception e) { Log.Write($"quran: player close failed ({e.GetType().Name})"); }
    }

    private void SetPlaying(bool on)
    {
        if (on == Playing) return;
        Playing = on;
        PlayingChanged?.Invoke(on);
    }

    public string Describe() => $"playing={(Playing ? "true" : "false")} surah={Surah} state={State}";

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"quran error: {e}"); }
    }

    public void Dispose()
    {
        _openTimeout.Stop();
        StopPlayer();
    }
}
