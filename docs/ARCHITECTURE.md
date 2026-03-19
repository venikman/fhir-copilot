# Starter architecture

## Current control flow

```text
SignalR request (/hubs/copilot)
  -> KeywordIntentRouter
  -> Agent profile lookup (config file)
  -> IAgentRunner (selected at startup via DI)
      -> GeminiAgentFrameworkRunner (Mode=Gemini + GEMINI_API_KEY)
      -> OpenAiCompatibleAgentRunner (Mode=Local)
      -> InvalidOperationException (otherwise — fail fast)
  -> Response envelope
```

## Why this shape

The original repository architecture is:

```text
Router -> Specialist Agent -> Explainability -> FHIR tools
```

This starter preserves that outer shape, but moves runtime agent definitions into file-backed seams (`config/agents/*.json`).

## Deliberate starter simplifications

- The router is deterministic, not LLM-based.
- Session persistence is in-memory, not durable.
- Tool outputs come from `SampleFhirBackend` (hardcoded demo data). A production FHIR R4 client can be added by implementing `IFhirBackend`.
- Export is simulated.
- Tool-call traces are not yet surfaced from Agent Framework updates into the SignalR stream.

## Cutover plan

### Replace the backend
Add a production `IFhirBackend` implementation (e.g., Firely SDK or raw `HttpClient`). Register it in `Program.cs` instead of `SampleFhirBackend`. Keep the `FhirToolbox` signatures stable.

### Replace the router
Add a router agent that emits one of the six agent types, but preserve deterministic fallback for ambiguous or failed classifications.

### Add evidence-first responses
Today the real-agent path extracts citations from the answer text. Replace that with explicit `EvidenceItem` results emitted by each tool or deterministic executor.

### Add durable session + export jobs
Persist `(threadId, agent)` sessions and move long-running exports to background responses or durable orchestration.
