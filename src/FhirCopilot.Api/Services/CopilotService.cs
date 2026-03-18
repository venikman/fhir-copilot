using System.Diagnostics;
using FhirCopilot.Api.Contracts;

namespace FhirCopilot.Api.Services;

public interface ICopilotService
{
    Task<CopilotResponse> RunAsync(CopilotRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<CopilotStreamEvent> StreamAsync(CopilotRequest request, CancellationToken cancellationToken);
}

public sealed class CopilotService : ICopilotService
{
    internal static readonly ActivitySource Telemetry = new("FhirCopilot.Agent");

    private readonly IIntentRouter _router;
    private readonly IAgentProfileStore _profileStore;
    private readonly IAgentRunner _runner;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        IIntentRouter router,
        IAgentProfileStore profileStore,
        IAgentRunner runner,
        ILogger<CopilotService> logger)
    {
        _router = router;
        _profileStore = profileStore;
        _runner = runner;
        _logger = logger;
    }

    public async Task<CopilotResponse> RunAsync(CopilotRequest request, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("copilot.request");

        var threadId = ResolveThreadId(request);
        activity?.SetTag("copilot.thread_id", threadId);

        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", _runner.GetType().Name);

        _logger.LogInformation("Routed query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, _runner.GetType().Name);

        return await _runner.RunAsync(profile, request.Query, threadId, cancellationToken);
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        CopilotRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("copilot.stream");

        var threadId = ResolveThreadId(request);
        activity?.SetTag("copilot.thread_id", threadId);

        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", _runner.GetType().Name);

        _logger.LogInformation("Streaming query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, _runner.GetType().Name);

        await foreach (var evt in _runner.StreamAsync(profile, request.Query, threadId, cancellationToken))
        {
            yield return evt;
        }
    }

    private static string ResolveThreadId(CopilotRequest request)
        => string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("n") : request.ThreadId!;
}
