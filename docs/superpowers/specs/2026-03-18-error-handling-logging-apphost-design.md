# Design Spec: Error Handling, Model Fallback, Logging, and AppHost

**Date:** 2026-03-18
**Status:** Draft
**Scope:** Four incremental improvements to the FHIR Copilot Agent Framework Starter (.NET 9 / ASP.NET Core API with Gemini AI, deployed on Fly.io)

---

## Table of Contents

1. [Error Handling -- Structured Error Responses](#item-1-error-handling--structured-error-responses)
2. [Gemini Model Fallback on 429](#item-2-gemini-model-fallback-on-429)
3. [Structured Logging -- Trace/Span ID Enrichment](#item-3-structured-logging--tracespan-id-enrichment)
4. [Aspire AppHost -- FHIR Backend as External Resource](#item-4-aspire-apphost--fhir-backend-as-external-resource)
5. [Testing Strategy](#testing-strategy)
6. [Execution Order](#execution-order)

---

## Item 1: Error Handling -- Structured Error Responses

### Problem

The non-streaming `POST /api/copilot` endpoint in `Program.cs` has no try/catch around the call to `service.RunAsync`. When Gemini (or any downstream dependency) throws, ASP.NET returns a raw 500 with an empty body. Callers receive no actionable information about what went wrong.

Meanwhile, `CopilotService` catches exceptions only to flip its metric status tag to `"error"` and re-throws -- it never calls `_logger.LogError`, so the exception details are lost from application logs unless the ASP.NET host-level logging happens to capture them.

The streaming endpoint (`POST /api/copilot/stream`) is already covered: `SseWriter.WriteAsync` catches non-cancellation exceptions and emits an `event: error` SSE frame. No changes are needed there.

### Design

#### Error contract

Add two records to `src/FhirCopilot.Api/Contracts/CopilotContracts.cs`:

```csharp
public sealed record CopilotError(string Type, string Message);
public sealed record CopilotErrorResponse(CopilotError Error);
```

The `Type` field is a machine-readable discriminator. The `Message` field is a human-readable summary safe for display (no stack traces in production).

#### Non-streaming endpoint

Add a try/catch to the `/api/copilot` handler in `Program.cs`. Map exception types to HTTP status codes and error types:

| Exception | HTTP Status | `error.type` |
|---|---|---|
| `HttpRequestException` (Gemini 4xx/5xx) | 502 Bad Gateway | `upstream_error` |
| `TaskCanceledException` / `OperationCanceledException` (timeout, not client disconnect) | 504 Gateway Timeout | `timeout` |
| `ArgumentException` / validation failures | 400 Bad Request | `invalid_request` |
| Any other exception | 500 Internal Server Error | `internal_error` |

Response body shape in all error cases:

```json
{
  "error": {
    "type": "upstream_error",
    "message": "The Gemini API returned an error. Please retry."
  }
}
```

Stack traces are never included in the response body. The message should be descriptive enough to guide the caller without leaking internals.

#### CopilotService logging

Add `_logger.LogError(ex, ...)` calls in the existing catch blocks of both `RunAsync` and `StreamAsync` in `CopilotService.cs`. Currently the catch blocks (lines 66-69 for `RunAsync`, and the implicit finally-only path for `StreamAsync`) set `status = "error"` and re-throw but never log the exception. The log calls should fire before re-throwing so the exception is always recorded in application logs regardless of how the caller handles it.

#### Streaming endpoint

No changes. `SseWriter` already catches exceptions and writes an `event: error` SSE frame with the exception message.

### Files to modify

- `src/FhirCopilot.Api/Contracts/CopilotContracts.cs` -- add `CopilotError` and `CopilotErrorResponse` records
- `src/FhirCopilot.Api/Program.cs` -- add try/catch to the `/api/copilot` handler
- `src/FhirCopilot.Api/Services/CopilotService.cs` -- add `LogError` calls in catch/finally paths

---

## Item 2: Gemini Model Fallback on 429

### Problem

When the primary Gemini model returns HTTP 429 (Too Many Requests / rate limited), the entire request fails. There is no automatic retry or fallback to an alternative model. This is especially problematic during peak usage or when a particular model variant hits its quota.

### Design

#### Fallback chain

Define an ordered list of Gemini models. When a model returns 429, the next model in the chain is tried. Each model gets exactly one attempt -- there are no retry loops on the same model.

1. `gemini-3-flash-preview` (primary, current default)
2. `gemini-3.1-flash-lite-preview` (first fallback)
3. `gemini-3.1-pro-preview` (second fallback)

If all models in the chain return 429, the request fails with a structured error (using the error contract from Item 1): HTTP 502, type `"upstream_error"`, message indicating rate limiting across all available models.

#### Configuration

The fallback chain is defined in `appsettings.json` under `Provider`:

```json
{
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
}
```

`ProviderOptions` gets a new property:

- `List<string>? GeminiModels` -- the ordered fallback chain. If set (non-null, non-empty), this is used for fallback. If not set, the runner falls back to the single `GeminiModel` property for backward compatibility (no fallback behavior, single model only).

The existing `GeminiModel` property continues to serve as the default for single-model setups and as a readable "primary model" identifier. The first entry in `GeminiModels` should match `GeminiModel` by convention, but the runner uses `GeminiModels` as the authoritative chain when present.

#### Agent cache key change

Currently `GeminiAgentFrameworkRunner` caches `AIAgent` instances in a `ConcurrentDictionary<string, AIAgent>` keyed by `profile.Name` alone. Each agent is bound to a specific model at creation time via `new GenerativeAIChatClient(apiKey, model)`.

For fallback support, the cache key must include the model name: `(profileName, model)`. The `GetOrCreateAgent` method should accept a `model` parameter and use a composite key like `$"{profileName}::{model}"`.

#### Fallback logic in GeminiAgentFrameworkRunner

The fallback logic lives in `RunAsync` and `StreamAsync` within `GeminiAgentFrameworkRunner`. The approach:

1. Iterate through `_provider.GeminiModels` (or fall back to `[_provider.GeminiModel]` if `GeminiModels` is not configured).
2. For each model, call `GetOrCreateAgent(profile, model)` and attempt the run.
3. If the run throws an `HttpRequestException` whose status code is 429, log a warning and continue to the next model.
4. If the run succeeds, record which model served the request by setting `Activity.Current?.SetTag("copilot.model", model)`.
5. If all models are exhausted, throw the last 429 exception (which will be caught by the endpoint error handler from Item 1).

The fallback is per-request. The next request always starts from the first model in the chain.

#### Observability

- Log each fallback at `Warning` level: `"Model {Model} rate-limited (429), falling back to {FallbackModel}"`
- Log final model selection at `Information` level: `"Request served by model {Model}"`
- Set OTEL span tag `copilot.model` to the model that actually served the request

#### `.env` and `.env.example` updates

Add the `GeminiModels` configuration. The `.env.example` should document the fallback chain:

```
Provider__GeminiModels__0=gemini-3-flash-preview
Provider__GeminiModels__1=gemini-3.1-flash-lite-preview
Provider__GeminiModels__2=gemini-3.1-pro-preview
```

#### CLAUDE.md update

Update the Gemini Model Policy section to list all three allowed models and explain the fallback chain.

### Files to modify

- `src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs` -- fallback loop, agent cache key change
- `src/FhirCopilot.Api/Options/RuntimeOptions.cs` -- add `GeminiModels` property to `ProviderOptions`
- `src/FhirCopilot.Api/appsettings.json` -- add `GeminiModels` array
- `.env.example` -- add `GeminiModels` entries
- `CLAUDE.md` -- update model policy

---

## Item 3: Structured Logging -- Trace/Span ID Enrichment

### Problem

When running locally without the Aspire dashboard (i.e., `dotnet run` directly, no OTLP exporter configured), console log output has no trace or span ID correlation. The OTEL logging bridge configured in `Extensions.cs` attaches trace context only to OTLP-exported `LogRecord` objects, not to the built-in console formatter. This makes it difficult to correlate log lines with specific requests during local development.

### Design

#### Custom console formatter

Create a `TraceEnrichedConsoleFormatter` that extends `Microsoft.Extensions.Logging.Console.ConsoleFormatter`. The formatter reads `Activity.Current?.TraceId` and `Activity.Current?.SpanId` at the time each log entry is written and includes them in the output.

**Format when an active span exists:**

```
[2026-03-18T04:00:00Z] [Information] [abc123def456 aabb1122] Routed query to agent lookup
```

The trace ID is truncated to the first 12 hex characters for readability (the full 32-character trace ID is available in OTEL exports). The span ID is shown in full (16 hex characters).

**Format when no active span exists:**

```
[2026-03-18T04:00:00Z] [Information] Routed query to agent lookup
```

The trace/span bracket is omitted entirely rather than showing empty placeholders.

#### Registration

Register the formatter in the `ConfigureOpenTelemetry` method in `Extensions.cs`, alongside the existing `AddOpenTelemetry` logging call. This keeps all observability configuration in one place.

Registration uses the standard ASP.NET Core console formatter extension point:

```
builder.Logging
    .AddConsole(options => options.FormatterName = "trace-enriched")
    .AddConsoleFormatter<TraceEnrichedConsoleFormatter, ConsoleFormatterOptions>()
```

This replaces the default console formatter. The OTEL logging bridge (`AddOpenTelemetry`) remains unchanged and continues to export full trace context to OTLP when configured.

### Files to modify

- `src/FhirCopilot.ServiceDefaults/TraceEnrichedConsoleFormatter.cs` -- new file
- `src/FhirCopilot.ServiceDefaults/Extensions.cs` -- register the custom formatter

---

## Item 4: Aspire AppHost -- FHIR Backend as External Resource

### Problem

The FHIR backend URL (`https://bulk-fhir.fly.dev/fhir`) is hardcoded in `appsettings.json` and loaded via `ProviderOptions.FhirBaseUrl`. It does not appear in the Aspire dashboard as a resource, so there is no visibility into the external dependency from the orchestrator view. Operators looking at the Aspire dashboard see the `api` project but have no indication that it depends on an external FHIR server.

### Design

#### External HTTP resource in AppHost

The `FhirCopilot.AppHost/Program.cs` currently has a minimal setup:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.AddProject<Projects.FhirCopilot_Api>("api");
builder.Build().Run();
```

Add the FHIR backend as an external HTTP resource so it appears in the Aspire dashboard. Use `AddConnectionString` or the Aspire `AddParameter` / resource model to surface the URL. The exact API depends on the Aspire SDK version (13.1.2 per the `.csproj`), but the intent is:

1. Declare the FHIR backend URL as a named resource in the AppHost.
2. Wire it into the `api` project via `WithReference` so the URL is injected through Aspire's standard configuration mechanism rather than only through `appsettings.json`.
3. The FHIR backend appears in the Aspire dashboard resource list with its URL visible.

The `api` project's `ProviderOptions.FhirBaseUrl` continues to work as-is -- Aspire's configuration injection populates the same configuration key. The `appsettings.json` value serves as the fallback when running outside the Aspire host.

#### Health check (stretch goal)

Optionally add a health check for the FHIR backend by issuing a GET to the `/metadata` endpoint (the FHIR capability statement). This would surface in both the Aspire dashboard health column and the `/health` endpoint. This is a stretch goal because:

- The FHIR backend is external and may have rate limits on the metadata endpoint.
- A failing health check for an external dependency should not prevent the API from starting.

If implemented, the health check should be tagged as `"ready"` (not `"live"`) so it does not affect the `/alive` liveness probe.

### Files to modify

- `src/FhirCopilot.AppHost/Program.cs` -- add FHIR backend resource, wire with `WithReference`
- `src/FhirCopilot.AppHost/FhirCopilot.AppHost.csproj` -- may need an additional Aspire hosting package depending on the resource API used

---

## Testing Strategy

### Error handling tests

Use the existing `CopilotFixture` / `WebApplicationFactory<Program>` pattern. Create a custom factory that registers a stub `IAgentRunner` implementation which throws specific exception types.

**Test cases:**
- `POST /api/copilot` returns `502` with `{"error":{"type":"upstream_error","message":"..."}}` when the runner throws `HttpRequestException`
- `POST /api/copilot` returns `504` with `{"error":{"type":"timeout","message":"..."}}` when the runner throws `TaskCanceledException`
- `POST /api/copilot` returns `400` with `{"error":{"type":"invalid_request","message":"..."}}` when the runner throws `ArgumentException`
- `POST /api/copilot` returns `500` with `{"error":{"type":"internal_error","message":"..."}}` for unexpected exceptions
- Response body deserializes cleanly into `CopilotErrorResponse`
- No stack traces appear in any error response body

### Model fallback tests

These test `GeminiAgentFrameworkRunner` in isolation (unit tests, not integration tests) since they require mocking the Gemini client.

**Test cases:**
- A 429 from the primary model triggers a call with the second model
- A 429 from both the first and second models triggers a call with the third model
- Exhaustion of all models (all return 429) results in an exception that maps to a structured error
- A non-429 error from the primary model does not trigger fallback (it propagates immediately)
- The `copilot.model` span tag reflects the model that actually served the request
- When `GeminiModels` is not configured, the runner uses `GeminiModel` with no fallback behavior

### Logging tests

**Test cases:**
- When an `Activity` is active, the console output includes trace and span IDs in the expected format
- When no `Activity` is active, the console output omits the trace/span bracket entirely
- The formatter does not throw or corrupt output for any log level

### AppHost integration

The AppHost changes are structural (resource declaration) and are best validated by running the Aspire host locally and confirming the FHIR backend appears in the dashboard. Automated testing of Aspire resource registration is not practical without the full Aspire test host, so manual verification is acceptable here.

---

## Execution Order

The items have the following dependency relationships:

```
Item 1 (Error contracts + endpoint handling)
  |
  v
Item 2 (Model fallback -- depends on error contracts for structured 502 on chain exhaustion)

Item 3 (Trace-enriched console formatter -- independent)

Item 4 (Aspire AppHost FHIR resource -- independent)
```

Recommended implementation sequence:

1. **Error contracts + error handling in endpoints** -- foundation that the other items build on
2. **LogError calls in CopilotService** -- quick addition, completes the error handling story
3. **Model fallback in GeminiAgentFrameworkRunner** -- depends on the error contract from step 1
4. **Trace-enriched console formatter** -- independent, can be done in parallel with step 3
5. **Aspire AppHost FHIR resource** -- independent, can be done in parallel with steps 3-4
