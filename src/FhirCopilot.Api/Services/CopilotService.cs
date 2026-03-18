using System.Diagnostics;
using System.Diagnostics.Metrics;
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
        catch
        {
            status = "error";
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

        var completed = false;
        try
        {
            await foreach (var evt in _runner.StreamAsync(profile, request.Query, threadId, cancellationToken))
            {
                yield return evt;
            }
            completed = true;
        }
        finally
        {
            sw.Stop();
            var tags = new TagList
            {
                { "copilot.agent", agentType },
                { "copilot.status", completed ? "success" : "error" }
            };
            RequestCounter.Add(1, tags);
            DurationHistogram.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("copilot.agent", agentType));
        }
    }

    private static string ResolveThreadId(CopilotRequest request)
        => string.IsNullOrWhiteSpace(request.ThreadId) ? Guid.NewGuid().ToString("n") : request.ThreadId!;
}
