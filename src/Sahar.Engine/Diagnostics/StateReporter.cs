using Sahar.Core.Protocol;

namespace Sahar.Engine.Diagnostics;

/// <summary>Builds the engine snapshot the pipe's <c>state</c> command returns (tests read it).</summary>
internal sealed class StateReporter
{
    public string Expression { get; set; } = "idle";
    public Func<string> MotionName { get; set; } = () => "Hover";
    public bool MenuOpen { get; set; }
    public bool Hidden { get; set; }
    public bool Quiet { get; set; }
    /// <summary>Sahar's body rect in screen pixels.</summary>
    public Func<(float X, float Y, float Width, float Height)> Bounds { get; set; } = () => default;

    public EngineState Snapshot()
    {
        var (x, y, w, h) = Bounds();
        return new EngineState(Expression, MotionName(), MenuOpen, Hidden, Quiet, x, y, w, h, Native.WorkingSetBytes());
    }
}
