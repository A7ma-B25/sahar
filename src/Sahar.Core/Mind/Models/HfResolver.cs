using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Models;

/// <summary>An added model, pinned: the repo's current commit and the chosen file's LFS SHA-256 and size; or the
/// reason in her words (gated, not found, no GGUF, blocked).</summary>
public sealed record HfResolved(string? Owner, string? Repo, string? Commit, string? File, long Bytes, string? Sha256, string? License, string? Reason);

/// <summary>
/// Hugging Face's public API (huggingface.co/api/models/…), read once when he adds a model: the commit, then the file
/// list at that commit. The file: the one he named, else his quant hint, else Q4_K_M → Q4_K_S → IQ4_XS → Q5_K_M →
/// Q4_0 → the smallest; never mmproj or split parts. Sends nothing but the repo name.
/// </summary>
public sealed partial class HfResolver(HttpMessageHandler handler)
{
    public const string Gated = "هالنموذج بدّه موافقة على صفحته بـ Hugging Face، وأنا ما بقدر وافق عنك. جرّب واحد تاني.";
    public const string NotFound = "ما لقيت هالنموذج على Hugging Face. تأكّد من الاسم.";
    public const string NoGguf = "ما في ملف GGUF بهالنموذج بقدر شغّله.";
    public const string Blocked = "ما قدرت وصل لـ Hugging Face من نتّك. إذا عندك VPN شغّله وجرّب كمان مرة.";

    private static readonly string[] Preferred = ["Q4_K_M", "Q4_K_S", "IQ4_XS", "Q5_K_M", "Q4_0"];
    private readonly HttpClient _http = new(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(15) };

    [GeneratedRegex(@"-\d{5}-of-\d{5}\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex Split();

    public async Task<HfResolved> ResolveAsync(HfRef r, CancellationToken ct)
    {
        try
        {
            using var model = await _http.GetAsync($"https://huggingface.co/api/models/{r.Owner}/{r.Repo}{(r.Revision is { } rev ? "/revision/" + rev : "")}", ct);
            if (!model.IsSuccessStatusCode) return Fail((int)model.StatusCode is 401 or 403 ? Gated : NotFound);
            using var info = JsonDocument.Parse(await model.Content.ReadAsStringAsync(ct));
            var root = info.RootElement;
            if (root.TryGetProperty("gated", out var g) && g.ValueKind is JsonValueKind.String or JsonValueKind.True) return Fail(Gated);
            var commit = root.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
            if (commit is null || !Regex.IsMatch(commit, "^[0-9a-f]{40}$")) return Fail(NotFound);
            string? license = root.TryGetProperty("cardData", out var card) && card.ValueKind == JsonValueKind.Object && card.TryGetProperty("license", out var l) ? l.GetString() : null;
            using var tree = await _http.GetAsync($"https://huggingface.co/api/models/{r.Owner}/{r.Repo}/tree/{commit}", ct);
            if (!tree.IsSuccessStatusCode) return Fail(NotFound);
            using var files = JsonDocument.Parse(await tree.Content.ReadAsStringAsync(ct));
            var ggufs = files.RootElement.EnumerateArray()
                .Where(f => f.TryGetProperty("path", out var p) && p.GetString() is { } path && path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains('/') && !path.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase) && !Split().IsMatch(path)
                    && f.TryGetProperty("lfs", out var lfs) && lfs.TryGetProperty("oid", out _))
                .Select(f => (Path: f.GetProperty("path").GetString()!, Size: f.GetProperty("lfs").GetProperty("size").GetInt64(), Sha: f.GetProperty("lfs").GetProperty("oid").GetString()!))
                .ToList();
            var pick = r.File is { } named ? ggufs.FirstOrDefault(f => f.Path.Equals(named, StringComparison.OrdinalIgnoreCase))
                : r.Quant is { } q ? ggufs.FirstOrDefault(f => ModelRegistry.QuantOf(f.Path).Equals(q, StringComparison.OrdinalIgnoreCase))
                : Preferred.Select(p => ggufs.FirstOrDefault(f => ModelRegistry.QuantOf(f.Path) == p)).FirstOrDefault(f => f.Path is not null);
            if (pick.Path is null && r.File is null && r.Quant is null) pick = ggufs.OrderBy(f => f.Size).FirstOrDefault();
            if (pick.Path is null || !Regex.IsMatch(pick.Sha, "^[0-9a-f]{64}$")) return Fail(NoGguf);
            return new HfResolved(r.Owner, r.Repo, commit, pick.Path, pick.Size, pick.Sha, license, null);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Fail(e is JsonException or InvalidOperationException or KeyNotFoundException ? NotFound : Blocked);
        }
    }

    private static HfResolved Fail(string reason) => new(null, null, null, null, 0, null, null, reason);
}
