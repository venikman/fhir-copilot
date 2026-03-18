# Production Readiness: Prototype vs Prod Gaps

This document captures known engineering gaps that are acceptable in the current demo/prototype but must be addressed before handling real patient data.

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
| 13 | **OTEL observability** | ILogger in place, no exporters configured. | `AddOpenTelemetry()` with traces + metrics + logs. Wire up to Jaeger/OTLP collector. ILogger calls are already structured and ready. | Low |
| 14 | **Microsoft.Agents.AI stability** | Pinned to `1.0.0-rc4` (prerelease). | Monitor for GA release. RC versions may have breaking API changes. Pin version and test upgrades explicitly. | Ongoing |
