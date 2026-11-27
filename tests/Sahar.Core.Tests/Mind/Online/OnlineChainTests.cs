using Sahar.Core.Mind.Online;
using Sahar.Core.Settings;

namespace Sahar.Core.Tests.Mind.Online;

public sealed class OnlineChainTests
{
    [Fact]
    public void From_OrderModelsKeys()
    {
        var s = new SaharSettings { MindOnlineConsent = true, MindOnlineChain = ["openrouter", "groq", "custom"], MindGroqModel = "llama-3.3-70b-versatile" };
        var c = OnlineChain.From(s, ["groq"]);
        Assert.Equal(["openrouter", "groq"], c.Select(x => x.P.Id));   // custom has no URL: not in the chain
        Assert.Equal("llama-3.3-70b-versatile", c[1].P.Model);
        Assert.Equal([false, true], c.Select(x => x.HasKey));
    }

    [Fact]
    public void From_LocalOnlyOrNoConsent_Empty()
    {
        Assert.Empty(OnlineChain.From(new SaharSettings { MindOnlineConsent = true, MindLocalOnly = true }, ["groq"]));
        Assert.Empty(OnlineChain.From(new SaharSettings(), ["groq"]));
    }

    [Fact]
    public void Usable_NeedsAKeyedProvider()
    {
        Assert.False(OnlineChain.Usable(new SaharSettings { MindOnlineConsent = true }));
        Assert.True(OnlineChain.Usable(new SaharSettings { MindOnlineConsent = true, MindOnlineKeys = ["openrouter"] }));
        Assert.False(OnlineChain.Usable(new SaharSettings { MindOnlineConsent = true, MindOnlineKeys = ["openrouter"], MindLocalOnly = true }));
    }

    [Fact]
    public void Custom_InChainWithUrlAndModel()
    {
        var s = new SaharSettings { MindOnlineConsent = true, MindCustomUrl = "http://127.0.0.1:9/v1", MindCustomModel = "fake" };
        Assert.Contains(OnlineChain.From(s, ["custom"]), x => x.P.Id == "custom" && x.HasKey);
    }
}
