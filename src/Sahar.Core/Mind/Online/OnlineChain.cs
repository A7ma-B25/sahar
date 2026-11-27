using Sahar.Core.Settings;

namespace Sahar.Core.Mind.Online;

/// <summary>
/// His chain from settings (spec 0.17 §4, read each turn: a change applies to the next line, no restart): each provider
/// in his order with its chosen model, and whether a key is sealed for it. «محلي فقط» or no consent = an empty chain.
/// The custom row is there only with a valid URL and model. Pure.
/// </summary>
public static class OnlineChain
{
    public static IReadOnlyList<(ProviderProfile P, bool HasKey)> From(SaharSettings s, IReadOnlyCollection<string> keyed)
    {
        if (s.MindLocalOnly || !s.MindOnlineConsent) return [];
        var list = new List<(ProviderProfile, bool)>();
        foreach (var id in s.MindOnlineChain.Distinct())
        {
            var p = For(id, s);
            if (p is not null) list.Add((p, keyed.Contains(id)));
        }
        return list;
    }

    public static ProviderProfile? For(string id, SaharSettings s) => id switch
    {
        ProviderProfiles.GroqId => ProviderProfiles.Groq(s.MindGroqModel),
        ProviderProfiles.OpenRouterId => ProviderProfiles.OpenRouter(s.MindOpenRouterModel),
        ProviderProfiles.CustomId => ProviderProfiles.Custom(s.MindCustomUrl, s.MindCustomModel),
        _ => null,
    };

    /// <summary>The chat window's chip shows only when online can really answer: consent, «محلي فقط» off, a keyed provider.</summary>
    public static bool Usable(SaharSettings s) => !s.MindLocalOnly && s.MindOnlineConsent && From(s, s.MindOnlineKeys).Any(c => c.HasKey);
}
