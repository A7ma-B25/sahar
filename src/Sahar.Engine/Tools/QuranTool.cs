using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Senses;
using Sahar.Engine.Surfaces;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Q = Sahar.Core.Quran.Quran;

namespace Sahar.Engine.Tools;

/// <summary>
/// القرآن (spec §7.1): pick a surah (the last one first) and a reciter; it streams from the islamic.network CDN.
/// Any playing music is paused first; while it plays she is quiet and still with the quran face. The end of the
/// surah, a stop or an error (a gentle line, no retry) bring her back.
/// </summary>
internal sealed class QuranTool : IDisposable
{
    public const string Failed = "ما قدرت شغّل التلاوة هلق";

    private readonly DispatcherQueue _ui;
    private readonly Panel _panel;
    private readonly Func<RectF> _body;
    private readonly Media _media;
    private readonly Action<string> _say;
    private readonly Func<SaharSettings> _get;
    private readonly Action<Func<SaharSettings, SaharSettings>> _set;   // load → change → save (two writers)
    private readonly string? _base;
    private readonly DispatcherQueueTimer _openTimeout;
    private IRecitation? _player;
    private int _generation;

    public QuranTool(DispatcherQueue ui, Panel panel, Func<RectF> body, Media media, Action<string> say,
        Func<SaharSettings> get, Action<Func<SaharSettings, SaharSettings>> set, string? baseUrl)
    {
        (_ui, _panel, _body, _media, _say, _get, _set, _base) = (ui, panel, body, media, say, get, set, baseUrl);
        // A recitation that never opens (a network that hangs) gives up once, gently: no endless "playing".
        _openTimeout = ui.CreateTimer();
        _openTimeout.IsRepeating = false;
        _openTimeout.Interval = TimeSpan.FromSeconds(15);
        _openTimeout.Tick += (_, _) => Safe(() => { if (State == "opening") Fail("timed out"); });
    }

    public bool Playing { get; private set; }
    public int Surah { get; private set; }
    /// <summary>idle, opening, playing, ended, stopped or failed.</summary>
    public string State { get; private set; } = "idle";
    public event Action<bool>? PlayingChanged;

    /// <summary>0.16 (Task 8 wires it): where he stopped in a surah, as the resume row shows it («وقفت عند ١٢:٠٤»).</summary>
    public Func<int, string?>? ResumeMeta { get; set; }

    /// <summary>«شو بدك تسمع؟» (V15Pickers): the resume row first, then the 114 surahs; search by name or number;
    /// the reciter is a drill in the same panel. Nothing typed or picked is logged beyond the surah number.</summary>
    public void Open() => _panel.Show(SurahSpec(), OnPanel);

    private string D(int n) => Sahar.Core.Text.Digits.Format(n, _get().Language);

    private PanelSpec SurahSpec()
    {
        var s = _get();
        var reciter = Valid(s.QuranReciter);
        var rows = new List<PickRow>();
        if (s.QuranLastSurah is int last and >= 1 and <= 114)
            rows.Add(new PickRow("resume", Address.ToYou("كمّل ") + Q.SurahName(last)) { Meta = ResumeMeta?.Invoke(last), Play = true, Sel = true, Pinned = true, Strong = true });
        for (int n = 1; n <= 114; n++) rows.Add(new PickRow(n.ToString(System.Globalization.CultureInfo.InvariantCulture), Q.SurahName(n)) { Num = n, NumText = D(n) });
        return new PanelSpec
        {
            Kind = "list", Name = "surah", Title = Address.ToYou("شو بدك تسمع؟"), Sub = "بصوت " + Q.ReciterName(reciter), SubAction = "reciter",
            Search = Address.ToYou("دوّر على سورة أو رقمها"), EnterHint = Address.ToYou("Enter لتشغّلها"), Rows = rows,
            None = new PickBox("ما لقيت سورة بهالاسم", Address.ToYou("جرّب رقمها، مثلاً ٢٥، أو أول حرفين منها."), "book"),
            Now = Playing ? (Address.ToYou("عم تسمع ") + Q.SurahName(Surah), Q.ReciterName(reciter), "وقّف") : null,
        };
    }

    private PanelSpec ReciterSpec()
    {
        var current = Valid(_get().QuranReciter);
        return new PanelSpec
        {
            Kind = "list", Name = "reciter", Title = Address.ToYou("مين بدك يقرأ؟"), Back = true, Sub = Address.ToYou("بتسمع صوته من السورة الجاية"),
            Rows = Q.Reciters.Select(r => new PickRow(r.Id, r.Name) { Sel = r.Id == current, Check = r.Id == current }).ToList(),
        };
    }

    private void OnPanel(SurfaceEvent e) => Safe(() =>
    {
        bool reciters = _panel.Name == "reciter";
        switch (e.Kind)
        {
            case "pick" when reciters:
                if (Q.Reciters.FirstOrDefault(r => r.Id == e.Value) is not { } who) return;
                _set(s => s with { QuranReciter = who.Id });
                Log.Write($"quran: reciter {who.Id}");
                _panel.Update(SurahSpec());
                break;
            case "back" when reciters:
                _panel.Update(SurahSpec());
                break;
            case "action" when e.Value == "reciter":
                _panel.Update(ReciterSpec());
                break;
            case "action" when e.Value == "now-stop":
                Stop();
                _panel.Update(SurahSpec());
                break;
            case "pick":
                var last = _get().QuranLastSurah;
                int surah = e.Value == "resume" && last is int l ? l : int.TryParse(e.Value, out var n) ? n : 0;
                if (surah is < 1 or > 114) return;
                _panel.Done();
                Play(surah, Valid(_get().QuranReciter));
                break;
        }
    });

    private static string Valid(string reciter) => Q.Reciters.Any(r => r.Id == reciter) ? reciter : Q.Reciters[0].Id;

    /// <param name="make">Test mode: a stand-in player (CI's Windows Server has no MP3 decoder).</param>
    public void Play(int surah, string reciter, Func<IRecitation>? make = null)
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
        }));
        player.Ended += () => _ui.TryEnqueue(() => Safe(() => { if (gen == _generation) Finish("ended"); }));
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
        try { p.Dispose(); }
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
        catch (Exception e) { Log.Write($"quran error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
    }

    public void Dispose()
    {
        _openTimeout.Stop();
        StopPlayer();
    }
}

/// <summary>One surah playing: opened, ended or failed (raised on any thread).</summary>
internal interface IRecitation : IDisposable
{
    event Action? Opened;
    event Action? Ended;
    event Action<string>? Failed;
    void Start(Uri url);
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

    public void Dispose()
    {
        _player.Pause();
        _player.Source = null;
        _player.Dispose();
    }
}

/// <summary>Test mode: "plays" for a set time, so CI (no MP3 decoder on Windows Server) can check her behaviour.</summary>
internal sealed class FakeRecitation(DispatcherQueue ui, TimeSpan length) : IRecitation
{
    private DispatcherQueueTimer? _open, _end;

    public event Action? Opened;
    public event Action? Ended;
    /// <summary>A stand-in never fails; the network failure is tested with the real player.</summary>
    public event Action<string>? Failed { add { } remove { } }

    public void Start(Uri url)
    {
        _open = ui.CreateTimer();
        _open.IsRepeating = false;
        _open.Interval = TimeSpan.FromMilliseconds(300);
        _open.Tick += (_, _) => Opened?.Invoke();
        _open.Start();
        _end = ui.CreateTimer();
        _end.IsRepeating = false;
        _end.Interval = length;
        _end.Tick += (_, _) => Ended?.Invoke();
        _end.Start();
    }

    public void Dispose()
    {
        _open?.Stop();
        _end?.Stop();
    }
}
