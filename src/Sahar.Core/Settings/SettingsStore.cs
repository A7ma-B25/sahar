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
        string tmp = path + ".tmp";
        string json = JsonSerializer.Serialize(settings, SettingsJson.Default.SaharSettings);
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
        catch (JsonException)
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
