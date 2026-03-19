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
