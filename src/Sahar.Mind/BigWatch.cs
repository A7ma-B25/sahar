using System.Security.Cryptography;
using Sahar.Core.Mind;
using Sahar.Core.Mind.Models;
using Sahar.Core.Mind.Protocol;
using Sahar.Core.Mind.Runtime;

namespace Sahar.Mind;

/// <summary>
/// 0.18 (owner: "he only downloads the file"): looks for a pinned model he downloaded with his browser — in his
/// Downloads folder and the models folder — when the Mind starts and whenever a .gguf lands there while it runs. A hit
/// is hashed on a low-priority thread; a match is moved into the models folder when on the same drive (else used where
/// it is), marked installed, and — the big brain only, once ever — made active. A bad file is never deleted (it is his)
/// and is not hashed again this session. Logs kinds only, never paths or names.
/// </summary>
internal sealed class BigWatch(ModelRegistry registry, Action<MindMsg> send, Func<MindMsg> models, Action switched) : IDisposable
{
    private readonly HashSet<(string, long, DateTime)> _bad = [];
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private int _busy;

    private static string Marker => Path.Combine(MindPaths.Models, "big-auto-used");

    public void Start()
    {
        string downloads = Native.Downloads();
        try
        {
            if (Directory.Exists(downloads))
            {
                _watcher = new FileSystemWatcher(downloads, "*.gguf") { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
                _watcher.Created += (_, _) => Soon();
                _watcher.Renamed += (_, _) => Soon();   // the browser renames .crdownload → .gguf at the end
                _watcher.Changed += (_, _) => Soon();
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { MindLog.Write($"big: no watcher ({e.GetType().Name})"); }
        Soon(TimeSpan.FromSeconds(5));
    }

    private void Soon(TimeSpan? after = null)
    {
        _debounce ??= new Timer(_ => _ = Task.Run(CheckAsync));
        _debounce.Change(after ?? TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
    }

    private async Task CheckAsync()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var pins = registry.List().Where(m => m.Origin == ModelOrigin.Recommended && !m.Installed).ToList();
            if (pins.Count == 0) return;
            foreach (var hit in BigFinder.Scan([Native.Downloads(), MindPaths.Models], pins))
            {
                var info = new FileInfo(hit.Path);
                var key = (hit.Path.ToLowerInvariant(), info.Length, info.LastWriteTimeUtc);
                if (_bad.Contains(key)) continue;
                bool big = hit.Pin.Id == ModelCatalog.BigId;
                MindLog.Write($"big: found {(big ? "big" : "pin")} file, checking");
                Report(hit.Pin, big ? "found" : "verifying");
                var sha = await HashAsync(hit.Path);
                if (!string.Equals(sha, hit.Pin.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    _bad.Add(key);
                    MindLog.Write($"big: {(sha is null ? "unreadable" : "mismatch")}");
                    Report(hit.Pin, "local-mismatch");
                    continue;
                }
                var path = Place(hit.Path, hit.Pin.File!);
                registry.MarkInstalled(hit.Pin.Id, path);
                bool use = (big && !File.Exists(Marker)) || registry.Active is null;
                if (use && big && Tiers.TooBig(Probe.Read(), hit.Pin.Bytes)) { MindLog.Write("big: too big for this RAM"); Report(hit.Pin, "too-big"); use = false; }
                if (use)
                {
                    registry.Use(hit.Pin.Id);
                    if (big) { try { File.WriteAllText(Marker, ""); } catch (IOException) { } }
                    switched();
                }
                MindLog.Write($"big: installed{(use ? ", active" : "")}");
                send(models());
                if (use) Report(hit.Pin, "done");
            }
        }
        catch (Exception e) { MindLog.Write($"big: check failed ({e.GetType().Name})"); }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private void Report(ModelEntry pin, string state) =>
        send(new MindMsg { T = "progress", Progress = new DownloadProgress(pin.Id, pin.File ?? "", 0, pin.Bytes, 0, state, "local") });

    /// <summary>SHA-256 on a below-normal thread (a 20 GB file: about a minute); null when it cannot be read (still open).</summary>
    private static Task<string?> HashAsync(string path)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan);
                tcs.SetResult(Convert.ToHexStringLower(SHA256.HashData(fs)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { tcs.SetResult(null); }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "big-hash" };
        t.Start();
        return tcs.Task;
    }

    /// <summary>Into the models folder when it is on the same drive (a rename: instant); otherwise where it is.</summary>
    private static string Place(string path, string file)
    {
        try
        {
            Directory.CreateDirectory(MindPaths.Models);
            var dest = Path.Combine(MindPaths.Models, file);
            if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase)) return path;
            if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), Path.GetPathRoot(Path.GetFullPath(dest)), StringComparison.OrdinalIgnoreCase) || File.Exists(dest)) return path;
            File.Move(path, dest);
            return dest;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return path; }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
