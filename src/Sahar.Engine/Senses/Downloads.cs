using Sahar.Core.Brain;
using Sahar.Core.Senses;
using Sahar.Engine.Diagnostics;
using Windows.System;

namespace Sahar.Engine.Senses;

/// <summary>
/// Finished downloads (spec §8.1): a browser's temp file renamed to its real name, or a file written straight to its
/// name that stopped growing for 2 s. At most one "download" face per 10 s. Only file names are looked at.
/// </summary>
internal sealed class Downloads : IDisposable
{
    private readonly DispatcherQueue _ui;
    private readonly Action<EventKind> _post;
    private readonly FileSystemWatcher? _watcher;
    private readonly Dictionary<string, (long Size, DateTimeOffset Since)> _growing = [];
    private readonly DispatcherQueueTimer _settle;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    public Downloads(DispatcherQueue ui, Action<EventKind> post, string? folder)
    {
        _ui = ui;
        _post = post;
        _settle = ui.CreateTimer();
        _settle.Interval = TimeSpan.FromSeconds(1);
        _settle.Tick += (_, _) => Safe(Settle);
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try
        {
            if (!Directory.Exists(folder)) { Log.Write("downloads: no Downloads folder"); return; }
            _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size };
            _watcher.Renamed += (_, e) => _ui.TryEnqueue(() => Safe(() => Renamed(e.OldName ?? "", e.Name ?? "", e.FullPath)));
            _watcher.Created += (_, e) => _ui.TryEnqueue(() => Safe(() => Touched(e.Name ?? "", e.FullPath)));
            _watcher.Changed += (_, e) => _ui.TryEnqueue(() => Safe(() => Touched(e.Name ?? "", e.FullPath)));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) { Log.Write($"downloads: not watched ({e.GetType().Name})"); }
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Log.Write($"downloads error: {e.GetType().Name}"); }
    }

    private void Renamed(string oldName, string newName, string path)
    {
        _growing.Remove(Path.Combine(Path.GetDirectoryName(path) ?? "", oldName));
        if (DownloadRules.FinishedByRename(oldName, newName)) Done();
    }

    private void Touched(string name, string path)
    {
        if (DownloadRules.IsTemp(name) || !File.Exists(path)) return;
        _growing[path] = (Size(path), DateTimeOffset.Now);
        _settle.Start();
    }

    private void Settle()
    {
        var now = DateTimeOffset.Now;
        foreach (var (path, (size, since)) in _growing.ToList())
        {
            if (!File.Exists(path)) { _growing.Remove(path); continue; }
            long s = Size(path);
            if (s != size) { _growing[path] = (s, now); continue; }
            if (now - since < TimeSpan.FromSeconds(2)) continue;
            _growing.Remove(path);
            var siblings = Directory.EnumerateFiles(Path.GetDirectoryName(path) ?? ".").Select(Path.GetFileName).OfType<string>();
            if (DownloadRules.Settled(Path.GetFileName(path), s, siblings)) Done();
        }
        if (_growing.Count == 0) _settle.Stop();
    }

    private static long Size(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return -1; }
        catch (UnauthorizedAccessException) { return -1; }
    }

    private void Done()
    {
        var now = DateTimeOffset.Now;
        if (now - _last < TimeSpan.FromSeconds(10)) return;
        _last = now;
        Log.Write("downloads: finished");
        _post(EventKind.DownloadDone);
    }

    public void Dispose() => _watcher?.Dispose();
}
