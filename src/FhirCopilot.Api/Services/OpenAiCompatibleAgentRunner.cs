using System.ClientModel;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FhirCopilot.Api.Services;

public sealed class OpenAiCompatibleAgentRunner : AgentRunnerBase
{
    public OpenAiCompatibleAgentRunner(IOptions<ProviderOptions> providerOptions, FhirToolbox toolbox, ILogger<OpenAiCompatibleAgentRunner> logger)
        : base(providerOptions, toolbox, logger) { }

    protected override string BackendDescription => "Local OpenAI-compatible client";

    protected override IReadOnlyList<string> ResolveModels() =>
        [Provider.LocalModel!];

    protected override IChatClient CreateChatClient(string model) =>
        new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(Provider.LocalEndpoint!) })
            .GetChatClient(model)
            .AsIChatClient();
}
