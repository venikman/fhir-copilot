using System.Diagnostics;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Options;
using Microsoft.Extensions.Options;

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
    private readonly StubAgentRunner _stubRunner;
    private readonly GeminiAgentFrameworkRunner _geminiRunner;
    private readonly RuntimeOptions _runtime;
    private readonly ILogger<CopilotService> _logger;

    public CopilotService(
        IIntentRouter router,
        IAgentProfileStore profileStore,
        StubAgentRunner stubRunner,
        GeminiAgentFrameworkRunner geminiRunner,
        IOptions<RuntimeOptions> runtimeOptions,
        ILogger<CopilotService> logger)
    {
        _router = router;
        _profileStore = profileStore;
        _stubRunner = stubRunner;
        _geminiRunner = geminiRunner;
        _runtime = runtimeOptions.Value;
        _logger = logger;
    }

    public async Task<CopilotResponse> RunAsync(CopilotRequest request, CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("copilot.request");

        var threadId = ResolveThreadId(request);
        activity?.SetTag("copilot.thread_id", threadId);

        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);
        var runner = _geminiRunner.IsConfigured ? "Gemini" : "Stub";

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", runner);

        _logger.LogInformation("Routed query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, runner);

        if (_geminiRunner.IsConfigured)
        {
            return await _geminiRunner.RunAsync(profile, request.Query, threadId, cancellationToken);
        }

        if (_runtime.UseStubWhenProviderMissing)
        {
            return await _stubRunner.RunAsync(profile, request.Query, threadId, cancellationToken);
        }

        throw new InvalidOperationException("No provider is configured and stub mode is disabled.");
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
        var runner = _geminiRunner.IsConfigured ? "Gemini" : "Stub";

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", runner);

        _logger.LogInformation("Streaming query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, runner);

        if (_geminiRunner.IsConfigured)
        {
            await foreach (var evt in _geminiRunner.StreamAsync(profile, request.Query, threadId, cancellationToken))
            {
                yield return evt;
            }

            yield break;
        }

        if (_runtime.UseStubWhenProviderMissing)
        {
            await foreach (var evt in _stubRunner.StreamAsync(profile, request.Query, threadId, cancellationToken))
            {
                yield return evt;
            }

            yield break;
        }

        yield return CopilotStreamEvent.Error("No provider is configured and stub mode is disabled.");
    }

    private static string ResolveThreadId(CopilotRequest request)
        => string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("n") : request.ThreadId!;
}
