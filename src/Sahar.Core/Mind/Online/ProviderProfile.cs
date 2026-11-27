namespace Sahar.Core.Mind.Online;

/// <summary>Where a provider's daily request count comes from (spec 0.17 §7): OpenRouter's <c>GET key</c>, the
/// reply's rate headers (Groq), or only our own count.</summary>
public enum QuotaKind { None, OpenRouterKey, Headers, Count }

/// <summary>
/// One OpenAI-compatible chat-completions endpoint (spec 0.17 §3): its base URL (ending in "/"), the model id, whether
/// llama-server's own fields go in the body (<c>top_k</c>, <c>chat_template_kwargs</c>, the decision schema), whether
/// it goes through the system proxy (online: yes, his VPN; local: never), extra headers and body members, and how its
/// quota is known. Not secret: the key is passed beside it, never kept in it.
/// </summary>
public sealed record ProviderProfile(
    string Id, string Name, Uri BaseUrl, string Model, bool LlamaFields, bool UseProxy, QuotaKind Quota, int? DailyLimit,
    IReadOnlyList<KeyValuePair<string, string>> Headers, string? ExtraBody = null)
{
    public bool Online => Id != ProviderProfiles.LocalId;
}

/// <summary>The profiles Sahar knows. Facts checked 2026-11-27 (Groq rate-limits and reasoning docs; OpenRouter limits
/// and its model list).</summary>
public static class ProviderProfiles
{
    public const string LocalId = "local", GroqId = "groq", OpenRouterId = "openrouter", CustomId = "custom";
    public static readonly IReadOnlyList<string> OnlineIds = [GroqId, OpenRouterId, CustomId];

    public const string GroqDefault = "openai/gpt-oss-120b";
    public const string OpenRouterDefault = "google/gemma-4-31b-it:free";

    /// <summary>Suggestions on the page (any id the provider has works too).</summary>
    public static readonly IReadOnlyList<string> GroqModels = [GroqDefault, "llama-3.3-70b-versatile", "qwen/qwen3.8-27b", "openai/gpt-oss-20b"];
    public static readonly IReadOnlyList<string> OpenRouterModels =
        [OpenRouterDefault, "google/gemma-4-26b-a4b-it:free", "nvidia/nemotron-3-ultra-550b-a55b:free", "openrouter/free"];

    /// <summary>CI only: <c>SAHAR_MIND_ONLINE_BASE</c> (a loopback URL) stands in for the real hosts; anything else is ignored.</summary>
    public static string? TestBase { get; set; } = Environment.GetEnvironmentVariable("SAHAR_MIND_ONLINE_BASE") is { Length: > 0 } b && IsLoopback(b) ? b.TrimEnd('/') + "/" : null;

    public static ProviderProfile Local(int port, string key) =>
        new(LocalId, "محلي", new Uri($"http://127.0.0.1:{port}/v1/"), "sahar", LlamaFields: true, UseProxy: false, QuotaKind.None, null, []);

    public static ProviderProfile Groq(string? model = null)
    {
        var m = Model(model, GroqDefault);
        // gpt-oss on Groq thinks by default: low effort, and the reasoning kept out of the reply (Groq reasoning docs).
        string? extra = m.StartsWith("openai/gpt-oss", StringComparison.Ordinal) ? "\"include_reasoning\":false,\"reasoning_effort\":\"low\"" : null;
        return new(GroqId, "Groq", Base("https://api.groq.com/openai/v1/", "groq"), m, false, true, QuotaKind.Headers, 1000, [], extra);
    }

    public static ProviderProfile OpenRouter(string? model = null) =>
        // OpenRouter's attribution headers: a generic app name, nothing about him.
        new(OpenRouterId, "OpenRouter", Base("https://openrouter.ai/api/v1/", "openrouter"), Model(model, OpenRouterDefault), false, true, QuotaKind.OpenRouterKey, 50,
            [new("HTTP-Referer", "https://github.com/A7ma-B25/sahar"), new("X-Title", "Sahar")]);

    /// <summary>Any OpenAI-compatible endpoint he types (Claude/OpenAI/others later); null when the URL or model is not allowed.</summary>
    public static ProviderProfile? Custom(string? url, string? model)
    {
        if (url is null || !IsAllowedUrl(url) || string.IsNullOrWhiteSpace(model) || !IsModelId(model.Trim())) return null;
        var u = url.Trim().TrimEnd('/') + "/";
        return new(CustomId, "مخصّص", new Uri(u), model.Trim(), false, true, QuotaKind.Count, null, []);
    }

    /// <summary>https anywhere, or http/https on a loopback host (his own server; CI's fake provider). No user info, no query.</summary>
    public static bool IsAllowedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Trim().Length > 200) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return false;
        if (u.UserInfo.Length > 0 || u.Query.Length > 0 || u.Fragment.Length > 0) return false;
        return u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback);
    }

    /// <summary>A model id as providers write them: letters, digits and <c>. _ : / @ -</c>, 1–120 characters.</summary>
    public static bool IsModelId(string? id) =>
        id is { Length: > 0 and <= 120 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '@' or '-');

    private static string Model(string? model, string fallback) => model is { } m && IsModelId(m.Trim()) ? m.Trim() : fallback;

    private static Uri Base(string real, string name) => new(TestBase is { } t ? t + name + "/" : real);

    private static bool IsLoopback(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.IsLoopback && u.Scheme is "http" or "https";
}
