using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public interface IIntentRouter
{
    Task<string> RouteAsync(string query, CancellationToken cancellationToken);
}

public sealed class KeywordIntentRouter : IIntentRouter
{
    private readonly RouterProfile _router;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _normalizedHints;
    private readonly ILogger<KeywordIntentRouter> _logger;

    public KeywordIntentRouter(IAgentProfileStore profileStore, ILogger<KeywordIntentRouter> logger)
    {
        _router = profileStore.GetRouter();
        _normalizedHints = _router.KeywordHints
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<string>)kvp.Value.Select(h => h.ToLowerInvariant()).ToList(),
                StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public Task<string> RouteAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = query.Trim().ToLowerInvariant();

        var selected = _normalizedHints
            .Where(kvp => AgentTypes.All.Contains(kvp.Key))
            .Select(kvp => (Agent: kvp.Key, Score: kvp.Value.Count(hint => normalized.Contains(hint, StringComparison.Ordinal))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Agent, StringComparer.Ordinal)
            .Select(x => x.Agent)
            .FirstOrDefault() ?? _router.FallbackAgent;

        _logger.LogDebug("Router selected: {Selected} for query", selected);
        return Task.FromResult(selected);
    }
}
