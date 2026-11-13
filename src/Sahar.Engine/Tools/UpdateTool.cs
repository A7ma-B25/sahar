using Sahar.Core.Geometry;
using Sahar.Core.Settings;
using Sahar.Core.Update;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Tools;

/// <summary>
/// "في نسخة جديدة مني": checks on <see cref="UpdateSchedule"/> (2 min after start, then every 12 h) or from the tray
/// item, asks in her bubble with yes / later, and on yes downloads, verifies and starts the setup, then quits so
/// the installer can replace her (it starts her again). Never updates without your yes. Background failures are a
/// log line only; a step you started gets a short line from her.
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
    private readonly Action _quit;
    private readonly DispatcherQueueTimer _timer;
    private readonly UpdateSchedule _schedule = new(DateTimeOffset.Now);
    private UpdateOffer? _offer;
    private UpdateStep? _last;
    private string _status = "idle";

    /// <param name="automatic">Checks on the schedule (off in test mode and when the setting is off).</param>
    /// <param name="reachable">False in test mode without a fake API: no request ever leaves (CI never hits GitHub).</param>
    public UpdateTool(DispatcherQueue ui, Updater updater, ListBubble list, Func<RectF> body, Action<string> say,
        Func<SaharSettings> get, Action<SaharSettings> set, Func<bool> busy, Action quit, bool automatic, bool reachable)
    {
        (_ui, _updater, _list, _body, _say, _get, _set, _busy, _quit) = (ui, updater, list, body, say, get, set, busy, quit);
        Reachable = reachable;
        _timer = ui.CreateTimer();
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Tick += (_, _) => Safe(() => { if (_get().UpdateCheckEnabled && _schedule.Due(DateTimeOffset.Now)) Check(manual: false); });
        if (automatic && reachable) _timer.Start();
    }

    public bool Reachable { get; }

    /// <summary>Looks for a newer version now. <paramref name="manual"/>: you asked (the tray), so she always answers.</summary>
    public void Check(bool manual)
    {
        if (!Reachable || _status is "checking" or "downloading" or "installing") return;
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

    /// <summary>Yes: download, verify, start the setup, quit.</summary>
    public void Install()
    {
        if (_offer is not { } offer || _status is "downloading" or "installing") return;
        _status = "downloading";
        _say("عم نزّل النسخة الجديدة… ⏳");
        _ = _updater.DownloadAsync(offer).ContinueWith(t => _ui.TryEnqueue(() => Safe(() =>
        {
            string? path = t.IsCompletedSuccessfully ? t.Result : null;
            if (path is null || !Updater.Launch(path))
            {
                _status = "failed";
                _say("ما قدرت نزّل التحديث هلق، بجرّب بكرا");
                return;
            }
            _status = "installing";
            Log.Write($"update: installing {offer.Version}");
            _say("رح ارجع بعد شوي 💜");
            _quit();
        })), TaskScheduler.Default);
    }

    /// <summary>Test mode: what the update check is doing.</summary>
    public string Describe() =>
        $"status={_status} offer={(_offer?.Version.ToString() ?? "none")} step={(_last?.ToString() ?? "none")} " +
        $"askAfter={(_get().UpdateAskAfter?.ToString("o") ?? "none")} next={_schedule.NextCheck:o} reachable={(Reachable ? "true" : "false")}";

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
