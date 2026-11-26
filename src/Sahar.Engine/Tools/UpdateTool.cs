using System.Diagnostics;
using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Core.Update;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Tools;

/// <summary>
/// "في نسخة جديدة مني": checks on <see cref="UpdateSchedule"/> (2 min after start, then every 12 h) or from the tray
/// item, asks in her bubble with yes / later, and on yes downloads and verifies the signed setup and starts it. She
/// does not quit by herself: the setup asks her to when it is ready, and starts her again (or the old her, if it did
/// not finish). If the setup ends with an error while she is still here, she says so. Installed, the setup runs
/// silently; portable (a zip folder), it runs as the wizard, and the installed her says where she lives now.
/// Never updates without your yes. Background failures are a log line (and the tray tooltip); a step you started gets
/// a short line. 0.15.0 T6: every step has its line (<see cref="UpdateText"/>) in her bubble and in the tray tooltip
/// (a hidden her gets a tray notification for what you asked); a click while a check is already on its way says so
/// and gets the answer (0.14.x returned silently: his «تحديث» showed nothing); network failures are tried again
/// twice (3 s, 10 s) before she tells you why; the download says how far it is.
/// </summary>
internal sealed class UpdateTool : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly Updater _updater;
    private readonly ListBubble _list;
    private readonly Func<RectF> _body;
    private readonly Action<string> _say;
    /// <summary>The tray tooltip's line ("" = her name only); true = worth a notification when she is hidden (an answer
    /// you asked for).</summary>
    private readonly Action<string, bool> _tray;
    private readonly Func<SaharSettings> _get;
    /// <summary>The engine's Save (0.12): a change merged into what the file holds now, never a whole snapshot.</summary>
    private readonly Action<Func<SaharSettings, SaharSettings>> _save;
    private readonly Func<bool> _busy;
    private readonly string _marker;
    private readonly DispatcherQueueTimer _timer;
    private readonly UpdateSchedule _schedule = new(DateTimeOffset.Now);
    private UpdateOffer? _offer;
    private UpdateStep? _last;
    private string _status = "idle";
    /// <summary>0.14.1 (review follow-up): Windows is ending the session or she is quitting — a setup is never started
    /// then (it would run against a closing desktop).</summary>
    public bool ShuttingDown { get; set; }
    private int? _setupExit;
    private readonly DispatcherQueueTimer _retry;
    private Action? _retryThen;
    private bool _manual;
    private int _tries;
    private int? _percent;
    private int _reported = -1;   // the last percent sent to the UI thread (pool threads write it)
    private UpdateFailure? _failure;

    /// <param name="automatic">Checks on the schedule (off in test mode and when the setting is off).</param>
    /// <param name="reachable">False in test mode without a fake GitHub: no request ever leaves (CI never hits GitHub).</param>
    /// <param name="installed">She runs from the installer's folder (else portable: the wizard, not a silent setup).</param>
    /// <param name="marker">Where a portable her notes that she left for the wizard (in %LocalAppData%\Sahar).</param>
    public UpdateTool(DispatcherQueue ui, Updater updater, ListBubble list, Func<RectF> body, Action<string> say, Action<string, bool> tray,
        Func<SaharSettings> get, Action<Func<SaharSettings, SaharSettings>> save, Func<bool> busy, bool automatic, bool reachable, bool installed, string marker)
    {
        (_ui, _updater, _list, _body, _say, _tray, _get, _save, _busy, _marker) = (ui, updater, list, body, say, tray, get, save, busy, marker);
        (Reachable, Installed) = (reachable, installed);
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (_, _) => Safe(() => { if (_get().UpdateCheckEnabled && _schedule.Due(DateTimeOffset.Now)) Check(manual: false); });
        if (automatic && reachable) _timer.Start();
        _retry = ui.CreateTimer();
        _retry.IsRepeating = false;
        _retry.Tick += (_, _) => Safe(() => { var then = _retryThen; _retryThen = null; then?.Invoke(); });
    }

    /// <summary>Her line in the bubble and the tray tooltip, to whoever she talks to.</summary>
    private void Tell(string line, bool notify = false)
    {
        line = Address.ToYou(line);
        _say(line);
        _tray(line, notify);
    }

    /// <summary>The tray tooltip only (a background step: no bubble).</summary>
    private void Status(string line) => _tray(Address.ToYou(line), false);

    private void After(TimeSpan wait, Action then)
    {
        _retryThen = then;
        _retry.Interval = wait;
        _retry.Start();
    }

    public bool Reachable { get; }
    public bool Installed { get; }

    /// <summary>At start: an installed her that a portable her just left for says where she lives now (once).</summary>
    public void SayIfMoved()
    {
        string? text = null;
        try { if (File.Exists(_marker)) text = File.ReadAllText(_marker); } catch (Exception) { }
        if (text is null) return;
        if (UpdateRules.SayMoved(text, DateTimeOffset.Now, Installed))
        {
            Log.Write("update: moved from a portable folder to the installed one");
            _say(UpdateRules.MovedLine);
        }
        if (Installed) try { File.Delete(_marker); } catch (Exception) { }
    }

    /// <summary>Looks for a newer version. <paramref name="manual"/>: you asked (the tray), so she always answers.
    /// A background check makes no request while she is busy or the question is snoozed.</summary>
    public void Check(bool manual)
    {
        if (!Reachable) return;
        if (_status is "checking" or "downloading" or "installing")
        {
            // 0.15.0 T6: a click while one is on its way is answered (and gets the check's answer too).
            if (!manual) return;
            Log.Write($"update: asked while {_status}");
            if (_status == "checking") { _manual = true; Tell(UpdateText.StillChecking); }
            else if (_status == "downloading") Tell(UpdateText.Downloading(_percent));
            else Tell(Installed ? UpdateText.Ready : UpdateText.ReadyPortable);
            return;
        }
        var now = DateTimeOffset.Now;
        if (UpdateSchedule.BeforeCheck(manual, _busy(), now, _get().UpdateAskAfter) is { } skip)
        {
            _last = skip;
            if (skip == UpdateStep.Later) _schedule.Busy(now); else _schedule.Checked(now);
            Log.Write($"update: {skip} (no request)");
            return;
        }
        _status = "checking";
        _manual = manual;
        _tries = 0;
        _failure = null;
        Log.Write($"update: checking ({(manual ? "you asked" : "on schedule")})");
        if (manual) Tell(UpdateText.Checking); else Status(UpdateText.Checking);
        RunCheck();
    }

    private void RunCheck()
    {
        _ = _updater.CheckAsync(_get().UpdateChannel).ContinueWith(t => _ui.TryEnqueue(() => Safe(() =>
        {
            var (offer, failure) = t.IsCompletedSuccessfully ? t.Result : (null, UpdateFailure.Other);
            Checked(offer, failure);
        })), TaskScheduler.Default);
    }

    private void Checked(UpdateOffer? offer, UpdateFailure? failure)
    {
        var now = DateTimeOffset.Now;
        bool manual = _manual;
        if (failure is { } f)
        {
            _tries++;
            if (!ShuttingDown && UpdateFailures.RetryAfter(f, _tries) is { } wait)
            {
                Log.Write(FormattableString.Invariant($"update: check retry {_tries} in {wait.TotalSeconds:0} s ({f})"));
                if (manual) Tell(UpdateText.Retrying); else Status(UpdateText.Retrying);
                After(wait, RunCheck);
                return;
            }
            _status = "failed";
            _failure = f;
            Log.Write($"update: check gave up ({f}, {_tries} {(_tries == 1 ? "try" : "tries")})");
            var line = UpdateText.Failed(f, download: false);
            if (manual) Tell(line, notify: true);
            else { Status(line); _schedule.Failed(now); }
            return;
        }
        _status = "idle";
        if (!manual) _schedule.Checked(now);
        var step = UpdateSchedule.Decide(offer, manual, _busy(), now, _get().UpdateAskAfter);
        _last = step;
        Log.Write(offer is null ? $"update: {step}" : $"update: {step} {offer.Version}");
        switch (step)
        {
            case UpdateStep.Ask: Ask(offer!, now, manual); break;
            case UpdateStep.UpToDate: Tell(UpdateText.UpToDate, notify: true); break;
            case UpdateStep.Later: _schedule.Busy(now); _tray("", false); break;
            case UpdateStep.Snoozed: _tray("", false); break;
            case UpdateStep.Nothing: Status(UpdateText.UpToDate); break;
        }
    }

    private void Ask(UpdateOffer offer, DateTimeOffset now, bool manual)
    {
        _offer = offer;
        _status = "asked";
        // Asking snoozes for a day: "later", Esc or a click elsewhere all mean "ask me tomorrow".
        _save(s => s with { UpdateAskAfter = UpdateSchedule.AskedAt(now) });
        Tell(UpdateText.Available(offer.Version), notify: manual);
        _list.Show(_body(), UpdateText.ListTitle(offer.Version), [UpdateText.Yes], UpdateText.Later,
            _ => _ui.TryEnqueue(() => Safe(Install)),
            () => _ui.TryEnqueue(() => Safe(Later)));
    }

    public void Later()
    {
        if (_status == "asked") _status = "idle";
        Log.Write("update: later");
    }

    /// <summary>Yes: download, verify, start the setup and watch it.</summary>
    public void Install()
    {
        if (_offer is not { } offer || _status is "downloading" or "installing") return;
        _status = "downloading";
        _setupExit = null;
        _tries = 0;
        _failure = null;
        Log.Write($"update: downloading {offer.Version}");
        Tell(UpdateText.Downloading(null));
        Download(offer);
    }

    private void Download(UpdateOffer offer)
    {
        _percent = null;
        Volatile.Write(ref _reported, -1);
        _ = _updater.DownloadAsync(offer, Progress).ContinueWith(t => _ui.TryEnqueue(() => Safe(() =>
        {
            var (setup, failure) = t.IsCompletedSuccessfully ? t.Result : (null, UpdateFailure.Other);
            if (setup is null)
            {
                var f = failure ?? UpdateFailure.Other;
                _tries++;
                if (!ShuttingDown && UpdateFailures.RetryAfter(f, _tries) is { } wait)
                {
                    Log.Write(FormattableString.Invariant($"update: download retry {_tries} in {wait.TotalSeconds:0} s ({f})"));
                    Tell(UpdateText.Retrying);
                    After(wait, () => Download(offer));
                    return;
                }
                _status = "failed";
                _failure = f;
                Log.Write($"update: download gave up ({f}, {_tries} {(_tries == 1 ? "try" : "tries")})");
                Tell(UpdateText.Failed(f, download: true), notify: true);
                return;
            }
            if (ShuttingDown) { _status = "idle"; Log.Write("update: not started (shutting down)"); return; }
            // Starting the setup is off her UI thread: Windows scans a new 60 MB exe before it runs (5.3 s on CI run
            // 43598507492), and she must keep drawing and answering meanwhile.
            bool silent = Installed;
            _ = Task.Run(() => Updater.Launch(setup, silent)).ContinueWith(l => _ui.TryEnqueue(() => Safe(() =>
                Started(offer, l.IsCompletedSuccessfully ? l.Result : null))), TaskScheduler.Default);
        })), TaskScheduler.Default);
    }

    /// <summary>From the download's pool thread: only a new whole percent goes to her UI thread.</summary>
    private void Progress(long done, long? total)
    {
        if (UpdateText.Percent(done, total) is not { } p || Interlocked.Exchange(ref _reported, p) == p) return;
        _ui.TryEnqueue(() => Safe(() => Progressed(p)));
    }

    private void Progressed(int p)
    {
        if (_status != "downloading") return;
        var reached = UpdateText.Milestone(_percent, p);
        _percent = p;
        Status(UpdateText.Downloading(p));
        if (reached is not { } m) return;
        Log.Write($"update: download {m}%");
        if (m < 100) _say(Address.ToYou(UpdateText.Downloading(m)));
    }

    /// <summary>On her UI thread again: the setup started (or could not).</summary>
    private void Started(UpdateOffer offer, Process? process)
    {
        if (process is null)
        {
            _status = "failed";
            Forget();
            Log.Write("update: setup could not start");
            Tell(UpdateText.NotStarted, notify: true);
            return;
        }
        // 0.14.1 (review follow-up): the portable marker only once the wizard really started (a launch that failed
        // left a marker that told the next start she had moved).
        if (!Installed) try { File.WriteAllText(_marker, UpdateRules.MoveMarker(DateTimeOffset.Now)); } catch (Exception) { }
        _status = "installing";
        Log.Write($"update: installing {offer.Version} ({(Installed ? "silent" : "wizard: portable")})");
        Tell(Installed ? UpdateText.Ready : UpdateText.ReadyPortable, notify: true);
        _ = process.WaitForExitAsync().ContinueWith(_ => _ui.TryEnqueue(() => Safe(() => SetupEnded(process))), TaskScheduler.Default);
    }

    /// <summary>The setup ended and she is still here: it did not install (it closes her when it does).</summary>
    private void SetupEnded(Process process)
    {
        int code;
        try { code = process.ExitCode; } catch (Exception) { code = -1; }
        process.Dispose();
        _setupExit = code;
        Forget();
        if (code == 0) { _status = "idle"; Log.Write("update: setup ended (0) and she is still here"); return; }
        _status = "failed";
        Log.Write($"update: setup ended with {code}; nothing changed");
        Tell(UpdateText.SetupFailed(code), notify: true);
    }

    private void Forget()
    {
        if (!Installed) try { File.Delete(_marker); } catch (Exception) { }
    }

    /// <summary>Test mode: what the update check is doing.</summary>
    public string Describe() =>
        $"status={_status} offer={(_offer?.Version.ToString() ?? "none")} step={(_last?.ToString() ?? "none")} " +
        $"askAfter={(_get().UpdateAskAfter?.ToString("o") ?? "none")} next={_schedule.NextCheck:o} reachable={(Reachable ? "true" : "false")} " +
        $"installed={(Installed ? "true" : "false")} setupExit={(_setupExit?.ToString() ?? "none")} " +
        $"percent={(_percent?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none")} failure={(_failure?.ToString() ?? "none")} tries={_tries}";

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"update error: {e.GetType().Name}"); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _retry.Stop();
        _updater.Dispose();
    }
}
