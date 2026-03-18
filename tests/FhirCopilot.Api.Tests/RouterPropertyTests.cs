using FhirCopilot.Api.Models;
using FhirCopilot.Api.Services;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace FhirCopilot.Api.Tests;

public class RouterPropertyTests
{
    private readonly KeywordIntentRouter _router;

    public RouterPropertyTests()
    {
        _router = new KeywordIntentRouter(new InMemoryProfileStore(), NullLogger<KeywordIntentRouter>.Instance);
    }

    [Property]
    public bool Always_returns_a_known_agent_type(NonEmptyString query)
    {
        var result = _router.RouteAsync(query.Get, CancellationToken.None).Result;
        return AgentTypes.All.Contains(result, StringComparer.OrdinalIgnoreCase);
    }

    [Property]
    public bool Deterministic_for_same_input(NonEmptyString query)
    {
        var first = _router.RouteAsync(query.Get, CancellationToken.None).Result;
        var second = _router.RouteAsync(query.Get, CancellationToken.None).Result;
        return first == second;
    }

    [Property]
    public bool Case_insensitive(NonEmptyString query)
    {
        var lower = _router.RouteAsync(query.Get.ToLowerInvariant(), CancellationToken.None).Result;
        var upper = _router.RouteAsync(query.Get.ToUpperInvariant(), CancellationToken.None).Result;
        return lower == upper;
    }

    [Property]
    public bool Never_returns_null_or_empty(NonEmptyString query)
    {
        var result = _router.RouteAsync(query.Get, CancellationToken.None).Result;
        return !string.IsNullOrWhiteSpace(result);
    }

    [Property]
    public bool Whitespace_padding_does_not_change_result(NonEmptyString query)
    {
        var clean = _router.RouteAsync(query.Get, CancellationToken.None).Result;
        var padded = _router.RouteAsync($"  {query.Get}  ", CancellationToken.None).Result;
        return clean == padded;
    }

    /// <summary>
    /// Minimal IAgentProfileStore that provides the router config
    /// without needing the full DI container or filesystem.
    /// </summary>
    private sealed class InMemoryProfileStore : IAgentProfileStore
    {
        private readonly RouterProfile _router = new()
        {
            KeywordHints = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["lookup"] = ["show me", "read", "what is", "who manages", "what insurance", "coverage for", "patient/", "encounter/", "condition/"],
                ["search"] = ["find patients", "search", "encounters for", "patients by", "list encounters", "list patients"],
                ["analytics"] = ["how many", "count", "compare", "breakdown", "trend", "top", "volume", "ratio", "percentage"],
                ["clinical"] = ["clinical summary", "summarize", "tell me about", "what happened", "full summary", "plain english"],
                ["cohort"] = ["without", "who needs", "care gap", "gap", "at risk", "patients with", "patients without", "flag for review"],
                ["export"] = ["export", "bulk", "download all", "snapshot", "extract"]
            }
        };

        public AgentProfile GetAgent(string name) => throw new NotImplementedException();
        public RouterProfile GetRouter() => _router;
        public IReadOnlyDictionary<string, AgentProfile> GetAllAgents() => throw new NotImplementedException();
    }
}
