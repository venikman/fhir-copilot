using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Options;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Services;

public abstract class AgentRunnerBase : IAgentRunner
{
    private const int MaxSessions = 200;

    private static readonly Counter<long> SessionsCreated =
        CopilotService.Metrics.CreateCounter<long>("copilot.sessions.created", description: "Sessions created");
    private static readonly Counter<long> SessionsEvicted =
        CopilotService.Metrics.CreateCounter<long>("copilot.sessions.evicted", description: "Sessions evicted");
    private static readonly UpDownCounter<long> SessionsActive =
        CopilotService.Metrics.CreateUpDownCounter<long>("copilot.sessions.active", description: "Currently active sessions");

    private readonly ConcurrentDictionary<string, AIAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private sealed record SessionEntry(AgentSession Session, DateTime LastUsed);

    protected ProviderOptions Provider { get; }
    protected FhirToolbox Toolbox { get; }
    protected ILogger Logger { get; }

    protected AgentRunnerBase(IOptions<ProviderOptions> providerOptions, FhirToolbox toolbox, ILogger logger)
    {
        Provider = providerOptions.Value;
        Toolbox = toolbox;
        Logger = logger;
    }

    /// <summary>Creates an IChatClient for the given model name.</summary>
    protected abstract IChatClient CreateChatClient(string model);

    /// <summary>Returns the ordered list of models to attempt (fallback chain).</summary>
    protected abstract IReadOnlyList<string> ResolveModels();

    /// <summary>Human-readable backend name used in response reasoning lines.</summary>
    protected abstract string BackendDescription { get; }

    public async Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken)
    {
        Logger.LogInformation("RunAsync started for agent {AgentName}, thread {ThreadId}", profile.Name, threadId);

        var models = ResolveModels();
        HttpRequestException? lastException = null;

        foreach (var model in models)
        {
            try
            {
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
                Logger.LogInformation("RunAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
                    profile.Name, threadId, model, answer.Length);
                return BuildResponse(answer, profile, threadId);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lastException = ex;
                Logger.LogWarning("Model {Model} rate-limited (429), falling back to next model", model);
            }
        }

        throw lastException!;
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        AgentProfile profile,
        string query,
        string threadId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var models = ResolveModels();
        HttpRequestException? lastException = null;

        yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: false);

        foreach (var model in models)
        {
            var succeeded = false;
            var answerBuilder = new StringBuilder();

            Logger.LogInformation("StreamAsync attempting model {Model} for agent {AgentName}, thread {ThreadId}",
                model, profile.Name, threadId);

            var agent = GetOrCreateAgent(profile, model);
            var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);

            var enumerator = agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken)
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
                    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        lastException = ex;
                        Logger.LogWarning("Model {Model} rate-limited (429) during stream, falling back to next model", model);
                        break;
                    }

                    if (!moved) { succeeded = true; break; }

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

            if (succeeded)
            {
                var answer = answerBuilder.ToString().Trim();
                Activity.Current?.SetTag("copilot.model", model);
                Logger.LogInformation("StreamAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
                    profile.Name, threadId, model, answer.Length);
                yield return CopilotStreamEvent.Done(BuildResponse(answer, profile, threadId));
                yield break;
            }
        }

        throw lastException!;
    }

    private CopilotResponse BuildResponse(string answer, AgentProfile profile, string threadId)
    {
        var citations = CitationExtractor.Extract(answer);
        return new CopilotResponse(
            answer,
            citations,
            [
                $"Routed to {profile.Name}.",
                $"Loaded runtime profile '{profile.Name}' from file-backed configuration.",
                $"Executed Agent Framework streaming run over the {BackendDescription}."
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
            var tools = ToolRegistry.BuildTools(Toolbox, profile.AllowedTools);

            var chatClient = CreateChatClient(model)
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

                Logger.LogInformation("Evicted {EvictedCount} session(s), {RemainingCount} remaining", keysToRemove.Count, _sessions.Count);
            }

            var created = await agent.CreateSessionAsync();
            _sessions[key] = new SessionEntry(created, DateTime.UtcNow);

            SessionsCreated.Add(1);
            SessionsActive.Add(1);

            Logger.LogDebug("Created new session for thread {ThreadId}, agent {AgentName}", threadId, agentName);
            return created;
        }
        finally
        {
            _sessionLock.Release();
        }
    }
}
