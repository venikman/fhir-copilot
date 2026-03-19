# StubAgentRunner: A Lightweight Test Double

The `StubAgentRunner` is a minimal implementation of `IAgentRunner` used in two
situations:

1. **Integration tests** -- the test fixture overrides the DI-registered runner
   with `StubAgentRunner` so snapshot tests run deterministically without a real
   LLM provider.

2. **Development without credentials** -- when no Gemini API key or local LLM
   endpoint is available, the stub returns a clear message explaining the
   configuration gap.

---

## How It Works

The stub does not query FHIR data or perform domain logic. It returns a fixed
response template that includes the routed agent name and the original query:

```csharp
// From src/FhirCopilot.Api/Services/StubAgentRunner.cs

private static string BuildAnswer(string agentName, string query) =>
    $"[{agentName} stub] No LLM provider is configured. Query received: \"{query}\". " +
    "Configure a Gemini API key or local LLM endpoint to get a real answer.";
```

The response always sets `IsStub: true` and `Confidence: "stub"` so callers can
distinguish stub output from real LLM output.

---

## Test Fixture

The test fixture in `CopilotFixture.cs` forces the stub path by overriding the
DI registration:

```csharp
// From tests/FhirCopilot.Api.Tests/CopilotFixture.cs

builder.ConfigureServices(services =>
{
    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentRunner));
    if (descriptor != null) services.Remove(descriptor);
    services.AddSingleton<IAgentRunner, StubAgentRunner>();
});
```

This guarantees that CI runs test the routing and HTTP pipeline without calling
an external LLM service.

---

## Snapshot Tests

Each agent type has a snapshot test in `ResponseSnapshotTests.cs`. The snapshots
verify the full JSON response structure, ensuring the routing, serialization, and
pipeline remain stable:

```csharp
// From tests/FhirCopilot.Api.Tests/ResponseSnapshotTests.cs

[Fact]
public async Task Cohort_care_gap()
{
    var body = await PostAndReadAsync("Which diabetic patients are without metformin?", "snap-cohort");
    await Verifier.Verify(body).ScrubMember("threadId");
}
```

---

## Real Domain Logic Lives in the LLM Runners

The actual clinical logic -- set-difference cohort analysis, multi-resource
fan-out, ICD-10 code filtering -- is performed by the LLM-backed runners
(`GeminiAgentFrameworkRunner`, `OpenAiCompatibleAgentRunner`) using FHIR tools
registered in `ToolRegistry`. The stub intentionally avoids duplicating that
logic.

---

**Relevant source files:**

- `src/FhirCopilot.Api/Services/StubAgentRunner.cs` -- the stub runner
- `src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs` -- the Gemini LLM runner
- `src/FhirCopilot.Api/Services/OpenAiCompatibleAgentRunner.cs` -- the local LLM runner
- `src/FhirCopilot.Api/Services/CopilotService.cs` -- the orchestrator that delegates to the active runner
- `tests/FhirCopilot.Api.Tests/ResponseSnapshotTests.cs` -- snapshot tests covering all agent types
- `tests/FhirCopilot.Api.Tests/CopilotFixture.cs` -- test fixture forcing the stub path
