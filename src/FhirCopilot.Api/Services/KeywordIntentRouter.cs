using System.Text.RegularExpressions;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public interface IIntentRouter
{
    Task<string> RouteAsync(string query, CancellationToken cancellationToken);
}

public sealed class KeywordIntentRouter : IIntentRouter
{
    private readonly RouterProfile _router;
    private readonly ILogger<KeywordIntentRouter> _logger;

    public KeywordIntentRouter(IAgentProfileStore profileStore, ILogger<KeywordIntentRouter> logger)
    {
        _router = profileStore.GetRouter();
        _logger = logger;
    }

    public Task<string> RouteAsync(string query, CancellationToken cancellationToken)
    {
        var normalized = query.Trim().ToLowerInvariant();
        var scores = AgentTypes.All.ToDictionary(agent => agent, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var (agentType, hints) in _router.KeywordHints)
        {
            foreach (var hint in hints)
            {
                if (normalized.Contains(hint.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    scores[agentType] = scores.TryGetValue(agentType, out var current) ? current + 1 : 1;
                }
            }
        }

        if (normalized.Contains("how many", StringComparison.Ordinal))
        {
            scores[AgentTypes.Analytics] += 2;
        }

        if (normalized.Contains("without", StringComparison.Ordinal) ||
            normalized.Contains("who needs", StringComparison.Ordinal) ||
            normalized.Contains("care gap", StringComparison.Ordinal) ||
            normalized.Contains("at risk", StringComparison.Ordinal))
        {
            scores[AgentTypes.Cohort] += 2;
        }

        if (Regex.IsMatch(normalized, @"\b(patient|encounter|condition|observation|medicationrequest|procedure|allergyintolerance|group)[/:\-]", RegexOptions.IgnoreCase))
        {
            scores[AgentTypes.Lookup] += 2;
        }

        if (normalized.Contains("summary", StringComparison.Ordinal) || normalized.Contains("plain english", StringComparison.Ordinal))
        {
            scores[AgentTypes.Clinical] += 1;
        }

        var best = scores.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault();
        var selected = best.Value > 0 ? best.Key : _router.FallbackAgent;
        _logger.LogDebug("Router scores: {Scores}, selected: {Selected}", scores.Where(s => s.Value > 0).ToDictionary(s => s.Key, s => s.Value), selected);
        return Task.FromResult(selected);
    }
}
