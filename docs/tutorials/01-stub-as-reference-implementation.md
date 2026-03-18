# The Stub That Isn't: Using a Deterministic Runner as a Reference Implementation

Most AI agent starter kits ship with stubs that look like this:

```csharp
public Task<string> RunAsync(string query)
{
    return Task.FromResult("TODO: implement agent logic");
}
```

That tells a developer nothing about what the agent is *supposed to do*. The stub
in this codebase takes a radically different approach. `StubAgentRunner` is not a
placeholder -- it is a fully functional, deterministic executor that performs the
exact clinical logic an LLM-backed agent is expected to replicate. It computes
set differences, fans out parallel FHIR queries, filters by ICD-10 codes, and
assembles structured clinical summaries complete with citations.

This tutorial explains why that design is powerful and how you can apply the same
pattern in your own agent projects.

---

## 1. The Pattern: A Stub That Does Real Work

`StubAgentRunner` handles six agent types, and each one executes genuine
domain logic against the FHIR backend. There are no hardcoded strings. Every
answer is computed from data.

### Cohort Analysis: Set-Difference Logic

The `ExecuteCohortAsync` method answers questions like "Which diabetic patients
are not on metformin?" It does this with the same algorithm a production system
would use -- build two sets, compute the difference:

```csharp
// From src/FhirCopilot.Api/Services/StubAgentRunner.cs

private async Task<StubExecutionPlan> ExecuteCohortAsync(string query, CancellationToken ct)
{
    var diabeticIds = await GetDiabeticPatientIdsAsync(ct);

    var medications = await _backend.GetMedicationsAsync(ct);
    var metforminIds = medications
        .Where(medication => medication.Display.Contains("metformin", StringComparison.OrdinalIgnoreCase))
        .Select(medication => medication.PatientId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var allPatients = await _backend.GetPatientsAsync(ct);
    var gapPatients = allPatients
        .Where(patient => diabeticIds.Contains(patient.Id) && !metforminIds.Contains(patient.Id))
        .ToList();

    // ...
}
```

This is not mock behavior. It is the exact set-difference operation that the LLM
agent needs to arrive at. A developer reading this stub immediately understands:
*the cohort agent should identify patients who appear in one clinical set but not
another.*

### Clinical Summary: Parallel Multi-Resource Fan-Out

The `ExecuteClinicalAsync` method demonstrates how a clinical summary should be
assembled -- by querying five FHIR resource types concurrently:

```csharp
// From src/FhirCopilot.Api/Services/StubAgentRunner.cs

var conditionsTask = _backend.SearchConditionsAsync(patient.Id, null, null, null, ct);
var medicationsTask = _backend.SearchMedicationsAsync(patient.Id, null, null, ct);
var observationsTask = _backend.SearchObservationsAsync(patient.Id, null, null, null, null, ct);
var encountersTask = _backend.SearchEncountersAsync(patient.Id, null, null, null, null, null, null, null, ct);
var allergiesTask = _backend.SearchAllergiesAsync(patient.Id, ct);
await Task.WhenAll(conditionsTask, medicationsTask, observationsTask, encountersTask, allergiesTask);
```

This `Task.WhenAll` pattern is the reference for what the LLM agent's tool calls
should look like. When an LLM-backed agent is given tools like
`search_conditions`, `search_medications`, and `search_observations`, it should
invoke them in parallel -- exactly as the stub does. The stub makes the expected
tool-calling strategy explicit.

### Diabetic Cohort Identification: ICD-10 Filtering

The shared helper `GetDiabeticPatientIdsAsync` shows how the system defines
"diabetic patient" -- by filtering conditions whose code starts with `E11` (the
ICD-10 prefix for Type 2 diabetes mellitus):

```csharp
// From src/FhirCopilot.Api/Services/StubAgentRunner.cs

private async Task<HashSet<string>> GetDiabeticPatientIdsAsync(CancellationToken ct)
{
    var conditions = await _backend.GetConditionsAsync(ct);
    return conditions
        .Where(condition => condition.Code.StartsWith("E11", StringComparison.OrdinalIgnoreCase))
        .Select(condition => condition.PatientId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
```

This encodes domain knowledge that might otherwise live only in a prompt or,
worse, be left for the LLM to figure out from scratch. The stub pins it down:
*E11 prefix means diabetes, case-insensitive, deduplicated by patient.*

---

## 2. Why This Is Powerful

### Tests Exercise Real Business Logic

The snapshot tests in `ResponseSnapshotTests.cs` send natural-language queries
through the full HTTP pipeline and verify the JSON response against checked-in
snapshots using [Verify.Xunit](https://github.com/VerifyTests/Verify):

```csharp
// From tests/FhirCopilot.Api.Tests/ResponseSnapshotTests.cs

[Fact]
public async Task Cohort_care_gap()
{
    var body = await PostAndReadAsync("Which diabetic patients are without metformin?", "snap-cohort");
    await Verifier.Verify(body).ScrubMember("threadId");
}
```

The verified snapshot for this test captures the *exact* expected output:

```json
{
  "answer": "Diabetic patients without metformin: Carla Gomez (Patient/patient-0003).",
  "citations": [
    {
      "resourceId": "Patient/patient-0003",
      "label": "Carla Gomez"
    }
  ],
  "reasoning": [
    "Selected cohort agent.",
    "Built the diabetes patient set from Condition records.",
    "Built the metformin patient set from MedicationRequest records.",
    "Computed the set difference."
  ],
  "toolsUsed": [
    "search_conditions",
    "search_medications",
    "calculator"
  ],
  "agentUsed": "cohort",
  "confidence": "high",
  "isStub": true
}
```

This is not a test that asserts "the response is not null." It asserts that the
system produces `Carla Gomez (Patient/patient-0003)` as the care gap patient,
with specific citations, reasoning steps, and tool usage. If someone changes the
FHIR sample data, the condition-filtering logic, or the set-difference
algorithm, the snapshot breaks. That is the point.

### A Developer Understands Expected Output Before Touching an LLM

When you are about to implement `GeminiAgentFrameworkRunner` (or any LLM-backed
runner), you can run the stub tests first and read the verified snapshots. They
tell you: "For a cohort care-gap query, the agent should identify exactly these
patients with exactly these citations." The LLM output does not need to match
character-for-character, but the snapshots document the *shape and content* of a
correct answer.

### The Stub Works as a Production Fallback

The `CopilotService` orchestrator does not treat the stub as test-only. It is a
first-class fallback:

```csharp
// From src/FhirCopilot.Api/Services/CopilotService.cs

if (_geminiRunner.IsConfigured)
{
    return await _geminiRunner.RunAsync(profile, request.Query, threadId, cancellationToken);
}

if (_runtime.UseStubWhenProviderMissing)
{
    return await _stubRunner.RunAsync(profile, request.Query, threadId, cancellationToken);
}

throw new InvalidOperationException("No provider is configured and stub mode is disabled.");
```

The `UseStubWhenProviderMissing` option (defaulting to `true` in
`RuntimeOptions`) means the system works out of the box with no API key. This is
not a degraded mode that returns "service unavailable." It returns a clinically
correct, citation-backed response. A demo, a development environment, or an air-gapped
deployment all get meaningful output.

### Snapshot Tests Catch Regressions Across Agent Types

There is one snapshot per agent type -- Clinical, Lookup, Analytics, Cohort,
Export, Search. Together, they form a regression suite that covers the full
surface of the system's clinical logic. The test fixture forces the stub path
regardless of environment configuration:

```csharp
// From tests/FhirCopilot.Api.Tests/CopilotFixture.cs

public class StubFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Provider:FhirBaseUrl"] = "",
                ["Provider:Mode"] = "Stub",
            });
        });

        return base.CreateHost(builder);
    }
}
```

This guarantees that CI runs test the deterministic path, not an external LLM
service.

---

## 3. How CopilotService Selects Between Runners

The selection logic lives in `CopilotService` and follows a clear priority:

1. **If Gemini is configured** (`ProviderOptions.IsGeminiMode` -- requires both
   `Mode = "Gemini"` and a non-empty `GEMINI_API_KEY` environment variable),
   route to `GeminiAgentFrameworkRunner`.

2. **If no provider is configured but `UseStubWhenProviderMissing` is true**
   (the default), route to `StubAgentRunner`.

3. **Otherwise**, throw an `InvalidOperationException`.

Both runners implement the same contract: they accept an `AgentProfile`, a
query string, a thread ID, and a cancellation token. They return the same
`CopilotResponse` record. The only structural difference is the `IsStub` flag
on the response, which the stub sets to `true` and the Gemini runner sets to
`false`.

The routing itself (deciding *which* agent type handles a query) happens before
runner selection. `IIntentRouter` classifies the query into one of the six agent
types (`lookup`, `search`, `analytics`, `clinical`, `cohort`, `export`), and
then `IAgentProfileStore` loads the corresponding profile. The runner just
executes.

---

## 4. The Trust Model Difference: Constructed vs. Extracted Citations

This is the most subtle part of the design and worth calling out explicitly.

**The stub builds citations from actual data access.** Every time the stub
queries the FHIR backend, it knows exactly which resources it touched. It
constructs `Citation` objects directly from the data:

```csharp
// StubAgentRunner -- citations are constructed from queried data
citations.AddRange(conditions.Select(
    condition => new Citation($"Condition/{condition.Id}", condition.Display)));
citations.AddRange(medications.Select(
    medication => new Citation($"MedicationRequest/{medication.Id}", medication.Display)));
```

Each citation carries both a `ResourceId` and a human-readable `Label`. The stub
*knows* the label because it fetched the resource.

**The live runner extracts citations from LLM-generated text via regex.** After
the Gemini agent produces its answer, `CitationExtractor` parses it:

```csharp
// From src/FhirCopilot.Api/Services/CitationExtractor.cs

private static readonly Regex CitationRegex = new(
    @"\b(?:Group|Patient|Encounter|Condition|Observation|MedicationRequest|Procedure|AllergyIntolerance)/[A-Za-z0-9\-_.]+\b",
    RegexOptions.Compiled);

public static IReadOnlyList<Citation> Extract(string answer)
{
    var citations = CitationRegex.Matches(answer)
        .Select(match => match.Value)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(id => new Citation(id))
        .ToList();

    return citations;
}
```

Notice the difference. The extracted citations have a `ResourceId` but *no
label* (the `Label` parameter defaults to `null`). The regex can find
`Patient/patient-0001` in the text, but it cannot reliably extract "Alice
Carter" as the associated label. The live runner also reports its confidence as
`"unverified"` -- an honest admission that LLM output has not been
cross-checked against the data store.

This is a deliberate design tension, not an oversight. The stub represents a
*high-trust* model: the system knows what data it accessed and can cite it
precisely. The live runner represents a *lower-trust* model: it relies on the
LLM to mention FHIR references in its output and does best-effort extraction.
The gap between these two models is exactly where future work (tool-call
auditing, citation verification, retrieval-augmented generation) would plug in.

---

## 5. How to Apply This Pattern in Your Own Projects

If you are building an AI agent system -- whether for healthcare, finance, legal,
or any domain with structured data -- here is the recipe:

### Step 1: Write the deterministic version first

Before you write a single prompt or configure an LLM, implement a class that
answers every query type using plain code. This forces you to answer fundamental
questions early:

- What data sources does each agent type need?
- What is the correct algorithm? (Set difference? Aggregation? Join?)
- What does a correct answer look like?
- What citations should accompany the answer?

### Step 2: Use it as your test fixture

Wire the deterministic runner into integration tests. Use snapshot testing
(Verify, Jest snapshots, or similar) to capture the exact output. These
snapshots become your specification -- they document what "correct" means for
each query type.

### Step 3: Use it as your fallback

Register the deterministic runner as a fallback for when the LLM provider is
unavailable. This gives you:

- A working demo without API keys
- Graceful degradation in production
- A development mode that does not burn API credits

### Step 4: Compare live output against the reference

When your LLM-backed runner is working, you can evaluate its output against the
stub's output. The stub answers are your ground truth. If the LLM says there
are three diabetic patients without metformin but the stub computes one, you
know the LLM got it wrong.

### Step 5: Make the trust gap visible

Follow this codebase's lead: mark stub responses with `IsStub: true` and live
responses with `Confidence: "unverified"`. Let the caller (UI, API consumer,
downstream system) decide how to handle the trust difference. Do not pretend
that LLM output and deterministic output carry equal weight.

---

## Summary

The `StubAgentRunner` in this codebase serves four roles simultaneously:

| Role                    | How                                                      |
|------------------------|----------------------------------------------------------|
| Reference implementation | Shows the exact algorithm each agent type should execute |
| Test fixture            | Snapshot tests verify its output for every agent type    |
| Production fallback     | `UseStubWhenProviderMissing` keeps the system functional |
| Specification document  | Verified snapshots capture what "correct" looks like     |

That is a lot of value from a single class. The key insight is that a stub does
not have to be stupid. When you make your stub smart -- when you encode real
domain logic into it -- it stops being throwaway scaffolding and becomes the
foundation of your system's correctness story.

---

**Relevant source files:**

- `src/FhirCopilot.Api/Services/StubAgentRunner.cs` -- the deterministic runner
- `src/FhirCopilot.Api/Services/GeminiAgentFrameworkRunner.cs` -- the LLM-backed runner
- `src/FhirCopilot.Api/Services/CopilotService.cs` -- the orchestrator that selects between them
- `src/FhirCopilot.Api/Services/CitationExtractor.cs` -- regex-based citation extraction for LLM output
- `src/FhirCopilot.Api/Options/RuntimeOptions.cs` -- the `UseStubWhenProviderMissing` flag
- `tests/FhirCopilot.Api.Tests/ResponseSnapshotTests.cs` -- snapshot tests covering all agent types
- `tests/FhirCopilot.Api.Tests/CopilotFixture.cs` -- test fixture forcing the stub path
