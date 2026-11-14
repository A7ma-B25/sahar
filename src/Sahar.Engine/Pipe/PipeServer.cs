using System.IO.Pipes;
using System.Text;
using Sahar.Core.Protocol;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Pipe;

/// <summary>
/// The engine's control pipe: one JSON request per line in, one JSON reply per line out. Connections are
/// served on background threads; every handler runs on the UI thread through the dispatcher queue.
/// </summary>
internal sealed class PipeServer : IDisposable
{
    public const string Name = "sahar";
    private const int MaxInstances = 4;
    private static readonly TimeSpan HandlerTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AsyncTimeout = TimeSpan.FromSeconds(10);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly DispatcherQueue _ui;
    private readonly Dictionary<string, (Func<PipeRequest, PipeReply> Handle, Action? After)> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<PipeRequest, Task<PipeReply>>> _async = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private Func<PipeRequest, PipeReply>? _onEvent;

    public PipeServer(DispatcherQueue ui) => _ui = ui;

    /// <summary>Handles <c>{"type":"cmd","name":cmd}</c>. <paramref name="afterReply"/> runs on the UI thread once the reply is sent.</summary>
    public void Register(string cmd, Func<PipeRequest, PipeReply> handler, Action? afterReply = null) =>
        _commands[cmd] = (handler, afterReply);

    /// <summary>A command whose answer takes time (the settings app's city search): <paramref name="handler"/> is started
    /// on the UI thread and awaited off it, up to 10 s.</summary>
    public void RegisterAsync(string cmd, Func<PipeRequest, Task<PipeReply>> handler) => _async[cmd] = handler;

    /// <summary>Handles every <c>{"type":"event",...}</c> request.</summary>
    public void OnEvent(Func<PipeRequest, PipeReply> handler) => _onEvent = handler;

    public void Start() => _ = Task.Run(AcceptLoop);

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = new NamedPipeServerStream(Name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                // All instances busy: wait for one to free up.
                await Delay(200);
                continue;
            }
            try
            {
                await server.WaitForConnectionAsync(_stop.Token);
            }
            catch (Exception e)
            {
                await server.DisposeAsync();
                if (_stop.IsCancellationRequested) return;
                Log.Write($"pipe accept failed: {Sahar.Core.Diagnostics.LogRules.Kind(e)}");
                await Delay(200);
                continue;
            }
            _ = Task.Run(() => Serve(server));
        }
    }

    private async Task Serve(NamedPipeServerStream stream)
    {
        try
        {
            await using (stream)
            {
                using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false);
                await using var writer = new StreamWriter(stream, Utf8) { NewLine = "\n", AutoFlush = true };
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.Length == 0) continue;
                    var (reply, after) = await Handle(line);
                    await writer.WriteLineAsync(ProtocolJson.Write(reply));
                    if (after != null) _ui.TryEnqueue(() => after());
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Client went away or the engine is stopping.
        }
        catch (Exception e)
        {
            Log.Write($"pipe connection error: {Sahar.Core.Diagnostics.LogRules.Describe(e)}");
        }
    }

    private async Task<(PipeReply, Action?)> Handle(string line)
    {
        PipeRequest request;
        try
        {
            request = ProtocolJson.ParseRequest(line);
        }
        catch (FormatException e)
        {
            return (new PipeReply(false, e.Message), null);
        }

        if (request.Type == "cmd" && _async.TryGetValue(request.Name, out var slow)) return (await HandleAsync(request, slow), null);

        Func<PipeRequest, PipeReply>? handle;
        Action? after = null;
        if (request.Type == "event") handle = _onEvent;
        else if (_commands.TryGetValue(request.Name, out var c)) (handle, after) = c;
        else handle = null;
        if (handle == null) return (new PipeReply(false, $"unknown {request.Type} '{request.Name}'"), null);

        var done = new TaskCompletionSource<PipeReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _ui.TryEnqueue(() =>
        {
            try { done.SetResult(handle(request)); }
            catch (Exception e)
            {
                Log.Write($"pipe handler '{request.Name}' failed: {Sahar.Core.Diagnostics.LogRules.Describe(e)}");
                done.SetResult(new PipeReply(false, e.Message));
            }
        });
        if (!queued) return (new PipeReply(false, "engine is shutting down"), null);
        try
        {
            return (await done.Task.WaitAsync(HandlerTimeout, _stop.Token), after);
        }
        catch (TimeoutException)
        {
            return (new PipeReply(false, $"'{request.Name}' timed out"), null);
        }
    }

    private async Task<PipeReply> HandleAsync(PipeRequest request, Func<PipeRequest, Task<PipeReply>> handler)
    {
        var started = new TaskCompletionSource<Task<PipeReply>>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = _ui.TryEnqueue(() =>
        {
            try { started.SetResult(handler(request)); }
            catch (Exception e) { started.SetException(e); }
        });
        if (!queued) return new PipeReply(false, "engine is shutting down");
        try
        {
            var work = await started.Task.WaitAsync(HandlerTimeout, _stop.Token);
            return await work.WaitAsync(AsyncTimeout, _stop.Token);
        }
        catch (TimeoutException)
        {
            return new PipeReply(false, $"'{request.Name}' timed out");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Write($"pipe handler '{request.Name}' failed: {e.GetType().Name}");
            return new PipeReply(false, e.GetType().Name);
        }
    }

    private async Task Delay(int ms)
    {
        try { await Task.Delay(ms, _stop.Token); }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => _stop.Cancel();
}
