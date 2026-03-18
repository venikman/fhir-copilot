using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Options;
using GenerativeAI.Microsoft;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Services;

public sealed class GeminiAgentFrameworkRunner : IAgentRunner
{
    private const int MaxSessions = 200;

    private static readonly Counter<long> SessionsCreated =
        CopilotService.Metrics.CreateCounter<long>("copilot.sessions.created", description: "Sessions created");
    private static readonly Counter<long> SessionsEvicted =
        CopilotService.Metrics.CreateCounter<long>("copilot.sessions.evicted", description: "Sessions evicted");
    private static readonly UpDownCounter<long> SessionsActive =
        CopilotService.Metrics.CreateUpDownCounter<long>("copilot.sessions.active", description: "Currently active sessions");

    private readonly ProviderOptions _provider;
    private readonly FhirToolbox _toolbox;
    private readonly ILogger<GeminiAgentFrameworkRunner> _logger;
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private sealed record SessionEntry(AgentSession Session, DateTime LastUsed);

    public GeminiAgentFrameworkRunner(IOptions<ProviderOptions> providerOptions, FhirToolbox toolbox, ILogger<GeminiAgentFrameworkRunner> logger)
    {
        _provider = providerOptions.Value;
        _toolbox = toolbox;
        _logger = logger;
    }

    public bool IsConfigured => _provider.IsGeminiMode;

    public async Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RunAsync started for agent {AgentName}, thread {ThreadId}", profile.Name, threadId);

        var agent = GetOrCreateAgent(profile);
        var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);

        var answerBuilder = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                answerBuilder.Append(update.Text);
            }
        }

        var answer = answerBuilder.ToString().Trim();
        _logger.LogInformation("RunAsync completed for agent {AgentName}, thread {ThreadId}, answer length {AnswerLength}", profile.Name, threadId, answer.Length);
        return BuildResponse(answer, profile, threadId);
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        AgentProfile profile,
        string query,
        string threadId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var agent = GetOrCreateAgent(profile);
        var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);

        _logger.LogInformation("StreamAsync started for agent {AgentName}, thread {ThreadId}", profile.Name, threadId);

        yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: false);

        var answerBuilder = new StringBuilder();

        await foreach (var update in agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                answerBuilder.Append(update.Text);
                yield return CopilotStreamEvent.Delta(update.Text);
            }
        }

        var answer = answerBuilder.ToString().Trim();
        _logger.LogInformation("StreamAsync completed for agent {AgentName}, thread {ThreadId}, answer length {AnswerLength}", profile.Name, threadId, answer.Length);
        yield return CopilotStreamEvent.Done(BuildResponse(answer, profile, threadId));
    }

    private static CopilotResponse BuildResponse(string answer, AgentProfile profile, string threadId)
    {
        var citations = CitationExtractor.Extract(answer);
        return new CopilotResponse(
            answer,
            citations,
            [
                $"Routed to {profile.Name}.",
                $"Loaded runtime profile '{profile.Name}' from file-backed configuration.",
                "Executed Agent Framework streaming run over the Gemini client."
            ],
            Array.Empty<string>(),
            profile.Name,
            Confidence: "unverified",
            threadId,
            IsStub: false);
    }

    private AIAgent GetOrCreateAgent(AgentProfile profile)
    {
        return _agents.GetOrAdd(profile.Name, _ =>
        {
            var model = _provider.GeminiModel ?? "gemini-3.1-flash";
            var instructions = PromptComposer.Compose(profile);
            var tools = ToolRegistry.BuildTools(_toolbox, profile.AllowedTools);

            var chatClient = new GenerativeAIChatClient(_provider.GeminiApiKey!, model)
                .AsBuilder()
                .UseOpenTelemetry(sourceName: "FhirCopilot.GenAI")
                .Build();
            return chatClient.AsAIAgent(
                name: profile.DisplayName,
                instructions: instructions,
                tools: tools.Cast<AITool>().ToList());
        });
    }

    private async Task<AgentSession> GetOrCreateSessionAsync(string threadId, string agentName, AIAgent agent)
    {
        var key = $"{threadId}::{agentName}";

        // All session access goes through the lock to prevent races between
        // the optimistic read, LRU eviction, and LastUsed updates.
        await _sessionLock.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                _sessions[key] = existing with { LastUsed = DateTime.UtcNow };
                return existing.Session;
            }

            // Evict least-recently-used sessions to reclaim capacity
            if (_sessions.Count >= MaxSessions)
            {
                var evictCount = Math.Min(_sessions.Count / 10, _sessions.Count - MaxSessions + 1);
                evictCount = Math.Max(evictCount, 1);
                var keysToRemove = _sessions
                    .OrderBy(kvp => kvp.Value.LastUsed)
                    .Take(evictCount)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var staleKey in keysToRemove)
                    _sessions.TryRemove(staleKey, out _);

                SessionsEvicted.Add(keysToRemove.Count);
                SessionsActive.Add(-keysToRemove.Count);

                _logger.LogInformation("Evicted {EvictedCount} session(s), {RemainingCount} remaining", keysToRemove.Count, _sessions.Count);
            }

            var created = await agent.CreateSessionAsync();
            _sessions[key] = new SessionEntry(created, DateTime.UtcNow);

            SessionsCreated.Add(1);
            SessionsActive.Add(1);

            _logger.LogDebug("Created new session for thread {ThreadId}, agent {AgentName}", threadId, agentName);
            return created;
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
