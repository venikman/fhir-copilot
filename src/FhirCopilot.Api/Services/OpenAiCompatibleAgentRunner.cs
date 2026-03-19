using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace FhirCopilot.Api.Services;

public sealed class OpenAiCompatibleAgentRunner : IAgentRunner
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
    private readonly ILogger<OpenAiCompatibleAgentRunner> _logger;
    private readonly ConcurrentDictionary<string, AIAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private sealed record SessionEntry(AgentSession Session, DateTime LastUsed);

    public OpenAiCompatibleAgentRunner(IOptions<ProviderOptions> providerOptions, FhirToolbox toolbox, ILogger<OpenAiCompatibleAgentRunner> logger)
    {
        _provider = providerOptions.Value;
        _toolbox = toolbox;
        _logger = logger;
    }

    public async Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RunAsync started for agent {AgentName}, thread {ThreadId}", profile.Name, threadId);

        var model = _provider.LocalModel ?? "zai-org/glm-4.7-flash";
        var agent = GetOrCreateAgent(profile, model);
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
        Activity.Current?.SetTag("copilot.model", model);
        _logger.LogInformation("RunAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
            profile.Name, threadId, model, answer.Length);
        return BuildResponse(answer, profile, threadId);
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        AgentProfile profile,
        string query,
        string threadId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var model = _provider.LocalModel ?? "zai-org/glm-4.7-flash";
        var answerBuilder = new StringBuilder();

        _logger.LogInformation("StreamAsync started for agent {AgentName}, thread {ThreadId}, model {Model}",
            profile.Name, threadId, model);

        yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: false);

        var agent = GetOrCreateAgent(profile, model);
        var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);

        var enumerator = agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                var moved = await enumerator.MoveNextAsync();
                if (!moved) break;

                if (!string.IsNullOrWhiteSpace(enumerator.Current.Text))
                {
                    answerBuilder.Append(enumerator.Current.Text);
                    yield return CopilotStreamEvent.Delta(enumerator.Current.Text);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }

        var answer = answerBuilder.ToString().Trim();
        Activity.Current?.SetTag("copilot.model", model);
        _logger.LogInformation("StreamAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
            profile.Name, threadId, model, answer.Length);
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
                "Executed Agent Framework streaming run over the Local OpenAI-compatible client."
            ],
            Array.Empty<string>(),
            profile.Name,
            Confidence: "unverified",
            threadId,
            IsStub: false);
    }

    private AIAgent GetOrCreateAgent(AgentProfile profile, string model)
    {
        var cacheKey = $"{profile.Name}::{model}";
        return _agents.GetOrAdd(cacheKey, _ =>
        {
            var instructions = PromptComposer.Compose(profile);
            var tools = ToolRegistry.BuildTools(_toolbox, profile.AllowedTools);

            var openAiClient = new OpenAIClient(
                new ApiKeyCredential("lm-studio"),
                new OpenAIClientOptions { Endpoint = new Uri(_provider.LocalEndpoint!) });
            var chatClient = openAiClient.GetChatClient(model)
                .AsIChatClient()
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

        await _sessionLock.WaitAsync();
        try
        {
            if (_sessions.TryGetValue(key, out var existing))
            {
                _sessions[key] = existing with { LastUsed = DateTime.UtcNow };
                return existing.Session;
            }

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
