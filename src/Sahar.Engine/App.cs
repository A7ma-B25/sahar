using Sahar.Core.Geometry;
using Sahar.Core.Protocol;
using Sahar.Engine.Diagnostics;
using Sahar.Engine.Host;
using Sahar.Engine.Pipe;

namespace Sahar.Engine;

/// <summary>Wires the windows, the compositor and the pipe together and runs the message loop.</summary>
internal sealed unsafe class App
{
    private const float BodyDip = 80f;   // Medium; Task 9 reads it from settings
    private const float HeightRatio = 90f / 80f;

    private readonly EngineOptions _options;
    private readonly StateReporter _state = new();

    public App(EngineOptions options) => _options = options;

    public int Run()
    {
        var ui = CompositionHost.EnsureDispatcherQueue();
        var overlay = new OverlayWindow(_options.Capturable);
        using var host = new CompositionHost(overlay.Hwnd);
        var input = new InputWindow(_options.Capturable, StartRect());
        _state.Bounds = () => (input.Rect.Left, input.Rect.Top, input.Rect.Right - input.Rect.Left, input.Rect.Bottom - input.Rect.Top);

        using var pipe = new PipeServer(ui);
        pipe.Register("state", _ => new PipeReply(true, State: _state.Snapshot()));
        pipe.Register("quit", _ => new PipeReply(true), afterReply: () => Native.PostQuitMessage(0));
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

    /// <summary>Bottom-right of the primary work area, sized for the primary monitor's DPI.</summary>
    private static RectI StartRect()
    {
        var (_, work, dpi) = Native.MonitorAt(new POINT());
        int w = (int)MathF.Ceiling(BodyDip * dpi / 96f), h = (int)MathF.Ceiling(BodyDip * HeightRatio * dpi / 96f);
        int margin = (int)(24 * dpi / 96f);
        return new RectI(work.Right - w - margin, work.Bottom - h, work.Right - margin, work.Bottom);
    }
}
