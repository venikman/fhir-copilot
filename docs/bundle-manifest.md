# Bundle manifest

## Imported from `fpf-skills-canonical.zip`

- `.codex/config.toml`
- `.codex/agents/*.toml`
- `.codex/skills/fpf-init/SKILL.md`
- `.codex/skills/fpf-init/agents/openai.yaml`
- `.codex/skills/fpf-init/references/*`
- the base FPF orchestration policy, merged into root `AGENTS.md`

## Imported / derived from `fhir-agents-fpf-tradeoff-studies.md`

- `docs/tradeoffs/index.md`
- `docs/tradeoffs/template.md`
- `docs/tradeoffs/01-agent-partitioning.md`
- `docs/tradeoffs/02-router-strategy.md`
- initial rows in `DECISIONS.md`

## Created for this starter

- `src/FhirCopilot.Api/*`
- externalized runtime agent profiles under `src/FhirCopilot.Api/config/agents/*.json`
- stub FHIR backend with sample attributed-population data
- OpenAI-backed Microsoft Agent Framework runner boundary
- `docs/ARCHITECTURE.md`
- `docs/http/copilot.http`
- `.env.example`
