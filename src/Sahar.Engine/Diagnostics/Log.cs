namespace Sahar.Engine.Diagnostics;

/// <summary>A small log for machines we can't see. Never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 1 << 20;
    private static readonly object Gate = new();

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar");

    public static string FilePath => Path.Combine(Folder, "engine.log");

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var f = new FileInfo(FilePath);
            if (f.Exists && f.Length > MaxBytes) f.Delete();
        }
        catch
        {
            // Logging must never take the engine down.
        }
    }

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
                if (new FileInfo(FilePath).Length > MaxBytes) KeepNewestHalf();
            }
        }
        catch
        {
            // Logging must never take the engine down.
        }
    }

    /// <summary>Over 1 MB: keep the newest half, cut at a line start.</summary>
    private static void KeepNewestHalf()
    {
        var bytes = File.ReadAllBytes(FilePath);
        int start = bytes.Length / 2;
        while (start < bytes.Length && bytes[start - 1] != (byte)'\n') start++;
        File.WriteAllBytes(FilePath, bytes[start..]);
    }
}
