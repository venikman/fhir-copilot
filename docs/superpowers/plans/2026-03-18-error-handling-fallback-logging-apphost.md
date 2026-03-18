# Error Handling, Model Fallback, Logging Enrichment & AppHost Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add structured error responses, Gemini model fallback on 429, trace-enriched console logging, and Aspire AppHost FHIR resource.

**Architecture:** Error contracts flow from contracts → endpoint handler → CopilotService logging. Model fallback wraps the existing runner with a retry-on-429 loop across a configurable model chain. Console formatter enriches local dev logs with trace/span IDs. AppHost declares the FHIR backend as an external resource.

**Tech Stack:** .NET 9, ASP.NET Core Minimal APIs, OpenTelemetry, Aspire 13.1.2, xUnit

**Spec:** `docs/superpowers/specs/2026-03-18-error-handling-logging-apphost-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `src/FhirCopilot.Api/Contracts/CopilotContracts.cs` | Add `CopilotError` + `CopilotErrorResponse` records |
| Modify | `src/FhirCopilot.Api/Program.cs` | try/catch on `/api/copilot` endpoint |
| Modify | `src/FhirCopilot.Api/Services/CopilotService.cs` | Add `LogError` calls |
| Modify | `src/FhirCopilot.Api/Options/RuntimeOptions.cs` | Add `GeminiModels` list to `ProviderOptions` |
| Modify | `src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs` | Fallback loop + cache key change |
| Modify | `src/FhirCopilot.Api/appsettings.json` | Add `GeminiModels` array |
| Modify | `.env` | Update model config |
| Modify | `.env.example` | Add `GeminiModels` entries |
| Modify | `CLAUDE.md` | Update model policy for all 3 models |
| Create | `src/FhirCopilot.ServiceDefaults/TraceEnrichedConsoleFormatter.cs` | Custom console formatter with trace/span IDs |
| Modify | `src/FhirCopilot.ServiceDefaults/Extensions.cs` | Register custom formatter |
| Modify | `src/FhirCopilot.AppHost/Program.cs` | Add FHIR backend external resource |
| Create | `tests/FhirCopilot.Api.Tests/ErrorHandlingTests.cs` | Error response integration tests |
| Create | `tests/FhirCopilot.Api.Tests/ModelFallbackTests.cs` | 429 fallback unit tests |
| Create | `tests/FhirCopilot.Api.Tests/TraceConsoleFormatterTests.cs` | Formatter output tests |

---

## Task 1: Error Contract Records

**Files:**
- Modify: `src/FhirCopilot.Api/Contracts/CopilotContracts.cs`

- [ ] **Step 1: Add error records to CopilotContracts.cs**

After the existing `CopilotStreamEvent` record (after line 37), add:

```csharp
public sealed record CopilotError(string Type, string Message);
public sealed record CopilotErrorResponse(CopilotError Error);
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/FhirCopilot.Api/FhirCopilot.Api.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FhirCopilot.Api/Contracts/CopilotContracts.cs
git commit -m "feat: add CopilotError and CopilotErrorResponse contracts"
```

---

## Task 2: Structured Error Handling on Non-Streaming Endpoint

**Files:**
- Test: `tests/FhirCopilot.Api.Tests/ErrorHandlingTests.cs`
- Modify: `src/FhirCopilot.Api/Program.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FhirCopilot.Api.Tests/ErrorHandlingTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FhirCopilot.Api.Tests;

public class ErrorHandlingTests
{
    private static HttpClient CreateClientWithRunner<TRunner>() where TRunner : class, IAgentRunner
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing IAgentRunner registration and replace with the throwing stub
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentRunner));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddSingleton<IAgentRunner, TRunner>();
                });
                builder.ConfigureAppConfiguration(config =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Provider:Mode"] = "Stub",
                        ["Provider:FhirBaseUrl"] = "",
                    });
                });
            });
        return factory.CreateClient();
    }

    [Fact]
    public async Task Post_copilot_returns_502_on_HttpRequestException()
    {
        using var client = CreateClientWithRunner<UpstreamErrorRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("upstream_error", body!.Error.Type);
        Assert.DoesNotContain("StackTrace", body.Error.Message);
    }

    [Fact]
    public async Task Post_copilot_returns_504_on_TaskCanceledException()
    {
        using var client = CreateClientWithRunner<TimeoutRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("timeout", body!.Error.Type);
    }

    [Fact]
    public async Task Post_copilot_returns_500_on_unexpected_exception()
    {
        using var client = CreateClientWithRunner<InternalErrorRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("internal_error", body!.Error.Type);
        Assert.DoesNotContain("secret", body.Error.Message);
    }

    // --- Throwing IAgentRunner stubs ---

    private class UpstreamErrorRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new HttpRequestException("Gemini API error", null, HttpStatusCode.TooManyRequests);
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new HttpRequestException("Gemini API error");
    }

    private class TimeoutRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new TaskCanceledException("Request timed out");
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new TaskCanceledException("Request timed out");
    }

    private class InternalErrorRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new InvalidOperationException("secret internal details here");
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new InvalidOperationException("secret internal details");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FhirCopilot.Api.Tests --filter "ErrorHandlingTests" --no-build 2>&1 || true`
Expected: FAIL — currently returns 500 with empty body, not structured JSON

- [ ] **Step 3: Add try/catch to the non-streaming endpoint in Program.cs**

Replace the `/api/copilot` endpoint handler (lines 95-99) with:

```csharp
app.MapPost("/api/copilot", async (CopilotRequest request, ICopilotService service, CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.RunAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("upstream_error", "The AI service returned an error. Please retry.")),
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("timeout", "The request timed out. Please retry.")),
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("invalid_request", ex.Message)),
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (Exception)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("internal_error", "An unexpected error occurred.")),
            statusCode: StatusCodes.Status500InternalServerError);
    }
});
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FhirCopilot.Api.Tests --filter "ErrorHandlingTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```bash
git add src/FhirCopilot.Api/Program.cs tests/FhirCopilot.Api.Tests/ErrorHandlingTests.cs
git commit -m "feat: structured error responses on /api/copilot endpoint"
```

---

## Task 3: Add LogError to CopilotService

**Files:**
- Modify: `src/FhirCopilot.Api/Services/CopilotService.cs`

- [ ] **Step 1: Add LogError in RunAsync catch block**

In `CopilotService.cs`, replace the catch block (lines 66-70):

```csharp
        catch (Exception ex)
        {
            status = "error";
            _logger.LogError(ex, "Copilot request failed for agent {AgentType}, thread {ThreadId}", agentType, threadId);
            throw;
        }
```

- [ ] **Step 2: Add LogError in StreamAsync**

In `StreamAsync`, change the `try` block (lines 107-113) to include a catch before finally:

```csharp
        try
        {
            await foreach (var evt in _runner.StreamAsync(profile, request.Query, threadId, cancellationToken))
            {
                yield return evt;
            }
            completed = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copilot stream failed for agent {AgentType}, thread {ThreadId}", agentType, threadId);
            throw;
        }
```

- [ ] **Step 3: Run all tests to verify nothing breaks**

Run: `dotnet test tests/FhirCopilot.Api.Tests`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/FhirCopilot.Api/Services/CopilotService.cs
git commit -m "feat: add LogError calls in CopilotService for exception visibility"
```

---

## Task 4: Model Fallback Configuration

**Files:**
- Modify: `src/FhirCopilot.Api/Options/RuntimeOptions.cs`
- Modify: `src/FhirCopilot.Api/appsettings.json`
- Modify: `.env`
- Modify: `.env.example`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Add GeminiModels property to ProviderOptions**

In `RuntimeOptions.cs`, add after the `GeminiModel` property (line 11):

```csharp
    public List<string>? GeminiModels { get; set; }
```

And add a helper method after `HasFhirBaseUrl` (line 22):

```csharp
    public IReadOnlyList<string> GetModelChain() =>
        GeminiModels is { Count: > 0 } ? GeminiModels : [GeminiModel ?? "gemini-3-flash-preview"];
```

- [ ] **Step 2: Add GeminiModels to appsettings.json**

Update `appsettings.json` Provider section to:

```json
  "Provider": {
    "Mode": "Gemini",
    "GeminiModel": "gemini-3-flash-preview",
    "GeminiModels": [
      "gemini-3-flash-preview",
      "gemini-3.1-flash-lite-preview",
      "gemini-3.1-pro-preview"
    ],
    "FhirBaseUrl": "https://bulk-fhir.fly.dev/fhir"
  }
```

- [ ] **Step 3: Update .env.example**

Add after the existing `Provider__GeminiModel` line:

```
Provider__GeminiModels__0=gemini-3-flash-preview
Provider__GeminiModels__1=gemini-3.1-flash-lite-preview
Provider__GeminiModels__2=gemini-3.1-pro-preview
```

- [ ] **Step 4: Update CLAUDE.md model policy**

Replace the Gemini Model Policy section with:

```markdown
## Gemini Model Policy
- Allowed Gemini models: `gemini-3-flash-preview`, `gemini-3.1-flash-lite-preview`, `gemini-3.1-pro-preview`
- All other versions are NOT allowed (2.x, pro without preview, etc.)
- The fallback chain order is: flash → flash-lite → pro
- NEVER change the model names in any file — this is the user's explicit choice
- This applies to appsettings.json, .env, .env.example, ProviderOptions defaults, and GeminiAgentFrameworkRunner fallbacks
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/FhirCopilot.Api/FhirCopilot.Api.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/FhirCopilot.Api/Options/RuntimeOptions.cs src/FhirCopilot.Api/appsettings.json .env.example CLAUDE.md
git commit -m "feat: add GeminiModels fallback chain configuration"
```

---

## Task 5: Model Fallback Logic in Runner

**Files:**
- Test: `tests/FhirCopilot.Api.Tests/ModelFallbackTests.cs`
- Modify: `src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FhirCopilot.Api.Tests/ModelFallbackTests.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Options;
using FhirCopilot.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Tests;

public class ModelFallbackTests
{
    [Fact]
    public void GetModelChain_returns_GeminiModels_when_configured()
    {
        var options = new ProviderOptions
        {
            GeminiModel = "gemini-3-flash-preview",
            GeminiModels = ["gemini-3-flash-preview", "gemini-3.1-flash-lite-preview", "gemini-3.1-pro-preview"]
        };

        var chain = options.GetModelChain();

        Assert.Equal(3, chain.Count);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
        Assert.Equal("gemini-3.1-pro-preview", chain[2]);
    }

    [Fact]
    public void GetModelChain_falls_back_to_single_GeminiModel()
    {
        var options = new ProviderOptions
        {
            GeminiModel = "gemini-3-flash-preview",
            GeminiModels = null
        };

        var chain = options.GetModelChain();

        Assert.Single(chain);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
    }

    [Fact]
    public void GetModelChain_uses_default_when_nothing_set()
    {
        var options = new ProviderOptions
        {
            GeminiModel = null,
            GeminiModels = null
        };

        var chain = options.GetModelChain();

        Assert.Single(chain);
        Assert.Equal("gemini-3-flash-preview", chain[0]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FhirCopilot.Api.Tests --filter "ModelFallbackTests"`
Expected: FAIL — `GetModelChain` method does not exist yet (if Task 4 hasn't been run), or PASS if Task 4 is done

- [ ] **Step 3: Implement fallback in GeminiAgentFrameworkRunner**

Modify `GeminiAgentFrameworkRunner.cs`:

**a) Change the agent cache key to include model name.** Replace the `GetOrCreateAgent` method signature and cache key:

```csharp
    private AIAgent GetOrCreateAgent(AgentProfile profile, string model)
    {
        var cacheKey = $"{profile.Name}::{model}";
        return _agents.GetOrAdd(cacheKey, _ =>
        {
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
```

**b) Replace `RunAsync` with fallback loop:**

```csharp
    public async Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RunAsync started for agent {AgentName}, thread {ThreadId}", profile.Name, threadId);

        var models = _provider.GetModelChain();
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
                System.Diagnostics.Activity.Current?.SetTag("copilot.model", model);
                _logger.LogInformation("RunAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
                    profile.Name, threadId, model, answer.Length);
                return BuildResponse(answer, profile, threadId);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                lastException = ex;
                _logger.LogWarning("Model {Model} rate-limited (429), falling back to next model", model);
            }
        }

        throw lastException!;
    }
```

**c) Replace `StreamAsync` with fallback loop:**

```csharp
    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        AgentProfile profile,
        string query,
        string threadId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var models = _provider.GetModelChain();
        HttpRequestException? lastException = null;

        yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: false);

        foreach (var model in models)
        {
            var succeeded = false;
            var answerBuilder = new StringBuilder();

            _logger.LogInformation("StreamAsync attempting model {Model} for agent {AgentName}, thread {ThreadId}",
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
                        _logger.LogWarning("Model {Model} rate-limited (429) during stream, falling back to next model", model);
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
                System.Diagnostics.Activity.Current?.SetTag("copilot.model", model);
                _logger.LogInformation("StreamAsync completed for agent {AgentName}, thread {ThreadId}, model {Model}, answer length {AnswerLength}",
                    profile.Name, threadId, model, answer.Length);
                yield return CopilotStreamEvent.Done(BuildResponse(answer, profile, threadId));
                yield break;
            }
        }

        throw lastException!;
    }
```

**d) Add `using System.Net;`** at the top of the file.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/FhirCopilot.Api.Tests`
Expected: All tests pass (including existing streaming/OTEL tests)

- [ ] **Step 5: Commit**

```bash
git add src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs tests/FhirCopilot.Api.Tests/ModelFallbackTests.cs
git commit -m "feat: Gemini model fallback on 429 with configurable chain"
```

---

## Task 6: Trace-Enriched Console Formatter

**Files:**
- Test: `tests/FhirCopilot.Api.Tests/TraceConsoleFormatterTests.cs`
- Create: `src/FhirCopilot.ServiceDefaults/TraceEnrichedConsoleFormatter.cs`
- Modify: `src/FhirCopilot.ServiceDefaults/Extensions.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FhirCopilot.Api.Tests/TraceConsoleFormatterTests.cs`:

```csharp
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace FhirCopilot.Api.Tests;

public class TraceConsoleFormatterTests
{
    [Fact]
    public void Format_includes_traceId_and_spanId_when_activity_active()
    {
        var source = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test-span")!;
        Assert.NotNull(activity);

        var formatter = new TraceEnrichedConsoleFormatter(
            Options.Create(new ConsoleFormatterOptions()));

        using var writer = new StringWriter();
        var entry = new LogEntry<string>(
            LogLevel.Information, "TestCategory", new EventId(0), "Hello world", null,
            (state, _) => state);

        formatter.Write(entry, null, writer);
        var output = writer.ToString();

        // Should contain truncated trace ID (first 12 chars)
        Assert.Contains(activity.TraceId.ToString()[..12], output);
        Assert.Contains("Information", output);
        Assert.Contains("Hello world", output);
    }

    [Fact]
    public void Format_omits_trace_bracket_when_no_activity()
    {
        Activity.Current = null;

        var formatter = new TraceEnrichedConsoleFormatter(
            Options.Create(new ConsoleFormatterOptions()));

        using var writer = new StringWriter();
        var entry = new LogEntry<string>(
            LogLevel.Warning, "TestCategory", new EventId(0), "No trace", null,
            (state, _) => state);

        formatter.Write(entry, null, writer);
        var output = writer.ToString();

        Assert.Contains("Warning", output);
        Assert.Contains("No trace", output);
        // No trace bracket — just level + message
        Assert.DoesNotContain("[0000", output);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FhirCopilot.Api.Tests --filter "TraceConsoleFormatterTests" 2>&1 || true`
Expected: FAIL — `TraceEnrichedConsoleFormatter` does not exist

- [ ] **Step 3: Create TraceEnrichedConsoleFormatter**

Create `src/FhirCopilot.ServiceDefaults/TraceEnrichedConsoleFormatter.cs`:

```csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

public sealed class TraceEnrichedConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "trace-enriched";
    private readonly ConsoleFormatterOptions _options;

    public TraceEnrichedConsoleFormatter(IOptions<ConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        _options = options.Value;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (message is null) return;

        var timestamp = _options.UseUtcTimestamp
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.Now;

        var activity = Activity.Current;

        textWriter.Write('[');
        textWriter.Write(timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        textWriter.Write("] [");
        textWriter.Write(logEntry.LogLevel);
        textWriter.Write(']');

        if (activity is not null)
        {
            textWriter.Write(" [");
            textWriter.Write(activity.TraceId.ToString()[..12]);
            textWriter.Write(' ');
            textWriter.Write(activity.SpanId.ToString());
            textWriter.Write(']');
        }

        textWriter.Write(' ');
        textWriter.Write(logEntry.Category);
        textWriter.Write(": ");
        textWriter.WriteLine(message);

        if (logEntry.Exception is not null)
        {
            textWriter.WriteLine(logEntry.Exception.ToString());
        }
    }
}
```

- [ ] **Step 4: Register the formatter in Extensions.cs**

In `ConfigureOpenTelemetry()` in `Extensions.cs`, add after the `AddOpenTelemetry` logging call (after line 36):

```csharp
        builder.Logging
            .AddConsole(options => options.FormatterName = TraceEnrichedConsoleFormatter.FormatterName)
            .AddConsoleFormatter<TraceEnrichedConsoleFormatter, ConsoleFormatterOptions>();
```

Add required using at top of file:

```csharp
using Microsoft.Extensions.Logging.Console;
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/FhirCopilot.Api.Tests --filter "TraceConsoleFormatterTests"`
Expected: 2 passed

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/FhirCopilot.Api.Tests`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add src/FhirCopilot.ServiceDefaults/TraceEnrichedConsoleFormatter.cs src/FhirCopilot.ServiceDefaults/Extensions.cs tests/FhirCopilot.Api.Tests/TraceConsoleFormatterTests.cs
git commit -m "feat: trace-enriched console formatter for local dev logging"
```

---

## Task 7: Aspire AppHost FHIR Backend Resource

**Files:**
- Modify: `src/FhirCopilot.AppHost/Program.cs`

- [ ] **Step 1: Add FHIR backend as external HTTP resource**

Replace `src/FhirCopilot.AppHost/Program.cs` with:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var fhirBaseUrl = builder.AddParameter("fhir-base-url", "https://bulk-fhir.fly.dev/fhir");

builder.AddProject<Projects.FhirCopilot_Api>("api")
    .WithEnvironment("Provider__FhirBaseUrl", fhirBaseUrl);

builder.Build().Run();
```

- [ ] **Step 2: Verify AppHost builds**

Run: `dotnet build src/FhirCopilot.AppHost/FhirCopilot.AppHost.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/FhirCopilot.AppHost/Program.cs
git commit -m "feat: add FHIR backend as Aspire parameter resource"
```

---

## Task 8: Final Validation & Config Updates

**Files:**
- Modify: `.env`
- Modify: `docs/PROD_READINESS.md`

- [ ] **Step 1: Update .env with model chain**

Ensure `.env` has:

```
Provider__GeminiModels__0=gemini-3-flash-preview
Provider__GeminiModels__1=gemini-3.1-flash-lite-preview
Provider__GeminiModels__2=gemini-3.1-pro-preview
```

- [ ] **Step 2: Run full test suite**

Run: `dotnet test tests/FhirCopilot.Api.Tests`
Expected: All tests pass

- [ ] **Step 3: Update PROD_READINESS.md**

Mark items 1 (error handling) and 2 (structured logging enrichment) as completed in the Next Steps section. Add model fallback to the Completed Work table.

- [ ] **Step 4: Commit**

```bash
git add .env docs/PROD_READINESS.md
git commit -m "docs: update PROD_READINESS with completed error handling and logging work"
```

- [ ] **Step 5: Push to deploy**

```bash
git push origin main
```
