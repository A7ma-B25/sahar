using System.Globalization;
using System.Numerics;
using Sahar.Core.Motion;
using Sahar.Engine.Render;
using Windows.System;

namespace Sahar.Engine.Life;

/// <summary>
/// Her small idle acts (spec §4): every 6–12 s while she rests, a little hop or a glance to one side for 1.2 s, then
/// back to watching the pointer. One timer; off on a test machine until the pipe turns it on.
/// </summary>
internal sealed class SoulDirector : IDisposable
{
    private readonly SoulLayer _soul;
    private readonly Func<SoulIdleKind, bool> _mayAct;
    private readonly DispatcherQueueTimer _timer, _glance;
    private readonly Random _rng = new();
    private (Vector2 Units, float Lean)? _beforeGlance;
    private DateTimeOffset _next;

    /// <param name="mayAct">Whether this act may happen now (<see cref="SoulIdle.MayAct"/> with the app's state).</param>
    public SoulDirector(DispatcherQueue ui, SoulLayer soul, Func<SoulIdleKind, bool> mayAct)
    {
        _soul = soul;
        _mayAct = mayAct;
        _timer = ui.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Safe(() => Act(null));
        _glance = ui.CreateTimer();
        _glance.IsRepeating = false;
        _glance.Interval = TimeSpan.FromSeconds(SoulIdle.LookSeconds);
        _glance.Tick += (_, _) => Safe(() => { if (_beforeGlance is { } b) _soul.Look(b.Units, b.Lean); _beforeGlance = null; });
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; if (value) Schedule(); else _timer.Stop(); }
    }
    private bool _enabled;

    /// <summary>Test mode: a fixed gap instead of 6–12 s.</summary>
    public TimeSpan? GapOverride { get; set; }
    public int Acts { get; private set; }

    /// <summary>Acts now (a given kind, or her own choice), if she rests.</summary>
    public void ActNow(SoulIdleKind? kind = null) => Act(kind);

    private void Act(SoulIdleKind? forced)
    {
        try
        {
            if (!Enabled && forced is null) return;
            var (_, kind) = SoulIdle.Next(_rng);
            kind = forced ?? kind;
            if (!_mayAct(kind)) return;
            switch (kind)
            {
                case SoulIdleKind.Hop:
                    if (_soul.Hop(SoulIdle.HopPower)) Acts++;
                    break;
                default:
                    _beforeGlance ??= (_soul.LookTarget, _soul.LeanTarget);
                    _soul.Look(new Vector2(kind == SoulIdleKind.LookLeft ? -SoulMath.LookUnits.X : SoulMath.LookUnits.X, 0), 0);
                    _glance.Stop();
                    _glance.Start();
                    Acts++;
                    break;
            }
        }
        finally
        {
            if (Enabled) Schedule();
        }
    }

    private void Schedule()
    {
        var wait = GapOverride ?? SoulIdle.Next(_rng).Wait;
        _next = DateTimeOffset.Now + wait;
        _timer.Stop();
        _timer.Interval = wait;
        _timer.Start();
    }

    public string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"enabled={(Enabled ? "true" : "false")} acts={Acts} next={(Enabled ? Math.Max(0, (_next - DateTimeOffset.Now).TotalSeconds) : 0):0.0}");

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Sahar.Engine.Diagnostics.Log.Write($"soul error: {e.GetType().Name}"); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _glance.Stop();
    }
}
