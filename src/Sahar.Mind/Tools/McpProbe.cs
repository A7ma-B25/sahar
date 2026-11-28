namespace Sahar.Mind.Tools;

/// <summary><c>Sahar.Mind.exe --mcp-probe &lt;url|exe&gt;</c>: connects to one MCP server, lists its tools and prints
/// <c>tools=&lt;n&gt;</c> (or <c>error=&lt;kind&gt;</c>) — numbers and kinds only, never a tool's name or text.</summary>
internal static class McpProbe
{
    public static int Run(string target)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            int n = RunAsync(target, cts.Token).GetAwaiter().GetResult();
            Console.WriteLine($"tools={n}");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine($"error={e.GetType().Name}");
            return 1;
        }
    }

    private static async Task<int> RunAsync(string target, CancellationToken ct)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
        {
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = !u.IsLoopback };
            await using var link = await McpLink.HttpAsync(u, new Dictionary<string, string>(), handler, ct);
            return (await link.ListAsync(ct)).Count;
        }
        using var p = JobProcess.Start(Path.GetFullPath(target), [], new Dictionary<string, string>());
        await using var stdio = await McpLink.StreamAsync(p.Input, p.Output, ct);
        return (await stdio.ListAsync(ct)).Count;
    }
}
