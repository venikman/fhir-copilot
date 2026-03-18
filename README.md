# FHIR Copilot Agent Framework Starter

This bundle is a starter repository for rebuilding `venikman/fhir-agents` as a C# / ASP.NET Core project on Microsoft Agent Framework.

It is intentionally **thin but runnable**:

- a minimal HTTP API with health, sync query, and SSE streaming endpoints
- externalized runtime agent profiles in `src/FhirCopilot.Api/config/agents/*.json`
- a deterministic keyword router for first boot
- an OpenAI-backed Microsoft Agent Framework runner that activates when `OPENAI_API_KEY` is set
- a stub runner plus sample FHIR-like data so the project can boot before any model or real FHIR server is wired
- imported FPF workspace config under `.codex/`
- imported tradeoff backlog and decision scaffolding under `docs/tradeoffs/`

## What this starter optimizes for

1. **Fast first boot.** The API can run in stub mode without external services.
2. **Config-first agent evolution.** Prompts, allowed tools, and API preference live in files, not code.
3. **Low-regret cutover path.** Replace the sample FHIR backend with a Firely-based client later, and replace the deterministic router with a router agent later.
4. **Evidence-first answers.** The response envelope keeps citations, reasoning, tools used, agent used, confidence, and thread id.

## Repo map

```text
.
├── .codex/                         # imported FPF workspace agent config
├── AGENTS.md                       # repo-wide orchestration policy
├── DECISIONS.md                    # short decision log
├── bundle-manifest.md              # what was imported vs created
├── docs/
│   ├── ARCHITECTURE.md
│   ├── http/copilot.http
│   └── tradeoffs/
├── src/
│   └── FhirCopilot.Api/
│       ├── Contracts/
│       ├── Fhir/
│       ├── Models/
│       ├── Options/
│       ├── Services/
│       ├── config/agents/
│       ├── appsettings.json
│       └── Program.cs
└── .env.example
```

## First boot

```bash
dotnet restore src/FhirCopilot.Api/FhirCopilot.Api.csproj
dotnet run --project src/FhirCopilot.Api/FhirCopilot.Api.csproj
```

Default mode is `Stub`, so no model key is required.

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

1. Set `Provider__Mode=OpenAI`
2. Set `OPENAI_API_KEY`
3. Optionally change `Provider__OpenAIChatModel` and `Provider__OpenAIResponsesModel`

Example:

```bash
export Provider__Mode=OpenAI
export OPENAI_API_KEY=sk-...
dotnet run --project src/FhirCopilot.Api/FhirCopilot.Api.csproj
```

The starter uses:
- `ChatCompletion` agents for lookup/search/analytics/clinical/cohort
- `Responses` agent for export
- function tools created from `FhirToolbox` methods
- `AgentSession` per `(threadId, agent)` pair

## Immediate next steps

1. Replace `SampleFhirBackend` with a Firely R4 client and capability registry.
2. Add durable session persistence instead of in-memory session storage.
3. Replace `KeywordIntentRouter` with an LLM router agent plus deterministic fallback.
4. Add structured evidence items instead of citation extraction from answer text.
5. Move export and reconciliation flows onto background responses or durable orchestration.

## Notes

- Package versions are pinned centrally in `Directory.Packages.props`.
- Microsoft Agent Framework packages are still prerelease; isolate framework-specific code behind the runner boundary before production rollout.
- The starter keeps the old six-agent mental model because it is the fastest path to a usable parity slice. The tradeoff backlog already captures where to collapse that into workflow-first orchestration later.
