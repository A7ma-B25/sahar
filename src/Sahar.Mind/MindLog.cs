namespace Sahar.Mind;

/// <summary>mind.log beside engine.log: kinds, counts and timings only — never a prompt, a reply, a title, a file
/// name or a path (spec §3.10.9). One file, cut to its last half at 1 MB.</summary>
internal static class MindLog
{
    private static readonly object Gate = new();
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("SAHAR_MIND_HOME") is { Length: > 0 } h ? h : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sahar"),
        "mind.log");

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 1 << 20)
                {
                    var keep = File.ReadAllText(Path);
                    File.WriteAllText(Path, keep[(keep.Length / 2)..]);
                }
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
