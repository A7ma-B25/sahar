namespace Sahar.Core.Mind.Import;

/// <summary>«اكتبلي أمثلة» (spec §7.5): sentences he writes the way he talks to friends, one per line.</summary>
public static class Examples
{
    public const int MaxLines = 50;

    public static IReadOnlyList<string> Lines(string? text) =>
        (text ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).Take(MaxLines).ToList();
}
