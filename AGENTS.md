# FPF orchestration policy for this repository

The root session is the dispatcher.

For any nontrivial task in this repo:
1. Derive `TaskSignature` and bounded contexts first.
2. Spawn in parallel:
   - `problem_typist`
   - `context_cartographer`
   - `kernel_auditor`
   - `evidence_auditor`
   - `method_scout`
3. Wait for all results.
4. Merge conflicts, state assumptions, and choose a plan.
5. Delegate file edits only to `publication_curator`.
6. Keep all other subagents read-only.
7. Skip subagents for trivial tasks.
8. If the repo has an existing test suite and the task changes executable code, first run the tests.
9. For new behavior or bug fixes in code, prefer red/green TDD.
10. For UI, API, or other manual flows, include execution evidence or manual test notes before claiming done.
11. Keep patches reviewable; prefer several small changes over one large scaffold dump.

## Repo-local defaults

- Runtime: ASP.NET Core minimal API on .NET 10.
- Agent SDK: Microsoft Agent Framework with direct OpenAI provider wired first.
- Runtime agent prompts/config live in `src/FhirCopilot.Api/config/agents/*.json`.
- FHIR integration starts with `SampleFhirBackend`; replace with a Firely-based client before production use.
- Preserve the response envelope fields: `answer`, `citations`, `reasoning`, `toolsUsed`, `agentUsed`, `confidence`, `threadId`.
- Prefer deterministic routing and deterministic cohort math over model-only reasoning for healthcare answers.
- Keep the multi-agent topology externally configurable; do not bury prompt policy in service code.
