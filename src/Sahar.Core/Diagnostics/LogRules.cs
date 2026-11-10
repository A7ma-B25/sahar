using System.Text.RegularExpressions;

namespace Sahar.Core.Diagnostics;

/// <summary>Small rules for engine.log (the owner's log, Nov 7–10): did the last run end cleanly, and which waiting
/// reasons are "the same" (a new "next turn at" time is not news).</summary>
/// <summary>Says each thing once: a repeating problem (a settings file that cannot be saved) is logged one time.</summary>
public sealed class OneShot
{
    private readonly HashSet<string> _said = [];

    public bool First(string key)
    {
        lock (_said) return _said.Add(key);
    }
}

public static partial class LogRules
{
    [GeneratedRegex(@"\s(exit -?\d+|session: end)\s*$")]
    private static partial Regex ExitLine();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}")]
    private static partial Regex Stamp();

    /// <summary>A run ends with "exit N", or with "session: end" (Windows shut down or signed out; nothing runs after
    /// it); an empty log (first run) counts as clean.</summary>
    public static bool EndedCleanly(string? lastLine) => string.IsNullOrWhiteSpace(lastLine) || ExitLine().IsMatch(lastLine);

    /// <summary>The time at the start of a log line (never the rest of it), or null.</summary>
    public static string? Timestamp(string line) => Stamp().Match(line) is { Success: true } m ? m.Value : null;

    /// <summary>What kind of waiting this is: "next turn at 12:00:01" and "next turn at 12:07:40" are one kind.</summary>
    public static string WaitKind(string reason) => reason.StartsWith("next turn at ", StringComparison.Ordinal) ? "next turn" : reason;
}
