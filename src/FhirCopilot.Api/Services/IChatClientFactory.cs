using Microsoft.Extensions.AI;

namespace FhirCopilot.Api.Services;

public interface IChatClientFactory
{
    IChatClient Create(string model);
    IReadOnlyList<string> ModelChain { get; }
    string Description { get; }
}
