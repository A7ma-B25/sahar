namespace Sahar.Settings;

/// <summary>Command line: <c>--test-mode</c> (the test pipe, DevTools, no message boxes), <c>--webview-data &lt;dir&gt;</c>
/// (test mode only), <c>--snapshot-dir &lt;dir&gt;</c> (where test snapshots and captures go).</summary>
internal sealed record Options(bool TestMode, string? WebViewData, string? SnapshotDir)
{
    public static Options Parse(string[] args)
    {
        bool test = args.Contains("--test-mode");
        string? Value(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        return new Options(test, test ? Value("--webview-data") : null, Value("--snapshot-dir"));
    }
}
