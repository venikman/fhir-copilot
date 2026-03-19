# v1 Prompt & Tool Adoption Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port 5 high-value features from fhir-agents v1 into the v2 codebase — FHIR data model prompts, code system references, search_procedures tool, cohort set-operation patterns, and shared response guidelines.

**Architecture:** Agent prompts are composed by `PromptComposer` from per-agent JSON profiles (`config/agents/*.json`). Shared knowledge (FHIR model, code systems, response guidelines) is injected by `PromptComposer` itself rather than duplicated across profiles. The new `search_procedures` tool follows the existing pattern: `IFhirBackend` method → `HttpFhirBackend` mapper → `FhirToolbox` wrapper → `ToolRegistry` entry.

**Tech Stack:** .NET 10, ASP.NET Core, System.Text.Json, xUnit + FsCheck

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `src/FhirCopilot.Api/Services/PromptComposer.cs` | Modify | Inject shared FHIR model, code systems, response guidelines |
| `src/FhirCopilot.Api/Fhir/SampleFhirBackend.cs` | Modify | Add `SearchProceduresAsync` to `IFhirBackend` interface |
| `src/FhirCopilot.Api/Fhir/HttpFhirBackend.cs` | Modify | Add `SearchProceduresAsync` + `MapProcedure` |
| `src/FhirCopilot.Api/Fhir/FhirToolbox.cs` | Modify | Add `SearchProcedures` tool method |
| `src/FhirCopilot.Api/Services/ToolRegistry.cs` | Modify | Register `search_procedures` |
| `src/FhirCopilot.Api/config/agents/search.json` | Modify | Add `search_procedures` to allowedTools |
| `src/FhirCopilot.Api/config/agents/analytics.json` | Modify | Add `search_procedures` to allowedTools |
| `src/FhirCopilot.Api/config/agents/clinical.json` | Modify | Add `search_procedures` to allowedTools |
| `src/FhirCopilot.Api/config/agents/cohort.json` | Modify | Add `search_procedures`, expand instructions |
| `tests/FhirCopilot.Api.Tests/PromptComposerTests.cs` | Create | Test shared prompt sections are injected |
| `tests/FhirCopilot.Api.Tests/ToolRegistryTests.cs` | Create | Test search_procedures is registered and wired |

---

## Task 1: Add FHIR Data Model to PromptComposer

The biggest gap — v1 injected a complete FHIR reference graph into every agent prompt. v2's agents don't know how resources link to each other.

**Files:**
- Modify: `src/FhirCopilot.Api/Services/PromptComposer.cs`
- Create: `tests/FhirCopilot.Api.Tests/PromptComposerTests.cs`

- [ ] **Step 1: Write failing test — FHIR model is present in composed prompt**

```csharp
// tests/FhirCopilot.Api.Tests/PromptComposerTests.cs
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Services;

namespace FhirCopilot.Api.Tests;

public class PromptComposerTests
{
    private static AgentProfile MakeProfile(string name = "test") => new()
    {
        Name = name,
        DisplayName = "Test",
        Purpose = "Testing",
        AllowedTools = ["read_resource"],
        Instructions = ["Do the thing."],
        DomainContext = ["Test context."],
        StructuredSections = ["Answer"]
    };

    [Fact]
    public void Composed_prompt_contains_fhir_data_model()
    {
        var prompt = PromptComposer.Compose(MakeProfile());

        Assert.Contains("FHIR Data Model", prompt);
        Assert.Contains("Patient", prompt);
        Assert.Contains("Encounter", prompt);
        Assert.Contains("Condition", prompt);
        Assert.Contains("Observation", prompt);
        Assert.Contains("MedicationRequest", prompt);
        Assert.Contains("Procedure", prompt);
        Assert.Contains("AllergyIntolerance", prompt);
        Assert.Contains("Coverage", prompt);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_fhir_data_model`
Expected: FAIL — "FHIR Data Model" not found in prompt

- [ ] **Step 3: Add FHIR Data Model section to PromptComposer**

Add this constant and inject it in `Compose()` after the purpose line and before domain context:

```csharp
// In PromptComposer.cs, add as a private const:
private const string FhirDataModel = """
    FHIR Data Model:
    - Group — Attribution lists. Contains member references -> Patient.
    - Patient — Demographics, address. Has generalPractitioner -> PractitionerRole, managingOrganization -> Organization.
    - Practitioner — Providers (doctors, nurses). Name, NPI, qualifications.
    - PractitionerRole — Links Practitioner -> Organization + specialty + location.
    - Organization — Clinics, hospitals, payers. Name, address, type.
    - Coverage — Insurance. payor -> Organization, beneficiary -> Patient, period, status.
    - Encounter — Visits. subject -> Patient, participant -> Practitioner, type (CPT), reasonCode (ICD-10), location, period, status.
    - Condition — Diagnoses. subject -> Patient, code (ICD-10), clinicalStatus, category.
    - Observation — Labs & vitals. subject -> Patient, code (LOINC), valueQuantity, effectiveDateTime, category.
    - MedicationRequest — Prescriptions. subject -> Patient, medicationCodeableConcept (RxNorm), status, authoredOn.
    - Procedure — Performed procedures. subject -> Patient, code (CPT), status, performedDateTime.
    - AllergyIntolerance — Allergies. patient -> Patient, code, clinicalStatus, criticality.
    """;
```

In `Compose()`, after the purpose line (`builder.AppendLine($"Purpose: {profile.Purpose}");`), add:

```csharp
builder.AppendLine();
builder.AppendLine(FhirDataModel);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_fhir_data_model`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/FhirCopilot.Api/Services/PromptComposer.cs tests/FhirCopilot.Api.Tests/PromptComposerTests.cs
git commit -m "feat: inject FHIR data model into all agent prompts (ported from v1)"
```

---

## Task 2: Add Code Systems reference to PromptComposer

v1 gave every agent ICD-10, LOINC, RxNorm, and CPT lookup references. Currently only the search agent has a single instruction line about code mappings.

**Files:**
- Modify: `src/FhirCopilot.Api/Services/PromptComposer.cs`
- Modify: `tests/FhirCopilot.Api.Tests/PromptComposerTests.cs`

- [ ] **Step 1: Write failing test — code systems present in prompt**

```csharp
// Add to PromptComposerTests.cs
[Fact]
public void Composed_prompt_contains_code_systems()
{
    var prompt = PromptComposer.Compose(MakeProfile());

    Assert.Contains("Code Systems", prompt);
    Assert.Contains("ICD-10", prompt);
    Assert.Contains("LOINC", prompt);
    Assert.Contains("RxNorm", prompt);
    Assert.Contains("CPT", prompt);
    Assert.Contains("4548-4", prompt);  // HbA1c LOINC code
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_code_systems`
Expected: FAIL

- [ ] **Step 3: Add Code Systems section to PromptComposer**

Add constant after `FhirDataModel`:

```csharp
private const string CodeSystems = """
    Code Systems (use system|code format in searches):
    - ICD-10-CM (diagnoses): E11.* (Type 2 diabetes), I10 (Hypertension), J06.9 (URI), M54.5 (Low back pain)
    - CPT (procedures/encounters): 99213 (Office visit), 99385 (Preventive visit)
    - LOINC (observations): 4548-4 (HbA1c), 2339-0 (Glucose), 8480-6 (Systolic BP), 8462-4 (Diastolic BP)
    - RxNorm (medications): 860975 (Metformin), 310798 (Lisinopril), 197361 (Amlodipine)
    """;
```

In `Compose()`, right after the FHIR Data Model block:

```csharp
builder.AppendLine(CodeSystems);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_code_systems`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/FhirCopilot.Api/Services/PromptComposer.cs tests/FhirCopilot.Api.Tests/PromptComposerTests.cs
git commit -m "feat: inject code systems reference into all agent prompts (ported from v1)"
```

---

## Task 3: Add shared Response Guidelines to PromptComposer

v1 had shared response guidelines injected into every agent. v2 has a partial "Response contract" section but it's missing key items like "show reference chains" and "format tables for comparisons."

**Files:**
- Modify: `src/FhirCopilot.Api/Services/PromptComposer.cs`
- Modify: `tests/FhirCopilot.Api.Tests/PromptComposerTests.cs`

- [ ] **Step 1: Write failing test — expanded response guidelines**

```csharp
[Fact]
public void Composed_prompt_contains_response_guidelines()
{
    var prompt = PromptComposer.Compose(MakeProfile());

    Assert.Contains("Cite resource IDs", prompt);
    Assert.Contains("reference chains", prompt);
    Assert.Contains("tables", prompt);
    Assert.Contains("Never dump raw JSON", prompt);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_response_guidelines`
Expected: FAIL — "reference chains" not found

- [ ] **Step 3: Expand the response contract in PromptComposer**

Replace the existing response contract block in `Compose()` (the block starting with `builder.AppendLine("Response contract:");` through `builder.AppendLine("Never dump raw JSON...");`) with:

```csharp
builder.AppendLine("Response contract:");
builder.AppendLine("- Lead with the direct answer.");
builder.AppendLine("- Cite resource IDs (e.g. Patient/patient-0001) for traceability.");
builder.AppendLine("- Show reference chains when resolving (e.g. Patient -> Practitioner -> Organization).");
builder.AppendLine("- Format tables for directory/comparison queries.");
builder.AppendLine("- Direct answer first for yes/no questions, then evidence.");
builder.AppendLine("- Include a short reasoning summary.");

if (profile.StructuredSections.Count > 0)
{
    builder.AppendLine("- Use these sections when relevant:");
    foreach (var section in profile.StructuredSections)
    {
        builder.AppendLine($"  - {section}");
    }
}

builder.AppendLine();
builder.AppendLine("Never dump raw JSON when a plain-English synthesis is possible.");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter PromptComposerTests.Composed_prompt_contains_response_guidelines`
Expected: PASS

- [ ] **Step 5: Run all tests**

Run: `dotnet test`
Expected: All tests pass (existing + new)

- [ ] **Step 6: Commit**

```bash
git add src/FhirCopilot.Api/Services/PromptComposer.cs tests/FhirCopilot.Api.Tests/PromptComposerTests.cs
git commit -m "feat: expand shared response guidelines in all agent prompts (ported from v1)"
```

---

## Task 4: Add search_procedures tool

v1 had `search_procedures` for CPT code lookups. v2 already has `ProcedureRecord` in `DomainModels.cs:52-58` but never wired the search. This task adds the full vertical: interface → backend → toolbox → registry.

**Files:**
- Modify: `src/FhirCopilot.Api/Fhir/SampleFhirBackend.cs` (IFhirBackend interface)
- Modify: `src/FhirCopilot.Api/Fhir/HttpFhirBackend.cs`
- Modify: `src/FhirCopilot.Api/Fhir/FhirToolbox.cs`
- Modify: `src/FhirCopilot.Api/Services/ToolRegistry.cs`
- Modify: `tests/FhirCopilot.Api.Tests/NullFhirBackend.cs` (add interface stub)
- Create: `tests/FhirCopilot.Api.Tests/ToolRegistryTests.cs`

- [ ] **Step 1: Write failing test — search_procedures is a registered tool**

```csharp
// tests/FhirCopilot.Api.Tests/ToolRegistryTests.cs
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Services;

namespace FhirCopilot.Api.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void Search_procedures_is_registered()
    {
        var toolbox = new FhirToolbox(new NullFhirBackend());
        var tools = ToolRegistry.BuildTools(toolbox, ["search_procedures"]);
        Assert.Single(tools);
        Assert.Equal("SearchProcedures", tools[0].Name, StringComparer.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter ToolRegistryTests.Search_procedures_is_registered`
Expected: FAIL — "search_procedures" not in KnownToolNames

- [ ] **Step 3: Add SearchProceduresAsync to IFhirBackend and NullFhirBackend**

In `src/FhirCopilot.Api/Fhir/SampleFhirBackend.cs`, add to the `IFhirBackend` interface (after `SearchAllergiesAsync`):

```csharp
Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default);
```

In `tests/FhirCopilot.Api.Tests/NullFhirBackend.cs`, add the stub (this class implements `IFhirBackend` for tests):

```csharp
public Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default) => throw new NotImplementedException();
```

- [ ] **Step 4: Implement in HttpFhirBackend**

In `src/FhirCopilot.Api/Fhir/HttpFhirBackend.cs`, add after `SearchAllergiesAsync`:

```csharp
public Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default) =>
    SearchAsync(BuildSearchUrl("Procedure", ("patient", patientId), ("code", code)), MapProcedure, ct);
```

Add the mapper after `MapAllergy`:

```csharp
private static ProcedureRecord MapProcedure(JsonElement r) =>
    new(Str(r, "id"), Ref(r, "subject"),
        Coding(r, "code"), Coding(r, "code", "display"),
        Str(r, "status"), Str(r, "performedDateTime"));
```

- [ ] **Step 5: Add SearchProcedures to FhirToolbox**

In `src/FhirCopilot.Api/Fhir/FhirToolbox.cs`, add after `SearchAllergies`:

```csharp
[Description("Search Procedure resources by patient or CPT code.")]
public async Task<string> SearchProcedures(
    [Description("Optional patient id.")] string? patientId = null,
    [Description("Optional CPT code or procedure display text.")] string? code = null)
    => JsonSerializer.Serialize(await _backend.SearchProceduresAsync(patientId, code), Services.JsonDefaults.Serializer);
```

- [ ] **Step 6: Register in ToolRegistry**

In `src/FhirCopilot.Api/Services/ToolRegistry.cs`:

Add `"search_procedures"` to `KnownToolNames`:

```csharp
private static readonly HashSet<string> KnownToolNames = new(StringComparer.OrdinalIgnoreCase)
{
    "search_groups", "read_resource", "list_resources", "bulk_export",
    "search_patients", "search_encounters", "search_conditions",
    "search_observations", "search_medications", "search_procedures",
    "search_allergies", "calculator"
};
```

Add to `BuildAllTools`:

```csharp
["search_procedures"] = AIFunctionFactory.Create(toolbox.SearchProcedures),
```

- [ ] **Step 7: Build to verify compilation**

Run: `dotnet build`
Expected: Build succeeded, 0 errors

- [ ] **Step 8: Run test to verify it passes**

Run: `dotnet test --filter ToolRegistryTests.Search_procedures_is_registered`
Expected: PASS

- [ ] **Step 9: Run all tests**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 10: Commit**

```bash
git add src/FhirCopilot.Api/Fhir/SampleFhirBackend.cs src/FhirCopilot.Api/Fhir/HttpFhirBackend.cs src/FhirCopilot.Api/Fhir/FhirToolbox.cs src/FhirCopilot.Api/Services/ToolRegistry.cs tests/FhirCopilot.Api.Tests/NullFhirBackend.cs tests/FhirCopilot.Api.Tests/ToolRegistryTests.cs
git commit -m "feat: add search_procedures tool (ported from v1)"
```

---

## Task 5: Add search_procedures to agent profiles

Now that the tool exists, give it to the agents that need it: search, analytics, clinical, cohort (same as v1).

**Files:**
- Modify: `src/FhirCopilot.Api/config/agents/search.json`
- Modify: `src/FhirCopilot.Api/config/agents/analytics.json`
- Modify: `src/FhirCopilot.Api/config/agents/clinical.json`
- Modify: `src/FhirCopilot.Api/config/agents/cohort.json`

- [ ] **Step 1: Add `search_procedures` to search.json allowedTools**

Add `"search_procedures"` after `"search_allergies"` in the `allowedTools` array.

- [ ] **Step 2: Add `search_procedures` to analytics.json allowedTools**

Add `"search_procedures"` after `"search_allergies"` in the `allowedTools` array.

- [ ] **Step 3: Add `search_procedures` to clinical.json allowedTools**

Add `"search_procedures"` after `"search_allergies"` in the `allowedTools` array.

- [ ] **Step 4: Add `search_procedures` to cohort.json allowedTools**

Add `"search_procedures"` after `"search_allergies"` in the `allowedTools` array.

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: Build succeeded, all tests pass. `ValidateProfiles` at startup should NOT warn about `search_procedures`.

- [ ] **Step 6: Commit**

```bash
git add src/FhirCopilot.Api/config/agents/search.json src/FhirCopilot.Api/config/agents/analytics.json src/FhirCopilot.Api/config/agents/clinical.json src/FhirCopilot.Api/config/agents/cohort.json
git commit -m "feat: grant search_procedures tool to search, analytics, clinical, cohort agents"
```

---

## Task 6: Expand cohort agent with set-operation patterns

v1's cohort prompt taught three explicit patterns (intersection, difference/care-gaps, at-risk). v2's cohort instructions are abstract. Port the concrete patterns.

**Files:**
- Modify: `src/FhirCopilot.Api/config/agents/cohort.json`

- [ ] **Step 1: Replace cohort.json instructions and domainContext**

Replace `instructions` with:

```json
"instructions": [
    "Treat cohort work as explicit set operations across resource types.",
    "Intersection: Search conditions for diagnosis A, search medications for drug B, find patients in BOTH sets.",
    "Difference (care gaps): Search conditions for a diagnosis, search observations for expected monitoring, find patients WITH the diagnosis but WITHOUT the monitoring.",
    "At-risk: Combine condition + observation thresholds to flag patients needing attention (e.g. diabetic patients with HbA1c > 9).",
    "Reason about absence of data: if a diabetic patient has no HbA1c observation, that IS a finding.",
    "Report the count AND the patient list when feasible.",
    "Be explicit about inclusion criteria, exclusion criteria, and what counts as missing evidence."
]
```

Replace `domainContext` with:

```json
"domainContext": [
    "Cohort differs from search because it combines multiple resource types or negative conditions.",
    "Care gap logic should be deterministic wherever possible.",
    "For gap analysis, clearly state what is missing and for whom.",
    "Flag anomalies with an explanation of why they are concerning."
]
```

- [ ] **Step 2: Build and run all tests**

Run: `dotnet build && dotnet test`
Expected: All tests pass (router property tests still valid — config changes don't affect routing)

- [ ] **Step 3: Commit**

```bash
git add src/FhirCopilot.Api/config/agents/cohort.json
git commit -m "feat: expand cohort agent with v1 set-operation patterns and care-gap instructions"
```

---

## Task 7: Final verification

- [ ] **Step 1: Run all tests**

Run: `dotnet test`
Expected: All tests pass (19 existing + 4 new = 23 total)

- [ ] **Step 2: Verify PromptComposer output manually**

Run this in a test or scratch file to eyeball the composed prompt:

```bash
dotnet run --project src/FhirCopilot.Api -- --urls "http://localhost:0" &
sleep 2 && kill %1
```

Check that startup logs show no `ValidateProfiles` warnings about unknown tools.

- [ ] **Step 3: Final commit (if any fixups needed)**

```bash
git add -A && git commit -m "chore: final fixups for v1 adoption"
```
