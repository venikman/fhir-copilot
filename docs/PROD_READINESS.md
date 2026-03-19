# Production Readiness: Prototype vs Prod Gaps

This document captures known engineering gaps that are acceptable in the current demo/prototype but must be addressed before handling real patient data.

## Completed Work

| # | Step | Commit | What changed |
|---|------|--------|-------------|
| 1 | ServiceDefaults | `6ba2849` | New `FhirCopilot.ServiceDefaults` project: OTEL, health checks, resilience |
| 2 | Wire into Api | `76885b3` | `AddServiceDefaults()` / `MapDefaultEndpoints()` in Program.cs |
| 3 | Aspire AppHost | `57291bf` | New `FhirCopilot.AppHost` project for dev dashboard |
| 4 | GenAI OTEL | `d0a6526` | `UseOpenTelemetry()` on IChatClient for GenAI semantic conventions |
| 5 | Agent spans | `c1036c8` | `FhirCopilot.Agent` ActivitySource with copilot.request/stream spans |
| 6 | IAgentRunner | `c0294be` | Extracted interface, DI-time runner selection |
| 7 | Functional | `efc170f` | Primary constructor, immutable session, Calculator guard |
| 8 | Prod config | `46b2f61` | Dockerfile + fly.toml OTEL env vars |
| 9 | Tests | `4f60957` | OTEL span tests, /alive test (38 total) |
| 10 | Config cleanup | `d2bfff4` | Gemini default, .env loading, removed UseStubWhenProviderMissing |
| 11 | Native Gemini SDK | `3292d7a` | Replaced OpenAI compat layer with Google_GenerativeAI native SDK |
| 12 | Custom metrics | — | `FhirCopilot.Agent` meter: request counter, duration histogram, routing decisions, session lifecycle |
| 13 | Error handling | `d3c1dd9` | Structured error responses via HubException (upstream_error/timeout/internal_error), LogError in CopilotService |
| 14 | Model fallback | `229d355` | Gemini 429 fallback chain: flash → flash-lite → pro, configurable via `GeminiModels` |
| 15 | Logging enrichment | `8e185b9` | Trace-enriched console formatter with trace/span IDs for local dev |
| 16 | AppHost FHIR resource | `8b81b46` | FHIR backend as Aspire parameter resource with env var injection |

## How to Run

```bash
# Dev (with Aspire dashboard)
dotnet run --project src/FhirCopilot.AppHost

# Tests
dotnet test
```

Requires `.env` at repo root with `GEMINI_API_KEY`.

## Addressed in This Prototype

These were fixed because they teach good engineering habits regardless of context:

| # | Issue | Fix | Why it matters |
|---|-------|-----|----------------|
| 1 | **Session race condition** | Moved all session access inside the semaphore in `GeminiAgentFrameworkRunner`. Removed the lock-free optimistic read that could race with LRU eviction. | Concurrency bugs are silent until they aren't. The pattern of "read outside lock, write inside lock" on mutable state is a classic defect. |
| 2 | **Startup tool config validation** | `ToolRegistry.ValidateProfiles()` runs at startup and logs warnings for unknown tool names in agent configs. | A typo in `config/agents/*.json` silently disables a tool with no feedback. This is a debugging nightmare in a multi-agent system. |
| 3 | **Misleading confidence signal** | Changed `BuildResponse` to always return `"unverified"` instead of deriving confidence from citation count. | Confidence based on "did the LLM mention a FHIR reference in its text" is actively misleading. A correct answer without slash-delimited references was tagged "low". Better to be honest than wrong. |
| 4 | **Removed production FHIR HTTP client** | `HttpFhirBackend` (573 lines, zero test coverage) was removed. The demo uses `SampleFhirBackend`. A production `IFhirBackend` implementation can be added when needed with proper error handling from the start. | Untested production code is worse than no production code — it gives false confidence. |
| 5 | **Structured logging via ILogger** | Added `ILogger<T>` to `CopilotService`, `GeminiAgentFrameworkRunner`, `KeywordIntentRouter`, and `HttpFhirBackend`. Logs routing decisions, session lifecycle, FHIR errors, and agent runs. | ILogger is the standard .NET abstraction that OTEL, Seq, Application Insights, and every other sink hooks into. Adding it now means OTEL integration is a one-line `AddOpenTelemetry()` call later, not a retrofit. |

## Out of Scope (prototype/demo)

These are real prod concerns but won't be pursued — this project stays at demo/prototype scope.

| Gap | Why skipped |
|-----|-------------|
| Authentication & authorization | Demo runs against synthetic FHIR data, no real PHI |
| Audit logging (HIPAA) | No real patients, no compliance requirement |
| Session durability (Redis) | In-memory is fine for single-instance demo |
| LRU eviction tuning | 200-session cap with 50% drop is adequate for demo load |
| Bulk export orchestration | Demo uses small datasets; synchronous polling is acceptable |
| Rate limiting | Single-user demo, no abuse vector |
| Calculator sandboxing | `DataTable.Compute()` risk is negligible without untrusted input |
| Typed tool returns | Blocked on Microsoft.Agents.AI GA; JSON strings work fine |
| Citation extraction upgrade | Regex approach works for demo queries |
| LLM-based router | Keyword router covers the demo query set |
| E2E test suite | Unit tests (38) cover the critical paths |
| CI/CD pipeline | Manual `fly deploy` is sufficient |
| OTEL alert rules | Traces/metrics flow to Phoenix; alerting is ops, not demo |
| Microsoft.Agents.AI GA tracking | Pinned to rc4, no action until GA ships |
| Multi-provider support | IAgentRunner interface is ready if needed later |
| Token usage tracking | Spans already capture gen_ai usage; dashboard is optional |
| Response caching | Demo latency is acceptable without caching |

## Status

All planned work is complete. See **Completed Work** (16 steps) and **Addressed in This Prototype** (5 fixes) above.
