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
    public const string Name = "sahar-surfaces";
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly SemaphoreSlim _write = new(1, 1);
    private StreamWriter? _writer;
    public bool Connected => _writer is not null;
    public event Action? Gone;

    public void Start() => _ = Task.Run(Loop);

    private async Task Loop()
    {
        var lonely = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            using var server = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
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
