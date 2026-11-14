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

    /// <summary>The start line's arguments: her switches (<c>--…</c>) as they are, every value as «…» (a value can be a
    /// path holding the user name, or an address).</summary>
    public static string Args(IReadOnlyList<string> args) =>
        string.Join(' ', args.Select(a => a.StartsWith('-') ? a : "…"));

    /// <summary>The time at the start of a log line (never the rest of it), or null.</summary>
    public static string? Timestamp(string line) => Stamp().Match(line) is { Success: true } m ? m.Value : null;

    /// <summary>An exception for a log line: its type and its inner one's, never the message (messages carry paths, and a
    /// path carries the user name: review fix 2026-11-13).</summary>
    public static string Kind(Exception e) => e.InnerException is { } inner ? $"{e.GetType().Name} ← {inner.GetType().Name}" : e.GetType().Name;

    /// <summary><see cref="Kind"/> and the stack (method names; never the message).</summary>
    public static string Describe(Exception e) => e.StackTrace is { Length: > 0 } st ? $"{Kind(e)}{Environment.NewLine}{st}" : Kind(e);

    /// <summary>What kind of waiting this is: "next turn at 12:00:01" and "next turn at 12:07:40" are one kind.</summary>
    public static string WaitKind(string reason) => reason.StartsWith("next turn at ", StringComparison.Ordinal) ? "next turn" : reason;
}
