# Trade-off Study: agent partitioning

## Context
- BoundedContext: `fhir-copilot/agent-partitioning`
- DescribedEntity: runtime topology for routing a query to specialist behavior
- Γ_time: starter bundle, first runnable cut

## Problem
- Kernel form: no single agent partitioning is yet accepted for the C# rebuild under the current time and validation budget.

## TaskSignature
- ObjectiveProfile: ship a runnable starter while preserving a low-regret path to workflow-first orchestration
- Constraints: first boot must work without a live FHIR server; config should stay reviewable; starter should preserve old query classes
- ScopeSlice(G): lookup, search, analytics, clinical, cohort, export
- Freshness: current architecture decision
- DataShape / Scale / Missingness: small stub data now, real ATR/FHIR server later

## Options
- O1: one monolithic agent
- O2: six specialist agents + router
- O3: three coarse agents + router

## Characteristics (G.3)
- C1: parity with the existing repo mental model
- C2: prompt maintenance effort
- C3: wrong-tool-call containment
- C4: ease of later migration into workflows

## AcceptanceClauses (G.4)
- A1: starter must preserve the current six user-facing query classes
- A2: configs must remain external to the service code
- A3: future workflow cutover must not require rewriting the HTTP API

## KillClauses
- K1: architecture hides agent behavior inside one giant hard-coded prompt
- K2: architecture prevents later deterministic execution for cohort or export flows

## Evidence
- Benchmark corpus: not yet run
- Trace set: not yet collected
- Reproduction steps: use `docs/http/copilot.http` once real provider + FHIR backend are wired

## Comparison result
- Pareto set: O2, O3
- Dominated options: O1
- Recommendation: start with O2 in the starter, but keep the cutover seam explicit for an O3 or workflow-first redesign.

## Decision hook
- Link to `../DECISIONS.md` row: D-0001
- Follow-up experiment: compare 30 labeled queries across O2 and O3 after Firely integration.
