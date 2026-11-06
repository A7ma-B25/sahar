using System.Globalization;
using System.Numerics;
using Sahar.Core.Art;
using Sahar.Core.Expressions;
using Sahar.Core.Geometry;
using Sahar.Core.Protocol;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Pipe;
using Sahar.Engine.Render;

namespace Sahar.Engine;

/// <summary>Wires the windows, the compositor and the pipe together and runs the message loop.</summary>
internal sealed unsafe class App
{
    private const float BodyDip = 80f;   // Medium; Task 12 reads it from settings

    private readonly EngineOptions _options;
    private readonly StateReporter _state = new();

    public App(EngineOptions options) => _options = options;

    public int Run()
    {
        var ui = CompositionHost.EnsureDispatcherQueue();
        var overlay = new OverlayWindow(_options.Capturable);
        using var host = new CompositionHost(overlay.Hwnd);
        var (_, work, dpi) = Native.MonitorAt(new POINT());
        var sahar = new SaharVisual(host, Rig.Load(), BodyDip, dpi / 96f)
        {
            Origin = new Vector2(overlay.Bounds.Left, overlay.Bounds.Top),
        };
        sahar.Position = StartPosition(sahar, work);
        IdleAnimations.Start(sahar);
        var player = new ExpressionPlayer(sahar);
        sahar.Rescaled += () =>
        {
            IdleAnimations.Start(sahar);
            player.Reapply();
        };
        host.DeviceLost += () =>
        {
            sahar.Redraw();
            player.Reapply();
        };

        var input = new InputWindow(_options.Capturable, sahar.BodyRect);
        _state.Bounds = () => (input.Rect.Left, input.Rect.Top, input.Rect.Right - input.Rect.Left, input.Rect.Bottom - input.Rect.Top);

        void MoveTo(Vector2 position)
        {
            sahar.Position = position;
            var body = sahar.BodyRect;
            var (_, _, d) = Native.MonitorAt(new POINT { X = (body.Left + body.Right) / 2, Y = (body.Top + body.Bottom) / 2 });
            sahar.SetSize(sahar.BodyDip, d / 96f);
            input.MoveTo(sahar.BodyRect);
        }
        overlay.DisplayChanged += () =>
        {
            sahar.Origin = new Vector2(overlay.Bounds.Left, overlay.Bounds.Top);
            MoveTo(sahar.Position);
        };

        using var pipe = new PipeServer(ui);
        pipe.Register("state", _ => new PipeReply(true, State: _state.Snapshot()));
        pipe.Register("quit", _ => new PipeReply(true), afterReply: () => Native.PostQuitMessage(0));
        pipe.Register("pose", r =>
        {
            MoveTo(new Vector2(Arg(r, "x"), Arg(r, "y")));
            return new PipeReply(true, State: _state.Snapshot());
        });
        pipe.Register("expression", r =>
        {
            if (r.Args is null || !r.Args.TryGetValue("key", out var key)) return new PipeReply(false, "expression needs 'key'");
            var preset = ExpressionCatalog.All.FirstOrDefault(e => e.Key == key);
            if (preset is null) return new PipeReply(false, "unknown expression");
            player.Play(preset);
            _state.Expression = preset.Key;
            return new PipeReply(true, State: _state.Snapshot());
        });
        if (_options.TestMode)
        {
            pipe.Register("debug-device-lost", _ =>
            {
                host.RecreateDevice();
                return new PipeReply(true);
            });
        }
        pipe.Start();
        Log.Write($"engine running (test mode {_options.TestMode}, capturable {_options.Capturable})");

        MSG msg;
        while (Native.GetMessage(&msg, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(&msg);
            Native.DispatchMessage(&msg);
        }
        Log.Write("message loop ended");
        Native.DestroyWindow(input.Hwnd);
        Native.DestroyWindow(overlay.Hwnd);
        return (int)msg.WParam;
    }

    /// <summary>Her canvas position so her body sits at the bottom-right of the work area.</summary>
    private static Vector2 StartPosition(SaharVisual v, RECT work)
    {
        var body = SaharVisual.BodyUnits;
        float margin = 24 * v.DpiScale;
        return new Vector2(
            work.Right - margin - (body.X + body.Width) * v.Scale,
            work.Bottom - (body.Y + body.Height) * v.Scale);
    }

    /// <summary>A numeric argument of a pipe command (screen pixels).</summary>
    private static float Arg(PipeRequest r, string name)
    {
        if (r.Args is null || !r.Args.TryGetValue(name, out var s) ||
            !float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"'{r.Name}' needs a numeric '{name}' argument");
        return v;
    }
}
