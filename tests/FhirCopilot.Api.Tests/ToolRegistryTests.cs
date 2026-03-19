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
