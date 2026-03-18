# FHIR Copilot Agent Framework Starter

A C# / ASP.NET Core starter for rebuilding `venikman/fhir-agents` on Microsoft Agent Framework with Gemini.

It is intentionally **thin but runnable**:

- a minimal HTTP API with health, sync query, and SSE streaming endpoints
- externalized runtime agent profiles in `src/FhirCopilot.Api/config/agents/*.json`
- a deterministic keyword router for first boot
- a Gemini-backed Microsoft Agent Framework runner that activates when `GEMINI_API_KEY` is set
- a stub runner plus sample FHIR-like data so the project can boot before any model or real FHIR server is wired

## What this starter optimizes for

1. **Fast first boot.** The API can run in stub mode without external services.
2. **Config-first agent evolution.** Prompts, allowed tools, and API preference live in files, not code.
3. **Low-regret cutover path.** Replace the sample FHIR backend with a Firely-based client later, and replace the deterministic router with a router agent later.
4. **Evidence-first answers.** The response envelope keeps citations, reasoning, tools used, agent used, confidence, and thread id.

## Repo map

```text
.
├── AGENTS.md                       # repo-wide orchestration policy
├── docs/
│   ├── ARCHITECTURE.md
│   ├── DECISIONS.md                # short decision log
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

## First boot

```bash
# Stub mode (no API key needed)
dotnet run --project src/FhirCopilot.Api

# Dev with Aspire dashboard (traces, metrics, logs)
dotnet run --project src/FhirCopilot.AppHost

# Run tests
dotnet test
```

Health check:

```bash
curl http://localhost:5075/health
```

Sync query:

```bash
curl -X POST http://localhost:5075/api/copilot \
  -H "Content-Type: application/json" \
  -d '{"query":"How many diabetic patients do we have?"}'
```

Streaming query:

```bash
curl -N "http://localhost:5075/api/copilot/stream?query=Clinical%20summary%20for%20patient-0001"
```

## Enable real Agent Framework calls

Create a `.env` file at the repo root (see `.env.example`):

```env
Provider__Mode=Gemini
GEMINI_API_KEY=your-key-here
Provider__GeminiModel=gemini-3-flash-preview
```

The runner uses the Google GenerativeAI native SDK with Microsoft Agent Framework's `AIAgent` abstraction, function tools from `FhirToolbox`, and `AgentSession` per `(threadId, agent)` pair.

## Immediate next steps

1. Replace `SampleFhirBackend` with a Firely R4 client and capability registry.
2. Add durable session persistence instead of in-memory session storage.
3. Replace `KeywordIntentRouter` with an LLM router agent plus deterministic fallback.
4. Add structured evidence items instead of citation extraction from answer text.
5. Move export and reconciliation flows onto background responses or durable orchestration.

## Notes

- Package versions are pinned centrally in `Directory.Packages.props`.
- Microsoft Agent Framework packages are still prerelease; isolate framework-specific code behind the runner boundary before production rollout.
