using Sahar.Core.Brain;
using Sahar.Core.Tools;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Windows.System;

namespace Sahar.Engine.Tools;

/// <summary>"Do not disturb" from her menu (spec §7.1, §8.2): her dnd face, quiet, and no big moves of her own.</summary>
internal sealed class DndTool
{
    private readonly DndState _state = new();
    private readonly Director _director;
    private readonly DispatcherQueueTimer _check;

    public DndTool(DispatcherQueue ui, Director director)
    {
        _director = director;
        _check = ui.CreateTimer();
        _check.Interval = TimeSpan.FromSeconds(30);
        _check.Tick += (_, _) =>
        {
            try { Check(); }
            catch (Exception e) { Log.Write($"dnd error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}"); }
        };
    }

    public bool On => _state.IsOn(DateTimeOffset.Now);
    /// <summary>Time left of a timed hush (null when off or until turned off).</summary>
    public TimeSpan? Remaining => On && _state.Until is { } u ? u - DateTimeOffset.Now : null;
    /// <summary>On until you say so.</summary>
    public bool UntilOff => On && _state.Until is null;

    /// <param name="length">null = until turned off.</param>
    public void TurnOn(TimeSpan? length)
    {
        _state.TurnOn(length, DateTimeOffset.Now);
        _director.SetDnd(true);
        _director.Post(EventKind.Dnd);
        if (length is not null) _check.Start();
        Log.Write(length is { } l ? FormattableString.Invariant($"dnd: on for {l.TotalMinutes:0} min") : "dnd: on until turned off");
    }

    public void TurnOff()
    {
        _state.TurnOff();
        _check.Stop();
        End("off");
    }

    /// <summary>A timed "do not disturb" that ran out (also after sleep) ends now.</summary>
    public void Check()
    {
        if (_state.TakeEnded(DateTimeOffset.Now)) { _check.Stop(); End("time is up"); }
    }

    private void End(string why)
    {
        _director.SetDnd(false);
        _director.Post(EventKind.DndOff);
        Log.Write($"dnd: {why}");
    }

    public string Describe() =>
        $"on={(On ? "true" : "false")} until={(_state.Until is { } u ? u.ToLocalTime().ToString("HH:mm") : "never")}";
}
