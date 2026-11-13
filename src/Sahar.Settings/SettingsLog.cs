namespace Sahar.Settings;

/// <summary>
/// %LocalAppData%\Sahar\settings.log (≤ 256 KB, then it starts over keeping the newer half). Same rules as engine.log:
/// never the name, a typed city, window titles or clipboard text — only what happened (kinds, counts, error types).
/// </summary>
internal static class SettingsLog
{
    public const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar");
    public static string FilePath => Path.Combine(Folder, "settings.log");

    public static void Write(string line)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var text = File.ReadAllText(FilePath);
                    File.WriteAllText(FilePath, text[(text.Length / 2)..]);
                }
                File.AppendAllText(FilePath, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // The log is a courtesy: never fail the app for it.
            }
        }
    }
}
