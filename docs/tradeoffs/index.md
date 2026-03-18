# FPF trade-off studies for `venikman/fhir-agents`

## Direct answer
Do the trade-off studies at the repo's existing decision seams, and keep the short decision in `DECISIONS.md` while storing the full FPF study as a separate doc under `docs/tradeoffs/`.

Suggested structure:
- `DECISIONS.md` = one-line decision log
- `docs/tradeoffs/index.md` = backlog + status
- `docs/tradeoffs/NN-<seam>.md` = one FPF trade-off study per seam

## Why these seams
The repo already exposes a clear architecture:
- Router -> Specialist Agent -> Explainability -> FHIR API tools
- Elysia HTTP/WebSocket server
- Bun + SQLite checkpoint memory
- Langfuse/OpenTelemetry observability
- Resource-specific FHIR tools

These are the places where changing one design choice changes behavior, cost, reliability, or safety.

## FPF pattern stack to use
Use this minimum stack for every study:
1. Problem Framing: Context, ScopeSlice(G), Γ_time, TaskSignature, AcceptanceClauses, KillClauses
2. G.0 / CG-Spec: comparability frame
3. G.3: characteristics and scales
4. G.4: acceptance and evidence
5. G.5: selection among alternatives without collapsing to a single hidden scalar too early
6. A.2.6: ClaimScope / WorkScope
7. B.3: F-G-R assurance notes
8. F.17: publish a short UTS row for the chosen option

## Backlog of trade-off studies

### 01. Agent partitioning
**Current baseline:** 6 specialized agents + router.
**Alternatives:**
- monolithic agent
- 6 specialized agents + router
- 3 coarse agents + router

**Why here:** this is the highest-leverage architecture choice because it changes hallucination rate, tool choice quality, latency, and maintenance cost.

**Primary characteristics:**
- task success rate
- wrong-tool-call rate
- median latency
- token cost
- prompt maintenance effort
- explainability quality

**Repo seam:** `src/agents/definitions.ts`, `src/agents/router.ts`, `DECISIONS.md`

**First slice:** compare current 6-agent setup against a monolith on 30 representative queries split across lookup/search/analytics/clinical/cohort/export.

### 02. Router strategy
**Current baseline:** LLM intent classifier with fallback to `clinical`.
**Alternatives:**
- LLM classifier
- rules/regex classifier
- hybrid rules-first then LLM
- confidence-threshold LLM with explicit clarification path

**Primary characteristics:**
- routing accuracy
- ambiguous-query recovery
- fallback frequency
- added latency
- cost per query
- failure containment

**Repo seam:** `src/agents/router.ts`, `docs/http/router.http`, `DECISIONS.md`

**First slice:** 100 labeled queries, including deliberately ambiguous boundary cases between search/cohort and analytics/clinical.

### 03. Tool granularity and tool output format
**Current baseline:** one tool per FHIR resource type, resource-specific Zod schemas, structured text summaries instead of raw JSON.
**Alternatives:**
- one generic search tool + raw JSON
- one generic search tool + structured summaries
- resource-specific tools + raw JSON
- resource-specific tools + structured summaries

**Primary characteristics:**
- parameter error rate
- tool call count
- answer correctness
- context-window load
- implementation churn when adding a resource

**Repo seam:** `src/tools/*.ts`, especially `fhir.ts`, `fhir-patient-search.ts`, and the resource-specific search tools

**First slice:** evaluate 20 parameter-heavy queries across Patient, Encounter, Condition, Observation, MedicationRequest.

### 04. Explainability and confidence method
**Current baseline:** deterministic post-processing for citations/reasoning/confidence; heuristic confidence based on citations and tool errors.
**Alternatives:**
- deterministic extraction + heuristic confidence
- LLM structured extraction + heuristic confidence
- deterministic extraction + model-scored confidence
- deterministic extraction + evidence-weighted confidence

**Primary characteristics:**
- citation recall/precision
- confidence calibration
- extra latency
- extra token cost
- determinism/reproducibility

**Repo seam:** `src/explainability.ts`, `DECISIONS.md`

**First slice:** replay 50 saved traces and score explanation completeness and confidence calibration against human labels.

### 05. Memory backend
**Current baseline:** custom `BunSqliteSaver` on `bun:sqlite` with WAL.
**Alternatives:**
- `BunSqliteSaver`
- `MemorySaver`
- Postgres-backed checkpointer
- upstream SQLite adapter once Bun compatibility is stable

**Primary characteristics:**
- crash recovery
- write/read latency
- data durability
- deploy complexity
- operational portability

**Repo seam:** `src/checkpointer.ts`, `copilot-core.ts`, `DECISIONS.md`

**First slice:** benchmark multi-turn sessions with restart recovery and concurrent thread access.

### 06. Transport and server shape
**Current baseline:** Elysia + HTTP POST + WebSocket streaming.
**Alternatives:**
- Elysia + WebSocket
- Elysia + SSE
- Hono/Express + SSE
- Bun native server with custom protocol

**Primary characteristics:**
- streaming smoothness
- cancellation capability
- client simplicity
- infra compatibility
- typed-contract strength

**Repo seam:** `src/server.ts`, `docs/COPILOT.md`, `DECISIONS.md`

**First slice:** compare WebSocket vs SSE for streaming UX, cancellation, reconnect behavior, and proxy compatibility.

### 07. Observability and PHI-safe tracing
**Current baseline:** Langfuse + OpenTelemetry with content redaction default.
**Alternatives:**
- Langfuse + OTel
- LangSmith
- generic OTEL backend + custom dashboards
- reduced tracing in production

**Primary characteristics:**
- debugging usefulness
- PHI leakage risk
- trace completeness
- deployment overhead
- cost of ownership

**Repo seam:** `src/otel.ts`, `docs/OBSERVABILITY.md`, `.env.example`, `DECISIONS.md`

**First slice:** compare redacted vs content-capturing traces in staging only, with a structured privacy review.

### 08. Runtime / provider / deployment bundle
**Current baseline:** Bun + Gemini 3.1-flash-lite + Railway + remote Cloudflare Workers FHIR backend.
**Alternatives:**
- Bun vs Node
- Gemini lite vs stronger model tier
- Railway vs Fly.io
- remote test backend vs local sandbox

**Primary characteristics:**
- p95 latency
- cost/query
- deploy reliability
- local dev friction
- offline testability

**Repo seam:** `package.json`, `LOCAL_SETUP.md`, `.env.example`, `DECISIONS.md`

**First slice:** one constrained benchmark matrix: {Bun, Node} x {current model, stronger model} on the same labeled query set.

## Recommended order
1. Agent partitioning
2. Router strategy
3. Tool granularity + output format
4. Explainability + confidence
5. Transport/server
6. Memory backend
7. Observability/privacy
8. Runtime/provider/deployment

## Standard study template

```md
# Trade-off Study: <name>

## Context
- BoundedContext: `fhir-agents/<seam>`
- DescribedEntity: `<component or boundary>`
- Γ_time: `<time window / build / release>`

## Problem
- Kernel form: No known `U.Method` choice satisfies the current `TaskSignature` under this `ScopeSlice(G)` with acceptable cost/risk.

## TaskSignature
- ObjectiveProfile:
- Constraints:
- ScopeSlice(G):
- Freshness:
- DataShape / Scale / Missingness:

## Options
- O1:
- O2:
- O3:

## Characteristics (G.3)
- C1:
- C2:
- C3:
- C4:

## AcceptanceClauses (G.4)
- A1:
- A2:
- A3:

## KillClauses
- K1:
- K2:

## Evidence
- Benchmark corpus:
- Trace set:
- Reproduction steps:

## Comparison result
- Pareto set:
- Dominated options:
- Recommendation:

## Decision hook
- Link to `DECISIONS.md` row:
- Follow-up experiment:
```

## Where to physically put them
- Keep the one-line outcome in `DECISIONS.md`.
- Put the actual study in `docs/tradeoffs/NN-name.md`.
- Put replay/benchmark fixtures under `docs/http/` or a new `scripts/bench/` folder.
- Put result summaries in `docs/tradeoffs/index.md`.

## Minimal governance rule
No decision row should be added to `DECISIONS.md` unless there is either:
- a linked trade-off study, or
- an explicit note saying the decision is provisional and awaiting study.
