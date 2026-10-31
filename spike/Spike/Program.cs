using System.Diagnostics;

namespace Spike;

internal static unsafe class Program
{
    private const nuint LookTimer = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string? mode = ParseMode(args) ?? AskMode();
            if (mode == null) return 0;
            Log.Write($"start mode {mode}, {Environment.OSVersion}, {Environment.ProcessorCount} cores");
            Run(mode);
            Log.Write("exit");
            return 0;
        }
        catch (Exception e)
        {
            Log.Write($"fatal: {e}");
            fixed (char* t = $"The spike stopped:\n\n{e.Message}\n\nDetails are in {Log.Folder}\\log.txt")
            fixed (char* c = "Desk Companion spike")
                Native.MessageBox(0, t, c, 0x10);
            return 1;
        }
    }

    private static string? ParseMode(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] is "--mode" or "-m")
                return args[i + 1].ToUpperInvariant() switch { "A" => "A", "B" => "B", _ => null };
        return null;
    }

    private static string? AskMode()
    {
        const string text = "Which drawing approach?\n\nYes = A (Windows Composition)\nNo = B (SkiaSharp)\nCancel = quit";
        fixed (char* t = text)
        fixed (char* c = "Desk Companion spike")
            return Native.MessageBox(0, t, c, Native.MB_YESNOCANCEL | Native.MB_ICONQUESTION) switch
            {
                Native.IDYES => "A",
                Native.IDNO => "B",
                _ => null,
            };
    }

    private static void Run(string mode)
    {
        float scale = Native.GetDpiForSystem() / 96f;
        var blob = new Blob(28f * scale);
        var hud = new Hud(mode);
        IBlobView view = mode == "A" ? new CompositionView(blob) : new SkiaView(blob);
        blob.Attach(view);
        view.SetExcludedFromCapture(true);
        Log.Write($"notification state at start: {Native.NotificationState()}");

        bool visible = true;
        var ticker = new Ticker(blob, view, mode);
        hud.OnTick = _ =>
        {
            string? reason = Native.FullscreenReason();
            bool show = reason == null;
            hud.HiddenReason = reason;
            if (show != visible)
            {
                visible = show;
                view.SetVisible(show);
                Log.Write(show ? "shown" : $"hidden: {reason}");
            }
            blob.RefreshWorkArea();
        };

        MSG msg;
        long last = 0;
        while (true)
        {
            if (visible && blob.NeedsFrames)
            {
                while (Native.PeekMessage(&msg, 0, 0, 0, Native.PM_REMOVE) != 0)
                {
                    if (msg.Message == Native.WM_QUIT) return;
                    Native.TranslateMessage(&msg);
                    Native.DispatchMessage(&msg);
                }
                long now = Stopwatch.GetTimestamp();
                float dt = last == 0 ? 1f / 60f : (float)Stopwatch.GetElapsedTime(last, now).TotalSeconds;
                last = now;
                blob.Step(dt);
                if (view.Animating) view.IdleTick(dt);
                hud.SetState("moving");
                hud.CountFrame();
                // Wait for the next composition frame (vsync). If DWM can't (remote desktop, odd drivers),
                // sleep instead of spinning a core.
                if (Native.DwmFlush() != 0) Thread.Sleep(8);
            }
            else
            {
                last = 0;
                hud.SetState(!visible ? "hidden" : blob.Dragging ? "dragging" : "idle");
                int r = Native.GetMessage(&msg, 0, 0, 0);
                if (r <= 0) return;
                Native.TranslateMessage(&msg);
                Native.DispatchMessage(&msg);
            }
        }
    }

    /// <summary>
    /// A hidden window whose timer turns the pupils toward the cursor (10 Hz, 30 Hz when the cursor
    /// is near) and, in mode B, redraws for breathing and blinking (30 Hz).
    /// </summary>
    private sealed class Ticker : IWindowHandler
    {
        private readonly Blob _blob;
        private readonly IBlobView _view;
        private readonly bool _redrawsWhileIdle;
        private readonly nint _hwnd;
        private uint _interval;
        private readonly long _t0 = Stopwatch.GetTimestamp();

        public Ticker(Blob blob, IBlobView view, string mode)
        {
            _blob = blob;
            _view = view;
            _redrawsWhileIdle = mode == "B";
            _hwnd = Win.Create(this, Native.WS_EX_TOOLWINDOW, Native.WS_POPUP, 0, 0, 0, 0, "spike ticker");
            SetInterval(_redrawsWhileIdle ? 33u : 100u);
        }

        private void SetInterval(uint ms)
        {
            if (ms == _interval) return;
            _interval = ms;
            Native.SetTimer(_hwnd, LookTimer, ms, 0);
        }

        public nint? OnMessage(nint hwnd, uint msg, nuint wParam, nint lParam)
        {
            if (msg != Native.WM_TIMER || wParam != LookTimer) return null;
            bool near = _blob.TrackCursor();
            if (_redrawsWhileIdle)
            {
                if (!_blob.NeedsFrames) _view.IdleTick(Stopwatch.GetElapsedTime(_t0).TotalSeconds);
            }
            else
            {
                SetInterval(near ? 33u : 100u);
            }
            return 0;
        }
    }
}
