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

    [Fact]
    public void Composed_prompt_contains_response_guidelines()
    {
        var prompt = PromptComposer.Compose(MakeProfile());

        Assert.Contains("Cite resource IDs", prompt);
        Assert.Contains("reference chains", prompt);
        Assert.Contains("tables", prompt);
        Assert.Contains("Never dump raw JSON", prompt);
    }
}
