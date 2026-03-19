# FHIR Copilot Agent Framework Starter

A C# / ASP.NET Core starter for rebuilding `venikman/fhir-agents` on Microsoft Agent Framework with Gemini.

It is intentionally **thin but runnable**:

- a minimal HTTP API with health, sync query, and SSE streaming endpoints
- externalized runtime agent profiles in `src/FhirCopilot.Api/config/agents/*.json`
- a deterministic keyword router for first boot
- a Gemini-backed Microsoft Agent Framework runner that activates when `GEMINI_API_KEY` is set
- a local LLM runner via any OpenAI-compatible server (LM Studio, Ollama, etc.)
- `IFhirBackend` interface for plugging in any FHIR R4 data source

## What this starter optimizes for

1. **Fail fast.** Missing LLM provider or FHIR backend crashes at startup with a clear error.
2. **Config-first agent evolution.** Prompts, allowed tools, and API preference live in files, not code.
3. **Low-regret cutover path.** Replace the sample FHIR backend with a Firely-based client later, and replace the deterministic router with a router agent later.
4. **Evidence-first answers.** The response envelope keeps citations, reasoning, tools used, agent used, confidence, and thread id.

## Repo map

```text
.
├── AGENTS.md                       # repo-wide orchestration policy
├── docs/
│   ├── ARCHITECTURE.md
│   ├── DECISIONS.md                # short decision log (15 entries)
│   ├── GETTING_STARTED.md          # full setup guide: modes, env, tests
│   ├── PROD_READINESS.md           # production gaps + completed work
│   ├── compliance/
│   │   └── hipaa-logging.md
│   ├── tradeoffs/
│   ├── tutorials/
│   └── http/copilot.http
├── src/
│   ├── FhirCopilot.Api/
│   │   ├── Contracts/
│   │   ├── Fhir/
│   │   ├── Hubs/
│   │   ├── Models/
│   │   ├── Options/
│   │   ├── Services/
│   │   ├── config/agents/
│   │   ├── appsettings.json
│   │   └── Program.cs
│   ├── FhirCopilot.AppHost/        # Aspire dev dashboard
│   └── FhirCopilot.ServiceDefaults/ # OTEL, health checks, resilience
├── tests/
│   └── FhirCopilot.Api.Tests/
└── .env.example
```

## Quick start

```bash
# Run with Local LLM (LM Studio on port 1234)
Provider__Mode=Local \
Provider__LocalEndpoint=http://localhost:1234/v1 \
Provider__LocalModel=zai-org/glm-4.7-flash \
dotnet run --project src/FhirCopilot.Api

# Run with Gemini (remote LLM)
Provider__Mode=Gemini \
GEMINI_API_KEY=your-key \
dotnet run --project src/FhirCopilot.Api

# Run tests (no LLM or FHIR server needed)
dotnet test

# Aspire dashboard (traces, metrics, logs)
dotnet run --project src/FhirCopilot.AppHost
```

No built-in `IFhirBackend` ships — register your own implementation in `Program.cs`.

See **[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)** for the full guide: run modes, environment variables, test suite, and configuration reference.

## Immediate next steps

1. Add an `IFhirBackend` implementation (e.g., Firely SDK or raw `HttpClient`) and register it in `Program.cs`.
2. Add durable session persistence instead of in-memory session storage.
3. Replace `KeywordIntentRouter` with an LLM router agent plus deterministic fallback.
4. Add structured evidence items instead of citation extraction from answer text.
5. Move export and reconciliation flows onto background responses or durable orchestration.

## Notes

- Package versions are pinned centrally in `Directory.Packages.props`.
- Microsoft Agent Framework packages are still prerelease; isolate framework-specific code behind the runner boundary before production rollout.
