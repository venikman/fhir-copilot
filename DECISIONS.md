# DECISIONS

| ID | Date | Status | Decision | Why | Linked study |
|---|---|---|---|---|---|
| D-0001 | 2026-03-17 | provisional | Start with the existing six-agent topology plus deterministic router fallback. | It preserves the current `fhir-agents` mental model while the workflow-first cutover is still being evaluated. | `docs/tradeoffs/01-agent-partitioning.md` |
| D-0002 | 2026-03-17 | accepted | Externalize runtime agent profiles and routing hints into files. | Prompt/config diffs stay reviewable and can be tested independently of orchestration code. | `docs/tradeoffs/02-router-strategy.md` |
| D-0003 | 2026-03-17 | accepted | Ship a stub FHIR backend for first boot and defer Firely integration to the next slice. | This keeps the starter runnable without external infrastructure while preserving the tool contracts. | `docs/ARCHITECTURE.md` |
| D-0004 | 2026-03-17 | accepted | Wire direct OpenAI provider first for Agent Framework startup. | Official docs show direct OpenAI support with `OpenAIClient`, `AsAIAgent`, function tools, sessions, and streaming; it is the smallest first integration surface. | `docs/ARCHITECTURE.md` |
