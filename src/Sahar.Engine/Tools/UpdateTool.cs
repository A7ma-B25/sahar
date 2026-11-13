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
/// Never updates without your yes. Background failures are a log line only; a step you started gets a short line.
/// </summary>
internal sealed class UpdateTool : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly Updater _updater;
    private readonly ListBubble _list;
    private readonly Func<RectF> _body;
    private readonly Action<string> _say;
    private readonly Func<SaharSettings> _get;
    private readonly Action<SaharSettings> _set;
    private readonly Func<bool> _busy;
    private readonly string _marker;
    private readonly DispatcherQueueTimer _timer;
    private readonly UpdateSchedule _schedule = new(DateTimeOffset.Now);
    private UpdateOffer? _offer;
    private UpdateStep? _last;
    private string _status = "idle";
    private int? _setupExit;

    /// <param name="automatic">Checks on the schedule (off in test mode and when the setting is off).</param>
    /// <param name="reachable">False in test mode without a fake GitHub: no request ever leaves (CI never hits GitHub).</param>
    /// <param name="installed">She runs from the installer's folder (else portable: the wizard, not a silent setup).</param>
    /// <param name="marker">Where a portable her notes that she left for the wizard (in %LocalAppData%\Sahar).</param>
    public UpdateTool(DispatcherQueue ui, Updater updater, ListBubble list, Func<RectF> body, Action<string> say,
        Func<SaharSettings> get, Action<SaharSettings> set, Func<bool> busy, bool automatic, bool reachable, bool installed, string marker)
    {
        (_ui, _updater, _list, _body, _say, _get, _set, _busy, _marker) = (ui, updater, list, body, say, get, set, busy, marker);
        (Reachable, Installed) = (reachable, installed);
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (_, _) => Safe(() => { if (_get().UpdateCheckEnabled && _schedule.Due(DateTimeOffset.Now)) Check(manual: false); });
        if (automatic && reachable) _timer.Start();
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
        if (!Reachable || _status is "checking" or "downloading" or "installing") return;
        var now = DateTimeOffset.Now;
        if (UpdateSchedule.BeforeCheck(manual, _busy(), now, _get().UpdateAskAfter) is { } skip)
        {
            _last = skip;
            if (skip == UpdateStep.Later) _schedule.Busy(now); else _schedule.Checked(now);
            Log.Write($"update: {skip} (no request)");
            return;
        }
        _status = "checking";
        if (manual) _say("لحظة، عم دوّر… 🔎");
        _ = _updater.CheckAsync(_get().UpdateChannel).ContinueWith(t => _ui.TryEnqueue(() => Safe(() =>
        {
            var (offer, ok) = t.IsCompletedSuccessfully ? t.Result : (null, false);
            Checked(offer, ok, manual);
        })), TaskScheduler.Default);
    }

    private void Checked(UpdateOffer? offer, bool ok, bool manual)
    {
        var now = DateTimeOffset.Now;
        _status = ok ? "idle" : "failed";
        if (!ok)
        {
            if (manual) _say("ما قدرت وصل هلق، بجرّب بعدين");
            else _schedule.Failed(now);
            return;
        }
        if (!manual) _schedule.Checked(now);
        var step = UpdateSchedule.Decide(offer, manual, _busy(), now, _get().UpdateAskAfter);
        _last = step;
        Log.Write(offer is null ? $"update: {step}" : $"update: {step} {offer.Version}");
        switch (step)
        {
            case UpdateStep.Ask: Ask(offer!, now); break;
            case UpdateStep.UpToDate: _say("أنا آخر نسخة 💜"); break;
            case UpdateStep.Later: _schedule.Busy(now); break;
        }
    }

    private void Ask(UpdateOffer offer, DateTimeOffset now)
    {
        _offer = offer;
        _status = "asked";
        // Asking snoozes for a day: "later", Esc or a click elsewhere all mean "ask me tomorrow".
        _set(_get() with { UpdateAskAfter = UpdateSchedule.AskedAt(now) });
        _say($"في نسخة جديدة مني {offer.Version}، بدك ثبّتها؟");
        _list.Show(_body(), $"سَحَر {offer.Version} ✨", ["إي، ثبّتها هلق"], "بعدين",
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
        _say("عم نزّل النسخة الجديدة… ⏳");
        _ = _updater.DownloadAsync(offer).ContinueWith(t => _ui.TryEnqueue(() => Safe(() =>
        {
            var setup = t.IsCompletedSuccessfully ? t.Result : null;
            if (setup is null)
            {
                _status = "failed";
                _say("ما قدرت نزّل التحديث هلق، بجرّب بكرا");
                return;
            }
            if (!Installed) try { File.WriteAllText(_marker, UpdateRules.MoveMarker(DateTimeOffset.Now)); } catch (Exception) { }
            var process = Updater.Launch(setup, silent: Installed);
            if (process is null)
            {
                _status = "failed";
                Forget();
                _say("ما قدرت ثبّت التحديث هلق، بجرّب بكرا");
                return;
            }
            _status = "installing";
            Log.Write($"update: installing {offer.Version} ({(Installed ? "silent" : "wizard: portable")})");
            _say(Installed ? "عم ثبّت النسخة الجديدة، رح ارجع بعد شوي 💜"
                : "رح يطلعلك المثبّت ✨ بعدو بسكن بقائمة ابدأ: «سحر»");
            _ = process.WaitForExitAsync().ContinueWith(_ => _ui.TryEnqueue(() => Safe(() => SetupEnded(process))), TaskScheduler.Default);
        })), TaskScheduler.Default);
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
        _say($"ما زبط التحديث (رمز {code})، ضليت متل ما كنت 💜");
    }

    private void Forget()
    {
        if (!Installed) try { File.Delete(_marker); } catch (Exception) { }
    }

    /// <summary>Test mode: what the update check is doing.</summary>
    public string Describe() =>
        $"status={_status} offer={(_offer?.Version.ToString() ?? "none")} step={(_last?.ToString() ?? "none")} " +
        $"askAfter={(_get().UpdateAskAfter?.ToString("o") ?? "none")} next={_schedule.NextCheck:o} reachable={(Reachable ? "true" : "false")} " +
        $"installed={(Installed ? "true" : "false")} setupExit={(_setupExit?.ToString() ?? "none")}";

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"update error: {e.GetType().Name}"); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _updater.Dispose();
    }
}
