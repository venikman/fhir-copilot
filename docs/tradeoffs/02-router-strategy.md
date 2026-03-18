# Trade-off Study: router strategy

## Context
- BoundedContext: `fhir-copilot/router`
- DescribedEntity: query classification into specialist behavior
- Γ_time: starter bundle, first runnable cut

## Problem
- Kernel form: the rebuild needs a router now, but the evidence budget is not yet high enough to justify an LLM-only classifier as the first implementation.

## TaskSignature
- ObjectiveProfile: route obvious queries reliably while keeping startup friction low
- Constraints: no model key required for first boot; agent behavior must remain configurable
- ScopeSlice(G): intent routing only
- Freshness: current starter architecture
- DataShape / Scale / Missingness: small keyword hints now, labeled routing set later

## Options
- O1: deterministic keyword router
- O2: LLM classifier
- O3: rules-first then LLM fallback

## Characteristics (G.3)
- C1: boot-time simplicity
- C2: routing accuracy on ambiguous healthcare queries
- C3: cost per query
- C4: failure containment

## AcceptanceClauses (G.4)
- A1: the router must work in stub mode with no model credentials
- A2: fallback agent must be explicit and configurable
- A3: swap to hybrid routing must not change external contracts

## KillClauses
- K1: hard-coded routing logic with no external hints
- K2: routing failures terminate the request instead of degrading to a safe fallback

## Evidence
- Benchmark corpus: not yet collected
- Trace set: not yet collected
- Reproduction steps: add 100 labeled prompts after real provider + FHIR backend are wired

## Comparison result
- Pareto set: O1, O3
- Dominated options: none yet
- Recommendation: start with O1, keep the config surface, then move to O3 after labeled routing data exists.

## Decision hook
- Link to `DECISIONS.md` row: D-0002
- Follow-up experiment: build a labeled route corpus and compare O1 vs O3 on ambiguous boundary cases.
