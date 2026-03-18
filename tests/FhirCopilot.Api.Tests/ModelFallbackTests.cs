using FhirCopilot.Api.Options;

namespace FhirCopilot.Api.Tests;

public class ModelFallbackTests
{
    [Fact]
    public void GetModelChain_returns_GeminiModels_when_configured()
    {
        var options = new ProviderOptions
        {
            GeminiModel = "gemini-3-flash-preview",
            GeminiModels = ["gemini-3-flash-preview", "gemini-3.1-flash-lite-preview", "gemini-3.1-pro-preview"]
        };

        var chain = options.GetModelChain();

        Assert.Equal(3, chain.Count);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
        Assert.Equal("gemini-3.1-pro-preview", chain[2]);
    }

    [Fact]
    public void GetModelChain_falls_back_to_single_GeminiModel()
    {
        var options = new ProviderOptions
        {
            GeminiModel = "gemini-3-flash-preview",
            GeminiModels = null
        };

        var chain = options.GetModelChain();

        Assert.Single(chain);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
    }

    [Fact]
    public void GetModelChain_uses_default_when_nothing_set()
    {
        var options = new ProviderOptions
        {
            GeminiModel = null,
            GeminiModels = null
        };

        var chain = options.GetModelChain();

        Assert.Single(chain);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
    }
}
