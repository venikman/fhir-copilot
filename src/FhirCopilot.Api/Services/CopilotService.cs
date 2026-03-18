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
    private readonly IIntentRouter _router;
    private readonly IAgentProfileStore _profileStore;
    private readonly StubAgentRunner _stubRunner;
    private readonly GeminiAgentFrameworkRunner _geminiRunner;
    private readonly RuntimeOptions _runtime;

    public CopilotService(
        IIntentRouter router,
        IAgentProfileStore profileStore,
        StubAgentRunner stubRunner,
        GeminiAgentFrameworkRunner geminiRunner,
        IOptions<RuntimeOptions> runtimeOptions)
    {
        _router = router;
        _profileStore = profileStore;
        _stubRunner = stubRunner;
        _geminiRunner = geminiRunner;
        _runtime = runtimeOptions.Value;
    }

    public async Task<CopilotResponse> RunAsync(CopilotRequest request, CancellationToken cancellationToken)
    {
        var threadId = ResolveThreadId(request);
        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

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
        var threadId = ResolveThreadId(request);
        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

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
