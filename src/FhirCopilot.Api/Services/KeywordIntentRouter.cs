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
        var scores = AgentTypes.All.ToDictionary(agent => agent, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var (agentType, hints) in _normalizedHints)
        {
            if (!scores.ContainsKey(agentType))
                continue;

            foreach (var hint in hints)
            {
                if (normalized.Contains(hint, StringComparison.Ordinal))
                {
                    scores[agentType] += 1;
                }
            }
        }

        var best = scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault();
        var selected = best.Value > 0 ? best.Key : _router.FallbackAgent;
        _logger.LogDebug("Router scores: {Scores}, selected: {Selected}", scores.Where(s => s.Value > 0).ToDictionary(s => s.Key, s => s.Value), selected);
        return Task.FromResult(selected);
    }
}
