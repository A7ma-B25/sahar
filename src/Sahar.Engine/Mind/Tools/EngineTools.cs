using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Sahar.Core.Brain;
using Sahar.Core.Games;
using Sahar.Core.Mind.Tools;
using Sahar.Core.Motion;
using Sahar.Core.Prayer;
using Sahar.Core.Protocol;
using Sahar.Core.Settings;
using Sahar.Core.Text;
using Sahar.Engine.Diagnostics;
using MusicTool = Sahar.Engine.Tools.MusicTool;

namespace Sahar.Engine.Mind.Tools;

/// <summary>A tool the engine runs: its inverse may be asked whether it still applies (the page shows «رجّعيها» only
/// then), and a read may need to wait for Windows (the media session).</summary>
internal interface IEngineTool : ITool
{
    bool CanUndo(string token);
    Task<ToolOutcome> RunAsync(ToolCall call);
}

/// <summary>The 0.13 activities the tools reach when the engine wires them (the lead's adapter in App.cs over the
/// weather, Claude Code and shelf sources). Null members = not announced.</summary>
internal interface IActivityTools
{
    /// <summary>A short weather summary («٢٢° غيم، مطر بعد ساعة»), or null when the weather is off.</summary>
    string? WeatherNow();
    /// <summary>Claude Code sessions: working, waiting, done today (counts only; never a prompt's content).</summary>
    (int Working, int Waiting, int DoneToday)? ClaudeStatus();
    bool HasShelf { get; }
    void ShelfOpen();
    bool ShelfTidy();
    bool ShelfClear();
}

/// <summary>One row of the catalog mapped to her real abilities (spec §3.4). Runs on the UI thread.</summary>
internal sealed class EngineTool(ToolSpec spec, Func<ToolCall, ToolOutcome> run, Func<string, bool>? undo = null, Func<string, bool>? canUndo = null,
    Func<ToolCall, Task<ToolOutcome>>? runAsync = null) : IEngineTool
{
    public ToolSpec Spec { get; } = spec;
    public ToolOutcome Run(ToolCall call) => run(call);
    public Task<ToolOutcome> RunAsync(ToolCall call) => runAsync?.Invoke(call) ?? Task.FromResult(run(call));
    public bool Undo(string token) => undo?.Invoke(token) ?? false;
    public bool CanUndo(string token) => canUndo?.Invoke(token) ?? false;
}

/// <summary>
/// Her abilities as tools: each <see cref="ToolCatalog"/> row maps to the code the menu card and the chat commands
/// already run (timer, note, hush, hiding, games, moves, prayer, music, Quran, her settings through the settings
/// app's own path), plus small reminders. Undo tokens say exactly what to reverse, and an undo runs only while it
/// still applies (the same timer still running, the note still there, the setting still what she set).
/// </summary>
internal sealed class EngineTools
{
    private readonly MindEngine _m;
    private readonly Dictionary<string, IEngineTool> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Windows.System.DispatcherQueueTimer> _reminders = [];
    private readonly ReminderBook _book;
    private readonly Func<bool> _gateOpen;
    private string? _herTimer;               // the timer she started (its token): any other running timer is his
    private long _hiddenUntil;               // the hiding she started (unix ms)
    private long _musicSeq;                  // each music press of hers; an older undo never flips a newer state

    /// <summary>Who started the call being run (her own reminders wait for the gate).</summary>
    public Sahar.Core.Mind.Autonomy.ActSource Source { get; set; }

    public EngineTools(MindEngine m, IActivityTools? acts, Sahar.Core.Mind.Memory.ISecretBox box, Func<bool> gateOpen)
    {
        _m = m;
        _book = new ReminderBook(box, () => DateTimeOffset.Now);
        _gateOpen = gateOpen;
        Add("timer.start", c =>
        {
            int minutes = Int(c, "minutes") ?? 25;
            var before = End();
            _m.Timer.Start(TimeSpan.FromMinutes(minutes));
            _herTimer = TimerToken();
            return Ok(c, _herTimer + "|" + before.ToString(CultureInfo.InvariantCulture));
        }, UndoTimerStart, t => TimerMatches(t.Split('|')[0]));
        Add("timer.cancel", c =>
        {
            if (!_m.Timer.Running) return ToolOutcome.Fail("unavailable", "ما في مؤقت شغّال.");
            var before = End();
            _m.Timer.Cancel();
            return Ok(c, "timer-was:" + before.ToString(CultureInfo.InvariantCulture));
        }, UndoTimerCancel, t => !_m.Timer.Running && Was(t) > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000);
        Add("reminder.set", Remind, t => Id(t) is { } id && _book.Remove(id) && Do(() => { if (_reminders.Remove(id, out var timer)) timer.Stop(); }),
            t => Id(t) is { } id && _book.Has(id));
        Add("note.save", c =>
        {
            if (!_m.Notes.Save(Str(c, "text") ?? "")) return ToolOutcome.Fail("failed", ToolTemplates.Failed);
            var last = _m.Notes.Store.Latest(1);
            return Ok(c, last.Count == 1 ? NoteToken(last[0]) : null);
        }, t => ParseNote(t) is { } n && _m.Notes.Store.Remove(n.Text, n.At), t => ParseNote(t) is { } n && _m.Notes.Store.Latest(Sahar.Core.Tools.NotesStore.MaxNotes)
            .Any(x => x.Text == n.Text && x.At.UtcTicks == n.At.UtcTicks));
        Add("note.open", c => { _m.Menu.Close(); _m.Notes.Open(); return Ok(c); });
        Add("hush.on", c =>
        {
            int? minutes = Int(c, "minutes");
            _m.Dnd.TurnOn(minutes is { } mm ? TimeSpan.FromMinutes(mm) : null);
            return Ok(c, HushToken());
        }, t => HushMatches(t) && Do(_m.Dnd.TurnOff), HushMatches);
        Add("hush.off", c => { _m.Dnd.TurnOff(); return Ok(c); });
        Add("sleep.hide", c =>
        {
            _m.Menu.Close();
            var span = TimeSpan.FromMinutes(Int(c, "minutes") ?? 60);
            _m.Director.HideFor(span);
            _hiddenUntil = (DateTimeOffset.UtcNow + span).ToUnixTimeMilliseconds();
            return Ok(c, "hidden:" + _hiddenUntil.ToString(CultureInfo.InvariantCulture));
        }, t => HiddenMatches(t) && Do(() => { _hiddenUntil = 0; _m.Director.HideFor(TimeSpan.Zero); }), HiddenMatches);
        Add("game.start", c =>
        {
            var kind = Str(c, "game") switch { "chase" => GameKind.Chase, "hide" => GameKind.HideSeek, "ball" => GameKind.Ball, _ => GameKind.Stars };
            _m.Menu.Close();
            _m.Games.Start(kind);
            return Ok(c);
        });
        Add("games.open", c =>
        {
            if (!_m.Menu.IsOpen) _m.Menu.Open("mind");
            _m.Menu.Press("play");
            return Ok(c);
        });
        Add("move.play", c =>
        {
            _m.Menu.Close();
            bool played = Str(c, "move") switch
            {
                "dance" => _m.Motion.Play(MotionKind.Dance),
                "stretch" => _m.Motion.Play(MotionKind.Stretch),
                _ => _m.Soul.Hop(1f),
            };
            return played ? Ok(c) : ToolOutcome.Fail("unavailable", "مو هلق، بعدين.");
        });
        Add("prayer.next", c =>
        {
            if (_m.Prayer.Next() is not { } p) return ToolOutcome.Fail("unavailable", "ما بعرف مدينتك لسا. خبّرني ياها.");
            int minutes = (int)Math.Ceiling(p.Left.TotalMinutes);
            var data = $$"""{"prayer":"{{p.Prayer.ToString().ToLowerInvariant()}}","inMinutes":{{minutes}}}""";
            return new ToolOutcome(true, Digits.ToArabicIndic($"{PrayerNames.Arabic(p.Prayer)} بعد {PrayerText.Duration(p.Left)}"), data);
        });
        Add("prayer.open", c => { _m.Menu.Close(); _m.Prayer.Open(); return Ok(c); });
        Add("music.now", c => new ToolOutcome(true, _m.Media.MusicPlaying ? "في شي عم يشتغل" : MusicTool.NothingPlaying,
            $$"""{"playing":{{(_m.Media.MusicPlaying ? "true" : "false")}}}"""), runAsync: MusicNowAsync);
        Add("music.control", c =>
        {
            if (!_m.Media.HasSession) return ToolOutcome.Fail("unavailable", MusicTool.NothingPlaying);
            var op = Str(c, "op") ?? "toggle";
            if (op == "pause" && !_m.Media.MusicPlaying) return new ToolOutcome(true, "الموسيقى واقفة أصلاً");
            bool wasPlaying = _m.Media.MusicPlaying;
            _m.Music.Press(op switch { "next" => "music-next", "prev" => "music-prev", _ => "music-toggle" });
            var stamp = (++_musicSeq).ToString(CultureInfo.InvariantCulture) + ":" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            return Ok(c, op is "pause" or "toggle" ? (wasPlaying ? "music:was-playing:" : "music:was-paused:") + stamp : null);
        }, t => MusicUndoable(t) && Do(() => _m.Music.Press("music-toggle")), MusicUndoable);
        Add("quran.open", c => { _m.Menu.Close(); _m.Quran.Open(); return Ok(c); });
        Add("quran.stop", c =>
        {
            if (!_m.Quran.Playing) return ToolOutcome.Fail("unavailable", "ما في قرآن شغّال.");
            _m.Quran.Stop();
            return Ok(c);
        });
        Add("settings.set", SetSetting, UndoSetting, CanUndoSetting);
        if (acts is not null)
        {
            Add("weather.now", c => acts.WeatherNow() is { } w ? new ToolOutcome(true, w, JsonSerializer.Serialize(w, EngineToolsJson.Default.String))
                : ToolOutcome.Fail("unavailable", "الطقس مطفى."));
            Add("claude.status", c => acts.ClaudeStatus() is { } s
                ? new ToolOutcome(true, Digits.ToArabicIndic($"Claude: {s.Working} شغّال، {s.Waiting} ناطر"),
                    FormattableString.Invariant($$"""{"working":{{s.Working}},"waiting":{{s.Waiting}},"doneToday":{{s.DoneToday}}}"""))
                : ToolOutcome.Fail("unavailable", ToolTemplates.Unavailable));
            if (acts.HasShelf)
            {
                Add("shelf.open", c => { _m.Menu.Close(); acts.ShelfOpen(); return Ok(c); });
                Add("shelf.tidy", c => acts.ShelfTidy() ? Ok(c) : ToolOutcome.Fail("failed", ToolTemplates.Failed));
                Add("shelf.clear", c => acts.ShelfClear() ? Ok(c) : ToolOutcome.Fail("failed", ToolTemplates.Failed));
            }
        }
    }

    /// <summary>The tools she has now, in catalog order (memory/goal proposals are announced too: the Mind keeps them).</summary>
    public IReadOnlyList<ToolSpec> Announced() =>
        ToolCatalog.All.Where(t => _tools.ContainsKey(t.Name) || ToolCatalog.MindLocal.Contains(t.Name)).ToList();

    public IEngineTool? Get(string name) => _tools.GetValueOrDefault(name);

    /// <summary>Pending reminders are dropped (the engine is closing).</summary>
    public void Stop()
    {
        foreach (var t in _reminders.Values) t.Stop();
        _reminders.Clear();
    }

    // ---- the pieces ----

    private void Add(string name, Func<ToolCall, ToolOutcome> run, Func<string, bool>? undo = null, Func<string, bool>? canUndo = null,
        Func<ToolCall, Task<ToolOutcome>>? runAsync = null)
    {
        var spec = ToolCatalog.Get(name)!;
        _tools[name] = new EngineTool(spec, run, undo, canUndo, runAsync);
    }

    private static bool Do(Action a)
    {
        a();
        return true;
    }

    private static ToolOutcome Ok(ToolCall c, string? undo = null) => new(true, ToolTemplates.Done(c), UndoToken: undo);

    internal static string? Str(ToolCall c, string k) =>
        c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static int? Int(ToolCall c, string k) =>
        c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;

    /// <summary>"timer:&lt;end, unix ms&gt;": the same timer is the one that ends then (± 2 s).</summary>
    private string? TimerToken() =>
        _m.Timer.Remaining is { } left ? "timer:" + (DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : null;

    private bool TimerMatches(string token) =>
        token.StartsWith("timer:", StringComparison.Ordinal) && long.TryParse(token.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out long end)
        && _m.Timer.Remaining is { } left && Math.Abs((DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds() - end) < 2000;

    private static string NoteToken(Sahar.Core.Tools.Note n) => "note:" + n.At.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + n.Text;

    private static Sahar.Core.Tools.Note? ParseNote(string t)
    {
        if (!t.StartsWith("note:", StringComparison.Ordinal)) return null;
        int colon = t.IndexOf(':', 5);
        if (colon < 0 || !long.TryParse(t.AsSpan(5, colon - 5), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)) return null;
        return new(t[(colon + 1)..], new DateTimeOffset(ticks, TimeSpan.Zero));
    }

    /// <summary>"music:was-playing:&lt;seq&gt;:&lt;ms&gt;": only her last press, within 10 minutes, and only while the state is still hers.</summary>
    private bool MusicUndoable(string t)
    {
        var p = t.Split(':');
        if (p.Length != 4 || p[0] != "music" || !long.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long seq)
            || !long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at)) return false;
        if (seq != _musicSeq || DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - at > 600_000 || !_m.Media.HasSession) return false;
        return p[1] == "was-playing" ? !_m.Media.MusicPlaying : _m.Media.MusicPlaying;
    }

    /// <summary>Is a timer running that she did not start (his)? Then hers asks before replacing or stopping it.</summary>
    public bool HisTimerRunning => _m.Timer.Running && !(_herTimer is { } t && TimerMatches(t));

    /// <summary>The running timer's end (unix ms), 0 when none.</summary>
    private long End() => _m.Timer.Remaining is { } left ? (DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds() : 0;

    private static long Was(string t) =>
        t.StartsWith("timer-was:", StringComparison.Ordinal) && long.TryParse(t.AsSpan(10), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : 0;

    /// <summary>Undo of her timer: stop it and give back the one it replaced, if that one still had time.</summary>
    private bool UndoTimerStart(string t)
    {
        var parts = t.Split('|');
        if (!TimerMatches(parts[0])) return false;
        _m.Timer.Cancel();
        _herTimer = null;
        if (parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long prev)) Restore(prev);
        return true;
    }

    private bool UndoTimerCancel(string t)
    {
        if (_m.Timer.Running) return false;
        return Restore(Was(t));
    }

    private bool Restore(long end)
    {
        var left = TimeSpan.FromMilliseconds(end - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (end == 0 || left < TimeSpan.FromSeconds(1)) return false;
        _m.Timer.Start(left);
        return true;
    }

    /// <summary>"hush:&lt;end ms&gt;" or "hush:off" (until he says): the same hush, still on.</summary>
    private string HushToken() => _m.Dnd.Remaining is { } left
        ? "hush:" + (DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) : "hush:off";

    internal bool HushMatches(string t)
    {
        if (!_m.Dnd.On || !t.StartsWith("hush:", StringComparison.Ordinal)) return false;
        if (t == "hush:off") return _m.Dnd.UntilOff;
        return long.TryParse(t.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out long end) && _m.Dnd.Remaining is { } left
            && Math.Abs((DateTimeOffset.UtcNow + left).ToUnixTimeMilliseconds() - end) < 2000;
    }

    private bool HiddenMatches(string t) =>
        t.StartsWith("hidden:", StringComparison.Ordinal) && long.TryParse(t.AsSpan(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out long until)
        && until == _hiddenUntil && _m.Director.Hidden && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < until;

    private static string? Id(string t) => t.StartsWith("reminder:", StringComparison.Ordinal) ? t[9..] : null;

    private async Task<ToolOutcome> MusicNowAsync(ToolCall c)
    {
        var now = await _m.Media.NowPlayingAsync();
        bool title = _m.Settings().MindLearnMusic;   // the title only with «تعرفي شو عم اسمع» (music learning) on
        if (now is not { } n) return new ToolOutcome(true, MusicTool.NothingPlaying, """{"playing":false}""");
        var data = title
            ? """{"playing":true,"title":""" + JsonSerializer.Serialize(n.Title, EngineToolsJson.Default.String) + "}"
            : """{"playing":true}""";
        return new ToolOutcome(true, title ? $"🎵 {n.Title}" : "في شي عم يشتغل", data);
    }

    private ToolOutcome Remind(ToolCall c)
    {
        var text = Str(c, "text") ?? "";
        DateTimeOffset due;
        if (Int(c, "inMinutes") is { } m) due = DateTimeOffset.Now.AddMinutes(m);
        else
        {
            var at = TimeOnly.ParseExact(Str(c, "at")!, "HH:mm", CultureInfo.InvariantCulture);
            var now = DateTime.Now;
            var when = now.Date + at.ToTimeSpan();
            if (when <= now) when = when.AddDays(1);   // a time already past today is tomorrow's
            due = new DateTimeOffset(when);
        }
        var r = _book.Add(text, due, Source == Sahar.Core.Mind.Autonomy.ActSource.Self);
        Arm(r);
        return Ok(c, "reminder:" + r.Id);
    }

    /// <summary>Her reminders after a restart: each is armed again (one that came due while she was off speaks now).</summary>
    public void ArmSaved()
    {
        foreach (var r in _book.Pending()) Arm(r);
    }

    private void Arm(Reminder r)
    {
        if (_reminders.Remove(r.Id, out var old)) old.Stop();
        var timer = _m.Ui.CreateTimer();
        timer.IsRepeating = false;
        var wait = r.Due - DateTimeOffset.Now;
        timer.Interval = wait > TimeSpan.FromMilliseconds(200) ? wait : TimeSpan.FromMilliseconds(200);
        timer.Tick += (_, _) =>
        {
            try { Fire(r); }
            catch (Exception e) { Log.Write($"mind: reminder error {e.GetType().Name}"); }
        };
        _reminders[r.Id] = timer;
        timer.Start();
    }

    /// <summary>His reminder speaks on time; one she set on her own waits (a minute at a time) for the gate.</summary>
    private void Fire(Reminder r)
    {
        _reminders.Remove(r.Id);
        if (!_book.Has(r.Id)) return;
        if (!ReminderBook.MaySpeak(r, _gateOpen()))
        {
            Log.Write("mind: her reminder waits for a calm moment");
            Arm(r with { Due = DateTimeOffset.Now.AddMinutes(1) });
            return;
        }
        _book.Remove(r.Id);
        Log.Write("mind: reminder due");
        _m.Director.Post(EventKind.Reminder, TimeSpan.FromSeconds(60));
        _m.Speech.Say("⏰ " + r.Text);
    }

    // ---- settings.set: the settings app's own path (SettingsPatch → settings.json → settings-reload) ----

    private ToolOutcome SetSetting(ToolCall c)
    {
        var key = Str(c, "key")!;
        var value = c.Args.GetProperty("value");
        var current = _m.Settings();
        var next = SettingsPatch.Apply(current, key, value, out var error);
        if (error is not null)
            return ToolOutcome.Fail("invalid", error == SettingsPatch.NeedsCity ? "بدي اعرف مدينتك أول (من صفحة سحورة)." : ToolTemplates.Failed);
        var old = Raw(current, key);
        if (!Apply(s => SettingsPatch.Apply(s, key, value, out _))) return ToolOutcome.Fail("failed", ToolTemplates.Failed);
        return Ok(c, "setting:" + key + "\n" + old + "\n" + Raw(next, key));
    }

    private bool CanUndoSetting(string token) => ParseSetting(token) is { } t && Raw(_m.Settings(), t.Key) == t.New;

    private bool UndoSetting(string token)
    {
        if (ParseSetting(token) is not { } t || Raw(_m.Settings(), t.Key) != t.New) return false;
        using var doc = JsonDocument.Parse(t.Old);
        var old = doc.RootElement.Clone();
        return Apply(s => SettingsPatch.Apply(s, t.Key, old, out _));
    }

    private static (string Key, string Old, string New)? ParseSetting(string token)
    {
        if (!token.StartsWith("setting:", StringComparison.Ordinal)) return null;
        var parts = token[8..].Split('\n');
        return parts.Length == 3 ? (parts[0], parts[1], parts[2]) : null;
    }

    /// <summary>The key's value as JSON text, as settings.json holds it.</summary>
    private static string Raw(SaharSettings s, string key)
    {
        using var doc = JsonDocument.Parse(SettingsStore.ToJson(s));
        foreach (var p in doc.RootElement.EnumerateObject())
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value.GetRawText();
        return "null";
    }

    /// <summary>Writes settings.json field by field (as the settings app does), then asks the engine to reload over its own
    /// pipe, so the change applies through the same path (she resizes, the menu changes, she hops).</summary>
    private bool Apply(Func<SaharSettings, SaharSettings> change)
    {
        try { SettingsStore.Update(SettingsStore.DefaultPath, change); }
        catch (Exception e)
        {
            Log.Write($"mind: setting not saved ({e.GetType().Name})");
            return false;
        }
        _ = Task.Run(SelfReloadAsync);
        return true;
    }

    private static async Task SelfReloadAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await using var pipe = new NamedPipeClientStream(".", Sahar.Engine.Pipe.PipeServer.Name, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(cts.Token);
            var utf8 = new UTF8Encoding(false);
            using var reader = new StreamReader(pipe, utf8);
            await using var writer = new StreamWriter(pipe, utf8) { NewLine = "\n", AutoFlush = true };
            await writer.WriteLineAsync(ProtocolJson.Write(new PipeRequest("cmd", "settings-reload", null)).AsMemory(), cts.Token);
            await reader.ReadLineAsync(cts.Token);
        }
        catch (Exception e) { Log.Write($"mind: reload after a setting failed ({e.GetType().Name})"); }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class EngineToolsJson : System.Text.Json.Serialization.JsonSerializerContext;
