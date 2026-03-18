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
| 7 | Functional | `efc170f` | Primary constructor, immutable session, SseWriter, Calculator guard |
| 8 | Prod config | `46b2f61` | Dockerfile + fly.toml OTEL env vars |
| 9 | Tests | `4f60957` | OTEL span tests, /alive test (38 total) |
| 10 | Config cleanup | `d2bfff4` | Gemini default, .env loading, removed UseStubWhenProviderMissing |
| 11 | Native Gemini SDK | `3292d7a` | Replaced OpenAI compat layer with Google_GenerativeAI native SDK |
| 12 | Custom metrics | — | `FhirCopilot.Agent` meter: request counter, duration histogram, routing decisions, session lifecycle |

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
| 4 | **Inconsistent error handling in HttpFhirBackend** | `FetchAllEntriesAsync` now logs and breaks on HTTP errors instead of throwing `HttpRequestException`. All paths (search, read, export) now log failures consistently and return empty/null results. | Search methods threw unhandled exceptions while read/export silently returned null. Pick one pattern and stick with it. |
| 5 | **Structured logging via ILogger** | Added `ILogger<T>` to `CopilotService`, `GeminiAgentFrameworkRunner`, `KeywordIntentRouter`, and `HttpFhirBackend`. Logs routing decisions, session lifecycle, FHIR errors, and agent runs. | ILogger is the standard .NET abstraction that OTEL, Seq, Application Insights, and every other sink hooks into. Adding it now means OTEL integration is a one-line `AddOpenTelemetry()` call later, not a retrofit. |

## Accepted for Prototype — Required for Prod

| # | Gap | Current State | Prod Requirement | Effort |
|---|-----|---------------|------------------|--------|
| 1 | **Authentication & authorization** | No auth middleware. Endpoints are open. | OIDC/JWT + SMART-on-FHIR scopes. Minimum: bearer token validation on all `/api/*` routes. | Medium |
| 2 | **Audit logging** | No audit trail for who queried what. | HIPAA requires audit logs for PHI access. Log user identity, query, agent used, resources accessed. | Medium |
| 3 | **Session durability** | In-memory `ConcurrentDictionary`, lost on restart. | Redis or distributed cache for HA. Sessions must survive deployments. | Medium |
| 4 | **LRU eviction strategy** | Drops 50% of sessions when hitting 200 cap. | Gradual eviction (10% or TTL-based expiry). Consider `IMemoryCache` with sliding expiration. | Low |
| 5 | **Bulk export orchestration** | Synchronous 30-second polling loop blocks the agent. | Background jobs or durable orchestration (e.g., Hangfire, Azure Durable Functions). Real exports take minutes/hours. | High |
| 6 | **Rate limiting** | Gemini API calls are unbounded per user. | Per-tenant/per-user throttling. ASP.NET Core rate limiting middleware or API gateway. | Low |
| 7 | **Calculator sandboxing** | `DataTable.Compute()` evaluates arbitrary expressions. | Replace with a sandboxed expression parser (e.g., NCalc, custom tokenizer). Low risk in demo but not acceptable for prod. | Low |
| 8 | **Tool return types** | All tools return serialized JSON strings, not typed objects. | When Microsoft.Agents.AI reaches GA, migrate to typed tool results for introspection and validation. | Medium |
| 9 | **Citation extraction** | Regex on LLM output text (`Patient/123` patterns). | Explicit `EvidenceItem` results emitted by tools. Already noted in ARCHITECTURE.md cutover plan. | Medium |
| 10 | **Keyword router limitations** | Deterministic keyword scoring with hardcoded boosts. Works for demo queries, fails on ambiguous ones. | LLM-based router with deterministic fallback. Already noted in ARCHITECTURE.md cutover plan. | Medium |
| 11 | **E2E test suite** | No tests. | Cover: routing decisions, streaming contract, backend swap (stub vs HTTP), tool dispatch, error paths. | Medium |
| 12 | **CI/CD pipeline** | Docker exists, no automation. | GitHub Actions or equivalent: build, test, Docker publish, deploy. | Low |
| 13 | **OTEL observability** | OTLP exporter configured (traces, metrics, logs). Custom `FhirCopilot.Agent` meter with request counters, duration histograms, session lifecycle, and routing decision metrics. | Define alert rules for error rate spikes and latency degradation. | Low |
| 14 | **Microsoft.Agents.AI stability** | Pinned to `1.0.0-rc4` (prerelease). | Monitor for GA release. RC versions may have breaking API changes. Pin version and test upgrades explicitly. | Ongoing |

## Next Steps

### High priority
1. **Error handling for Gemini failures** — CopilotService should catch runner exceptions and return structured error responses instead of 500s with stack traces
2. **Structured logging enrichment** — Add trace/span IDs to log entries so logs correlate with traces in the dashboard

### Medium priority
4. **Aspire AppHost enhancements** — Add FHIR backend as an external resource in the dashboard, configure environment variables for the Api project
5. **Integration test with real Gemini** — Optional test category that hits the real API (skipped in CI without GEMINI_API_KEY)
6. **Response caching** — Cache FHIR backend responses to reduce latency on repeated tool calls within a session

### Nice to have
7. **Multi-provider support** — IAgentRunner implementations for Claude, Azure OpenAI (the interface is ready)
8. **Token usage tracking** — Extract gen_ai.usage.input_tokens/output_tokens from GenAI spans into a dashboard panel
9. **Alert rules** — Define OTEL alert rules for error rate spikes or latency degradation
