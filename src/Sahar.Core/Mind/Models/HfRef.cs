using System.Text.RegularExpressions;

namespace Sahar.Core.Mind.Models;

/// <summary>
/// «زيدي نموذج» from Hugging Face: a link (huggingface.co or hf.co, https only), <c>owner/repo</c>,
/// <c>owner/repo/file.gguf</c> or llama.cpp's <c>owner/repo:QUANT</c>. Anything else (another host, datasets,
/// "..", spaces) is null. The revision is pinned to a commit when the model is added.
/// </summary>
public sealed partial record HfRef(string Owner, string Repo, string? Revision, string? File, string? Quant)
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$")]
    private static partial Regex Name();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,199}\\.gguf$", RegexOptions.IgnoreCase)]
    private static partial Regex GgufName();

    public static HfRef? Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length is 0 or > 500 || s.Contains(' ') || s.Contains("..")) return null;
        if (s.Contains("://"))
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host is not ("huggingface.co" or "hf.co" or "www.huggingface.co")) return null;
            s = u.AbsolutePath.Trim('/');
        }
        else if (s.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase) || s.StartsWith("huggingface.co/", StringComparison.OrdinalIgnoreCase))
            s = s[(s.IndexOf('/') + 1)..];
        string? quant = null;
        int colon = s.LastIndexOf(':');
        if (colon > 0)
        {
            quant = s[(colon + 1)..];
            s = s[..colon];
            if (!Name().IsMatch(quant)) return null;
        }
        var parts = s.Split('/');
        if (parts.Length < 2 || !Name().IsMatch(parts[0]) || !Name().IsMatch(parts[1]) || parts[0] is "datasets" or "spaces") return null;
        if (parts.Length == 2) return new(parts[0], parts[1], null, null, quant);
        if (parts.Length == 3 && GgufName().IsMatch(parts[2])) return new(parts[0], parts[1], null, parts[2], quant);
        if (parts.Length == 4 && parts[2] == "tree" && Name().IsMatch(parts[3])) return new(parts[0], parts[1], parts[3], null, quant);
        if (parts.Length == 5 && parts[2] is "blob" or "resolve" && Name().IsMatch(parts[3]) && GgufName().IsMatch(parts[4]))
            return new(parts[0], parts[1], parts[3], parts[4], quant);
        return null;
    }
}
