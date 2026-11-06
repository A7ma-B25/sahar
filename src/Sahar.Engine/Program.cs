using Sahar.Core;

namespace Sahar.Engine;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            // WinExe has no console; attach to the parent's so the output is visible in a terminal.
            if (!Console.IsOutputRedirected) Native.AttachToParentConsole();
            Console.WriteLine($"Sahar {CoreInfo.Version}");
            return 0;
        }
        return 0;
    }
}
