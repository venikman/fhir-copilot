# Orchestration policy for this repository

## General rules

1. For any nontrivial task, derive scope and plan before editing.
2. If the repo has an existing test suite and the task changes executable code, run the tests first.
3. For new behavior or bug fixes in code, prefer red/green TDD.
4. For UI, API, or other manual flows, include execution evidence or manual test notes before claiming done.
5. Keep patches reviewable; prefer several small changes over one large scaffold dump.
6. Skip ceremony for trivial tasks.

## Repo-local defaults

- Runtime: ASP.NET Core minimal API on .NET 10.
- Agent SDK: Microsoft Agent Framework with Gemini provider via Google GenerativeAI native SDK.
- Runtime agent prompts/config live in `src/FhirCopilot.Api/config/agents/*.json`.
- FHIR integration starts with `SampleFhirBackend`; replace with a Firely-based client before production use.
- Preserve the response envelope fields: `answer`, `citations`, `reasoning`, `toolsUsed`, `agentUsed`, `confidence`, `threadId`.
- Prefer deterministic routing and deterministic cohort math over model-only reasoning for healthcare answers.
- Keep the multi-agent topology externally configurable; do not bury prompt policy in service code.
