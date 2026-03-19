using System.Diagnostics;
using System.Diagnostics.Metrics;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public interface ICopilotService
{
    Task<CopilotResponse> RunAsync(CopilotRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<CopilotStreamEvent> StreamAsync(CopilotRequest request, CancellationToken cancellationToken);
}

public sealed class CopilotService : ICopilotService
{
    internal static readonly ActivitySource Telemetry = new("FhirCopilot.Agent");
    internal static readonly Meter Metrics = new("FhirCopilot.Agent");

    private static readonly Counter<long> RequestCounter =
        Metrics.CreateCounter<long>("copilot.requests", description: "Number of copilot requests");
    private static readonly Histogram<double> DurationHistogram =
        Metrics.CreateHistogram<double>("copilot.request.duration_ms", "ms", "Copilot request duration");
    private static readonly Counter<long> RoutingCounter =
        Metrics.CreateCounter<long>("copilot.routing.decisions", description: "Routing decisions made");

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
        var sw = Stopwatch.StartNew();
        var status = "success";

        var threadId = ResolveThreadId(request);
        activity?.SetTag("copilot.thread_id", threadId);

        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", _runner.GetType().Name);

        RoutingCounter.Add(1, new KeyValuePair<string, object?>("copilot.agent", agentType));

        _logger.LogInformation("Routed query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, _runner.GetType().Name);

        try
        {
            return await _runner.RunAsync(profile, request.Query, threadId, cancellationToken);
        }
        catch (Exception ex)
        {
            status = "error";
            _logger.LogError(ex, "Copilot request failed for agent {AgentType}", agentType);
            throw;
        }
        finally
        {
            sw.Stop();
            var tags = new TagList
            {
                { "copilot.agent", agentType },
                { "copilot.status", status }
            };
            RequestCounter.Add(1, tags);
            DurationHistogram.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("copilot.agent", agentType));
        }
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        CopilotRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var activity = Telemetry.StartActivity("copilot.stream");
        var sw = Stopwatch.StartNew();

        var threadId = ResolveThreadId(request);
        activity?.SetTag("copilot.thread_id", threadId);

        var agentType = await _router.RouteAsync(request.Query, cancellationToken);
        var profile = _profileStore.GetAgent(agentType);

        activity?.SetTag("copilot.agent", agentType);
        activity?.SetTag("copilot.runner", _runner.GetType().Name);

        RoutingCounter.Add(1, new KeyValuePair<string, object?>("copilot.agent", agentType));

        _logger.LogInformation("Streaming query to agent {AgentType}, thread {ThreadId}, runner {Runner}",
            agentType, threadId, _runner.GetType().Name);

        var errored = false;
        var enumerator = _runner.StreamAsync(profile, request.Query, threadId, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errored = true;
                    _logger.LogError(ex, "Copilot stream failed for agent {AgentType}", agentType);
                    throw;
                }

                if (!moved) break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            sw.Stop();
            var status = errored ? "error"
                : cancellationToken.IsCancellationRequested ? "cancelled"
                : "success";
            var tags = new TagList
            {
                { "copilot.agent", agentType },
                { "copilot.status", status }
            };
            RequestCounter.Add(1, tags);
            DurationHistogram.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("copilot.agent", agentType));
        }
    }

    private static string ResolveThreadId(CopilotRequest request)
        => string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("n") : request.ThreadId!;
}
