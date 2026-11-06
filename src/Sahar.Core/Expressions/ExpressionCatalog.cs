using Sahar.Core.Art;
using Sahar.Core.Json;

namespace Sahar.Core.Expressions;

/// <summary>Idle (id 0) plus the 25 v1 expressions (spec §5), from assets/expressions.json.</summary>
public static class ExpressionCatalog
{
    public static IReadOnlyList<ExpressionPreset> All { get; } = Load();

    private static readonly Dictionary<string, ExpressionPreset> ByKey = All.ToDictionary(x => x.Key);

    public static ExpressionPreset Get(string key) =>
        ByKey.TryGetValue(key, out var p) ? p : throw new KeyNotFoundException($"unknown expression '{key}'");

    private static List<ExpressionPreset> Load() =>
        Assets.Read("expressions.json", CoreJson.Default.ListExpressionDto)
            .Select(d => new ExpressionPreset(
                d.Id, d.Key, Enum.Parse<Mood>(d.Mood), Enum.Parse<Eyes>(d.Eyes), Enum.Parse<Mouth>(d.Mouth), d.OrbGlyph,
                d.OrbTop is null ? null : Rgba.Parse(d.OrbTop), d.OrbBottom is null ? null : Rgba.Parse(d.OrbBottom),
                Enum.Parse<Extras>(d.Extras)))
            .ToList();
}

public sealed record ExpressionDto(
    int Id, string Key, string Mood, string Eyes, string Mouth, string? OrbGlyph, string? OrbTop, string? OrbBottom, string Extras);
