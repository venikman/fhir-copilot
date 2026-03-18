using System.Text.Json;
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
        _router = new KeywordIntentRouter(new ConfigFileProfileStore(), NullLogger<KeywordIntentRouter>.Instance);
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
    /// Loads router config from the actual router.json config file
    /// so tests stay in sync with production routing hints.
    /// </summary>
    private sealed class ConfigFileProfileStore : IAgentProfileStore
    {
        private readonly RouterProfile _router;

        public ConfigFileProfileStore()
        {
            var configDir = Path.Combine(AppContext.BaseDirectory, "config", "agents");
            var routerPath = Path.Combine(configDir, "router.json");
            var json = File.ReadAllText(routerPath);
            _router = JsonSerializer.Deserialize<RouterProfile>(json, JsonDefaults.Serializer)
                      ?? throw new InvalidDataException("Failed to deserialize router.json");
        }

        public AgentProfile GetAgent(string name) => throw new NotImplementedException();
        public RouterProfile GetRouter() => _router;
        public IReadOnlyDictionary<string, AgentProfile> GetAllAgents() => throw new NotImplementedException();
    }
}
