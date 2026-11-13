using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sahar.Core.Update;

/// <summary>One file of a GitHub release (its download URL is <c>browser_download_url</c>).</summary>
public sealed record ReleaseAsset(string Name, string Url, long Size);

/// <summary>The parts of a GitHub release the update check reads.</summary>
public sealed record Release(string Tag, bool Draft, IReadOnlyList<ReleaseAsset> Assets)
{
    /// <summary>A GitHub "get a release" answer, or null when it is not one (bad JSON, an error message).</summary>
    public static Release? Parse(string json)
    {
        ReleaseDto? dto;
        try { dto = JsonSerializer.Deserialize(json, UpdateJson.Default.ReleaseDto); }
        catch (JsonException) { return null; }
        if (dto?.TagName is not { } tag || dto.Assets is not { } assets) return null;
        return new Release(tag, dto.Draft, assets
            .Where(a => a.Name is { Length: > 0 } && a.BrowserDownloadUrl is { Length: > 0 })
            .Select(a => new ReleaseAsset(a.Name!, a.BrowserDownloadUrl!, a.Size))
            .ToList());
    }
}

/// <summary>An offer to update: a newer setup and the checksum file published beside it.</summary>
public sealed record UpdateOffer(SemVersion Version, ReleaseAsset Setup, ReleaseAsset Checksum);

internal sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
}

internal sealed class AssetDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

[JsonSerializable(typeof(ReleaseDto))]
internal sealed partial class UpdateJson : JsonSerializerContext;
