using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Options;
using GenerativeAI.Microsoft;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Services;

public sealed class GeminiAgentFrameworkRunner : AgentRunnerBase
{
    public GeminiAgentFrameworkRunner(IOptions<ProviderOptions> providerOptions, FhirToolbox toolbox, ILogger<GeminiAgentFrameworkRunner> logger)
        : base(providerOptions, toolbox, logger) { }

    protected override string BackendDescription => "Gemini client";

    protected override IReadOnlyList<string> ResolveModels() => Provider.GetModelChain();

    protected override IChatClient CreateChatClient(string model) =>
        new GenerativeAIChatClient(Provider.GeminiApiKey!, model);
}
