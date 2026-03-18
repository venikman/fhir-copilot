using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public interface IAgentRunner
{
    Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken);
    IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken);
}
