# Aspire + OTEL + Functional Refactors — Completed

## What was done

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

## How to run

```bash
# Dev (with Aspire dashboard)
dotnet run --project src/FhirCopilot.AppHost

# Tests
dotnet test
```

Requires `.env` at repo root with `GEMINI_API_KEY`.

## Next steps

### High priority
1. **Error handling for Gemini failures** — CopilotService should catch runner exceptions and return structured error responses instead of 500s with stack traces
2. **Structured logging enrichment** — Add trace/span IDs to log entries so logs correlate with traces in the dashboard
3. **Custom metrics** — Add counters for requests per agent, latency histograms, error rates via `FhirCopilot.Agent` meter

### Medium priority
4. **Aspire AppHost enhancements** — Add FHIR backend as an external resource in the dashboard, configure environment variables for the Api project
5. **Integration test with real Gemini** — Optional test category that hits the real API (skipped in CI without GEMINI_API_KEY)
6. **Response caching** — Cache FHIR backend responses to reduce latency on repeated tool calls within a session

### Nice to have
7. **Multi-provider support** — IAgentRunner implementations for Claude, Azure OpenAI (the interface is ready)
8. **Token usage tracking** — Extract gen_ai.usage.input_tokens/output_tokens from GenAI spans into a dashboard panel
9. **Alert rules** — Define OTEL alert rules for error rate spikes or latency degradation
