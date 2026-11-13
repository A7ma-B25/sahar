using System.Text.Json;
using Sahar.Core.Json;

namespace Sahar.Core.Settings;

/// <summary>Saving was refused: the file on disk could not be read, and writing would replace it with defaults.</summary>
public sealed class SettingsSaveRefusedException() : IOException("settings file unreadable: kept a .bad copy, not overwritten");

/// <summary>
/// Loads and saves settings. Never throws on a bad file: it keeps a ".bad" copy and returns defaults.
/// Two programs write this file (the engine and the settings app, owner ruling 2026-11-10): every save runs under one
/// cross-process named mutex, sharing violations are retried, and a file that exists but does not parse is never
/// overwritten — the save is refused and the ".bad" copy kept, so a broken file is never silently reset to defaults.
/// </summary>
public static class SettingsStore
{
    public const string MutexName = @"Local\Sahar.Settings.Store";
    public const int Tries = 6;
    private static readonly TimeSpan MutexWait = TimeSpan.FromSeconds(5);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sahar", "settings.json");

    // Files whose last load failed (every read attempt): the program runs on defaults, so a save would replace a
    // good file with them. Saves are refused until a load of that file succeeds.
    private static readonly HashSet<string> Failed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records that loading <paramref name="path"/> failed; saves to it are refused until a load succeeds.</summary>
    public static void LoadFailed(string path)
    {
        lock (Failed) Failed.Add(Path.GetFullPath(path));
    }

    private static bool LastLoadFailed(string path)
    {
        lock (Failed) return Failed.Contains(Path.GetFullPath(path));
    }

    public static SaharSettings Load(string path) => Load(path, File.ReadAllText);

    /// <summary>Loads with <paramref name="read"/> (tests inject failing reads). A read that keeps failing throws and is
    /// remembered (<see cref="LoadFailed"/>); an empty or blank file counts as missing.</summary>
    public static SaharSettings Load(string path, Func<string, string> read)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(path)) { Succeeded(full); return new SaharSettings(); }
        string text;
        // Reads take the same cross-process lock as saves: on Windows a read racing File.Replace hits a sharing
        // violation (the Windows CI run showed retries alone run out under many writers).
        using (Lock())
        {
        try { text = Retry(() => read(path)); }
        catch (FileNotFoundException) { Succeeded(full); return new SaharSettings(); }   // deleted meanwhile: missing
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LoadFailed(path);
            throw;
        }
        }
        Succeeded(full);
        if (string.IsNullOrWhiteSpace(text)) return new SaharSettings();
        if (Parse(text) is { } s) return s;
        KeepBad(path, text);
        return new SaharSettings();
    }

    private static void Succeeded(string full)
    {
        lock (Failed) Failed.Remove(full);
    }

    /// <summary>Writes "path.tmp" then swaps it in, so a crash never leaves a half-written file. Throws
    /// <see cref="SettingsSaveRefusedException"/> when the file on disk is unreadable.</summary>
    public static void Save(string path, SaharSettings settings)
    {
        if (LastLoadFailed(path)) throw new SettingsSaveRefusedException();
        using var gate = Lock();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path))
        {
            string current = Retry(() => File.ReadAllText(path));
            if (!string.IsNullOrWhiteSpace(current) && Parse(current) is null)
            {
                KeepBad(path, current);
                throw new SettingsSaveRefusedException();
            }
        }
        WriteLocked(path, settings);
    }

    /// <summary>Load → change → save, all under the cross-process lock: the way both programs save (plan 0.10c
    /// Ruling 4), so a change made by the other program meanwhile is never lost. Reads the file fresh (never a copy kept
    /// in memory); a missing or blank file starts from defaults. Returns what was saved. Throws
    /// <see cref="SettingsSaveRefusedException"/> when the file on disk is unreadable (a ".bad" copy is kept), and the IO
    /// error when every read attempt failed (remembered, as <see cref="Load(string)"/> does).</summary>
    public static SaharSettings Update(string path, Func<SaharSettings, SaharSettings> change)
    {
        string full = Path.GetFullPath(path);
        using var gate = Lock();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        SaharSettings current = new();
        if (File.Exists(path))
        {
            string text;
            try { text = Retry(() => File.ReadAllText(path)); }
            catch (FileNotFoundException) { text = ""; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LoadFailed(path);
                throw;
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (Parse(text) is not { } parsed)
                {
                    KeepBad(path, text);
                    throw new SettingsSaveRefusedException();
                }
                current = parsed;
            }
        }
        Succeeded(full);
        var next = change(current);
        WriteLocked(path, next);
        return next;
    }

    /// <summary>Like <see cref="Update"/>, but answers false (and the settings unchanged on disk) instead of throwing
    /// when the file cannot be read or written.</summary>
    public static bool TryUpdate(string path, Func<SaharSettings, SaharSettings> change, out SaharSettings? saved)
    {
        try
        {
            saved = Update(path, change);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            saved = null;
            return false;
        }
    }

    /// <summary>Whether the file can be used: missing, blank, or JSON settings. False for a file that does not parse
    /// (or cannot be read at all): the settings page then offers «نبلّش من جديد».</summary>
    public static bool Readable(string path)
    {
        if (!File.Exists(path)) return true;
        string text;
        try
        {
            using (Lock()) text = Retry(() => File.ReadAllText(path));
        }
        catch (FileNotFoundException) { return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        return string.IsNullOrWhiteSpace(text) || Parse(text) is not null;
    }

    /// <summary>«نبلّش من جديد» (owner ruling 2026-11-13): the unreadable file is kept aside as ".bad" and fresh defaults
    /// are written (she asks your name again: HelloDone is false). Only settings.json: notes and the rest stay.</summary>
    public static SaharSettings Reset(string path)
    {
        string full = Path.GetFullPath(path);
        using var gate = Lock();
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(path))
        {
            try
            {
                string text = Retry(() => File.ReadAllText(path));
                if (!string.IsNullOrWhiteSpace(text) && Parse(text) is null) KeepBad(path, text);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { File.Copy(path, path + ".bad", overwrite: true); } catch (Exception) { /* a courtesy */ }
            }
        }
        var fresh = new SaharSettings();
        WriteLocked(path, fresh);
        Succeeded(full);
        return fresh;
    }

    /// <summary>The settings as the file writes them (camelCase, indented).</summary>
    public static string ToJson(SaharSettings settings) => JsonSerializer.Serialize(settings, SettingsJson.Default.SaharSettings);

    /// <summary>Settings from JSON text; anything that is not settings JSON gives defaults (never throws).</summary>
    public static SaharSettings FromJson(string json) => string.IsNullOrWhiteSpace(json) ? new SaharSettings() : Parse(json) ?? new SaharSettings();

    // The caller holds the lock: "path.tmp" then swap it in, so a crash never leaves a half-written file.
    private static void WriteLocked(string path, SaharSettings settings)
    {
        string tmp = path + ".tmp";
        string json = ToJson(settings);
        Retry(() => { File.WriteAllText(tmp, json); return 0; });
        Retry(() =>
        {
            if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
            else File.Move(tmp, path);
            return 0;
        });
    }

    /// <summary>Like <see cref="Save"/>, but answers false instead of throwing when the save is refused.</summary>
    public static bool TrySave(string path, SaharSettings settings)
    {
        try
        {
            Save(path, settings);
            return true;
        }
        catch (SettingsSaveRefusedException)
        {
            return false;
        }
    }

    /// <summary>Runs <paramref name="io"/>, retrying sharing violations (another program has the file open for a
    /// moment) and access denied (Windows, while File.Replace swaps the file in) up to <see cref="Tries"/> times with a
    /// growing pause. Missing files and other errors are not retried.</summary>
    public static T Retry<T>(Func<T> io)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return io();
            }
            catch (Exception e) when ((e is IOException and not FileNotFoundException and not DirectoryNotFoundException
                                        and not SettingsSaveRefusedException || e is UnauthorizedAccessException) && attempt < Tries)
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }

    private static SaharSettings? Parse(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, SettingsJson.Default.SaharSettings) ?? new SaharSettings();
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void KeepBad(string path, string text)
    {
        try { File.WriteAllText(path + ".bad", text); }
        catch (IOException) { /* a copy is a courtesy; never fail the caller for it */ }
    }

    /// <summary>The cross-process gate. An abandoned mutex (the other program crashed while holding it) is ours now.</summary>
    private static Gate Lock()
    {
        var m = new Mutex(false, MutexName);
        bool owned;
        try { owned = m.WaitOne(MutexWait); }
        catch (AbandonedMutexException) { owned = true; }
        return new Gate(m, owned);
    }

    private readonly struct Gate(Mutex mutex, bool owned) : IDisposable
    {
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
