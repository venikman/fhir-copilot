using FhirCopilot.Api.Options;

namespace FhirCopilot.Api.Tests;

public class ModelFallbackTests
{
    [Fact]
    public void GetModelChain_returns_GeminiModels_when_configured()
    {
        var options = new ProviderOptions
        {
            GeminiModels = ["gemini-3-flash-preview", "gemini-3.1-flash-lite-preview", "gemini-3.1-pro-preview"]
        };

        var chain = options.GetModelChain();

        Assert.Equal(3, chain.Count);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
        Assert.Equal("gemini-3.1-pro-preview", chain[2]);
    }

    [Fact]
    public void GetModelChain_falls_back_to_full_default_chain_when_GeminiModels_null()
    {
        var options = new ProviderOptions
        {
            GeminiModels = null
        };

        var chain = options.GetModelChain();

        Assert.Equal(6, chain.Count);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
        Assert.Equal("gemini-2.0-flash", chain[5]);
    }

    [Fact]
    public void GetModelChain_falls_back_to_full_default_chain_when_GeminiModels_empty()
    {
        var options = new ProviderOptions
        {
            GeminiModels = []
        };

        var chain = options.GetModelChain();

        Assert.Equal(6, chain.Count);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
    }
}
