# Getting Started

## Prerequisites

| Requirement | Version | Check |
|-------------|---------|-------|
| .NET SDK | 10.0 preview | `dotnet --version` |
| Git | any | `git --version` |
| LM Studio *(Local mode only)* | any | running on `localhost:1234` |
| Gemini API key *(Gemini mode only)* | — | [aistudio.google.com](https://aistudio.google.com) |

## Ways to run

| # | Mode | External services needed | Best for |
|---|------|------------------------|----------|
| 1 | [Local LLM](#1-local-llm-mode) | LM Studio | Offline dev, fast iteration |
| 2 | [Gemini](#2-gemini-mode) | Gemini API | Real agent behavior |
| 3 | [Aspire](#3-aspire-dashboard) | varies | OTEL traces, metrics, logs |

All modes listen on **http://localhost:5075**.

---

### 1. Local LLM mode

Uses an OpenAI-compatible local server (LM Studio, Ollama, etc.) for real LLM inference without cloud API keys.

**Prerequisites:**
1. LM Studio running with the local server enabled on port 1234 (its default)
2. A model loaded — the default is `zai-org/glm-4.7-flash`, but you can use whatever you have loaded

**Option A: .env file** (recommended)

Create `.env` at the **repo root** (the app walks up the directory tree to find it):

```env
Provider__Mode=Local
Provider__LocalEndpoint=http://localhost:1234/v1
Provider__LocalModel=zai-org/glm-4.7-flash
```

Then start the server:

```bash
cd src/FhirCopilot.Api
dotnet run
```

**Option B: environment variables** (no file edits)

```bash
Provider__Mode=Local \
Provider__LocalEndpoint=http://localhost:1234/v1 \
Provider__LocalModel=zai-org/glm-4.7-flash \
dotnet run --project src/FhirCopilot.Api
```

**Option C: edit appsettings.json directly**

```json
{
  "Provider": {
    "Mode": "Local",
    "LocalEndpoint": "http://localhost:1234/v1",
    "LocalModel": "zai-org/glm-4.7-flash"
  }
}
```

### 2. Gemini mode

Uses the Google Gemini API via the native `Google_GenerativeAI` SDK. Requires an API key.

Create `.env` at the repo root:

```env
Provider__Mode=Gemini
GEMINI_API_KEY=your-key-here
```

The full fallback chain is pre-configured in `appsettings.json` — on HTTP 429 (rate limit), the runner automatically tries the next model:

```
gemini-3-flash-preview → gemini-3.1-flash-lite-preview → gemini-3.1-pro-preview → gemini-2.5-flash → gemini-2.5-pro → gemini-2.0-flash
```

Override individual models via env vars:

```env
Provider__GeminiModels__0=gemini-3-flash-preview
Provider__GeminiModels__1=gemini-3.1-flash-lite-preview
# ... etc
```

Start:

```bash
cd src/FhirCopilot.Api
dotnet run
```

### 3. Aspire dashboard

Runs the app through .NET Aspire for OTEL traces, metrics, and structured logs in a local dashboard.

```bash
dotnet run --project src/FhirCopilot.AppHost
```

The Aspire dashboard opens automatically. The API project gets the same `.env` configuration — Aspire just adds the dev dashboard on top.

To send traces to an external OTEL collector (e.g., Arize Phoenix):

```env
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:6006/v1
```

---

## FHIR backend

The demo uses `SampleFhirBackend` — 3 hardcoded patients with deterministic data. No FHIR server is required.

To connect to a real FHIR R4 server, implement the `IFhirBackend` interface and register it in `Program.cs`.

---

## Running tests

```bash
# From repo root
dotnet test

# Verbose
dotnet test --logger "console;verbosity=detailed"

# Single test class
dotnet test --filter "FullyQualifiedName~RouterPropertyTests"
```

**Test suite** (5 test files, 19 tests):

| File | Covers |
|------|--------|
| `RouterPropertyTests` | FsCheck property-based router invariants |
| `ModelFallbackTests` | 429 retry with next model in chain |
| `TraceConsoleFormatterTests` | Trace-enriched log formatting |
| `ErrorHandlingTests` | HubException error propagation via SignalR |
| `SignalRHubTests` | WebSocket hub integration (requires LM Studio) |

Unit tests run without external dependencies. `SignalRHubTests` auto-skips when LM Studio is unavailable — when running, it tests real LLM queries over SignalR.

---

## Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/` | GET | Welcome text |
| `/health` | GET | Health check (returns `Healthy`) |
| `/alive` | GET | Liveness probe |
| `/hubs/copilot` | — | SignalR WebSocket hub |

### Quick smoke test

```bash
# Health
curl http://localhost:5075/health

# Welcome
curl http://localhost:5075/
```

The copilot is accessible via SignalR at `/hubs/copilot`. Use a SignalR client to invoke:
- `SendQuery(CopilotRequest)` — one-shot request/response
- `StreamQuery(CopilotRequest)` — streaming via `IAsyncEnumerable<CopilotStreamEvent>`

---

## Environment variable reference

| Variable | Default | Required | Description |
|----------|---------|----------|-------------|
| `Provider__Mode` | `Gemini` | no | `Gemini` or `Local` |
| `GEMINI_API_KEY` | — | Gemini mode | Google AI Studio API key |
| `Provider__GeminiModels__N` | *(see appsettings.json)* | no | Fallback chain (0-indexed) |
| `Provider__LocalEndpoint` | `http://localhost:1234/v1` | no | OpenAI-compatible API URL |
| `Provider__LocalModel` | `zai-org/glm-4.7-flash` | no | Model name for local server |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | — | no | OTLP collector endpoint |
| `OTEL_SERVICE_NAME` | — | no | Service name for traces |
| `ASPNETCORE_ENVIRONMENT` | `Development` (via launchSettings.json) | no | Set by `launchSettings.json` for `dotnet run` |

---

## Configuration precedence

The app merges configuration in this order (last wins):

1. `appsettings.json` — checked into repo, shared defaults
2. `.env` file — loaded by `Program.cs` at startup (walks up directory tree)
3. Environment variables — highest priority, good for CI and containers

The `.env` loader in `Program.cs` reads `KEY=VALUE` lines, skips comments (`#`) and blank lines, and sets them as environment variables before the host builds.

---

## Project structure

```
.
├── src/
│   ├── FhirCopilot.Api/          # Main API project
│   │   ├── Contracts/             # Request/response records
│   │   ├── Fhir/                  # IFhirBackend + implementations
│   │   ├── Hubs/                  # SignalR WebSocket hub
│   │   ├── Models/                # Domain records (Patient, Condition, etc.)
│   │   ├── Options/               # ProviderOptions, RuntimeOptions
│   │   ├── Services/              # Runners, router, copilot service
│   │   ├── config/agents/         # JSON agent profiles
│   │   ├── appsettings.json
│   │   └── Program.cs
│   ├── FhirCopilot.AppHost/      # Aspire dev dashboard host
│   └── FhirCopilot.ServiceDefaults/ # OTEL, health checks, resilience
├── tests/
│   └── FhirCopilot.Api.Tests/    # xUnit + FsCheck + Verify.Xunit
├── docs/
│   ├── ARCHITECTURE.md
│   ├── DECISIONS.md
│   ├── GETTING_STARTED.md         # ← you are here
│   ├── PROD_READINESS.md
│   ├── compliance/
│   ├── tradeoffs/
│   └── tutorials/
├── Directory.Build.props          # net10.0, C# preview
├── Directory.Packages.props       # Centralized package versions
├── Dockerfile
├── fly.toml
└── .env.example
```
