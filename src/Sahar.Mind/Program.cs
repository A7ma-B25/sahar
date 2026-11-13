namespace Sahar.Mind;

/// <summary>
/// Sahar.Mind.exe (stream A owns this project): <c>--pipe &lt;name&gt; --parent &lt;pid&gt;</c>. Connects to the engine's
/// private pipe, runs the turn loop (stream C, through <see cref="Sahar.Core.Mind.MindParts"/>) on the model runtime,
/// and exits when idle or when the engine goes. Seam stub: exits at once.
/// </summary>
internal static class Program
{
    private static int Main(string[] args) => args.Contains("--version") ? Version() : 0;

    private static int Version()
    {
        Console.WriteLine($"Sahar.Mind {Sahar.Core.CoreInfo.Version}");
        return 0;
    }
}
