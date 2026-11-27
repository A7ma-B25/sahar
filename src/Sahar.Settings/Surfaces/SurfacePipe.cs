using System.IO.Pipes;
using System.Text;
using Sahar.Core.Surfaces;

namespace Sahar.Settings.Surfaces;

/// <summary>
/// The host's end of <c>sahar-surfaces</c>: one engine at a time (current user only), one JSON line per message both
/// ways. Lines are handed to the UI thread; writes are serialised. When the engine has been gone for 15 s the host quits
/// (it never outlives her). Never logs a line (they carry what he typed).
/// </summary>
internal sealed class SurfacePipe(UiSyncContext ui, Action<SurfaceMsg> received)
{
    /// <summary>Review I7: one per Windows session (and current user only).</summary>
    public static readonly string Name = SurfacePipeName.For(System.Diagnostics.Process.GetCurrentProcess().SessionId);
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly SemaphoreSlim _write = new(1, 1);
    private StreamWriter? _writer;
    public bool Connected => _writer is not null;
    public event Action? Gone;
    /// <summary>The engine disconnected (it may come back).</summary>
    public event Action? Lost;

    public void Start() => _ = Task.Run(Loop);

    private async Task Loop()
    {
        var lonely = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // review I7: the name is taken this moment (an old pipe closing): wait, and give up with the rest
                SettingsLog.Write($"surfaces: pipe not made ({e.GetType().Name})");
                if (lonely.Elapsed > TimeSpan.FromSeconds(14)) { ui.Post(_ => Gone?.Invoke(), null); return; }
                await Task.Delay(500);
                continue;
            }
            using var owned = server;
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await server.WaitForConnectionAsync(wait.Token); }
            catch (OperationCanceledException)
            {
                if (lonely.Elapsed > TimeSpan.FromSeconds(14)) { ui.Post(_ => Gone?.Invoke(), null); return; }
                continue;
            }
            SettingsLog.Write("surfaces: engine connected");
            try
            {
                using var reader = new StreamReader(server, Utf8, false);
                _writer = new StreamWriter(server, Utf8) { NewLine = "\n", AutoFlush = true };
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (SurfaceJson.Read(line) is { } m) ui.Post(_ => received(m), null);
                }
            }
            catch (Exception e) { SettingsLog.Write($"surfaces: pipe ({e.GetType().Name})"); }
            _writer = null;
            SettingsLog.Write("surfaces: engine went away");
            ui.Post(_ => Lost?.Invoke(), null);
            lonely.Restart();
        }
    }

    public async void Send(SurfaceMsg m)
    {
        var w = _writer;
        if (w is null) return;
        await _write.WaitAsync();
        try { await w.WriteLineAsync(SurfaceJson.Write(m)); }
        catch (Exception e) { SettingsLog.Write($"surfaces: send failed ({e.GetType().Name})"); }
        finally { _write.Release(); }
    }
}
