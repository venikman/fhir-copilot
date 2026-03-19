using System.ClientModel;
using FhirCopilot.Api.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FhirCopilot.Api.Services;

public sealed class LocalChatClientFactory(IOptions<ProviderOptions> options) : IChatClientFactory
{
    public IChatClient Create(string model) =>
        new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(options.Value.LocalEndpoint!) })
            .GetChatClient(model)
            .AsIChatClient();

    public IReadOnlyList<string> ModelChain => [options.Value.LocalModel!];
    public string Description => "Local OpenAI-compatible client";
}
