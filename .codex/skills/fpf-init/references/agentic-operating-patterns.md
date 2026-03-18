# Agentic operating patterns for FPF repos

Use these prompt snippets when the repo contains executable code or when you need stronger evidence.

## First run the tests
- `First run the tests.`
- `Run the existing test suite and summarize failures before editing code.`

## Red/green TDD
- `Use red/green TDD.`
- `Write the failing test first, confirm it fails, then implement the smallest change that makes it pass.`

## Manual test evidence
- `Exercise the new path and report the exact commands, inputs, outputs, and edge cases tried.`
- `For UI or API changes, include manual test notes before claiming done.`

## Linear walkthrough
- `Create a short linear walkthrough of the changed files and explain how control flows through them.`
- `Explain the new agent topology file by file, with one paragraph per file.`

## Patch sizing
- Prefer several small reviewable changes over one large scaffold dump.
- If conflicts with existing local policy are high, produce a merge plan before rewriting.
