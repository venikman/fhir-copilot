using FhirCopilot.Api.Options;
using GenerativeAI.Microsoft;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Services;

public sealed class GeminiChatClientFactory(IOptions<ProviderOptions> options) : IChatClientFactory
{
    public IChatClient Create(string model) =>
        new GenerativeAIChatClient(options.Value.GeminiApiKey!, model);

    public IReadOnlyList<string> ModelChain => options.Value.GetModelChain();
    public string Description => "Gemini client";
}
