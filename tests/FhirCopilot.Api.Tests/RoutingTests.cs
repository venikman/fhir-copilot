using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FhirCopilot.Api.Tests;

public class RoutingTests : CopilotFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RoutingTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Theory]
    [InlineData("What insurance does patient-0001 have?", "lookup")]
    [InlineData("Show me patient-0002", "lookup")]
    [InlineData("Read encounter/encounter-0001", "lookup")]
    [InlineData("Find patients named Carter", "search")]
    [InlineData("List encounters for patient-0001", "lookup")] // "patient-0001" matches lookup regex (+2), ties with search, alphabetical tiebreak
    [InlineData("Search for female patients", "search")]
    [InlineData("How many diabetic patients do we have?", "analytics")]
    [InlineData("Count all patients in the panel", "analytics")]
    [InlineData("Clinical summary for patient-0001", "clinical")]
    [InlineData("Summarize Bob Nguyen's clinical history", "clinical")]
    [InlineData("Which diabetic patients are without metformin?", "cohort")]
    [InlineData("Identify care gaps for diabetic patients", "cohort")]
    [InlineData("Export all data for the Northwind group", "export")]
    [InlineData("Bulk export patient and encounter data", "export")]
    public async Task Query_routes_to_expected_agent(string query, string expectedAgent)
    {
        var response = await Client.PostAsJsonAsync("/api/copilot", new { query, threadId = $"routing-{expectedAgent}" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<CopilotResponseDto>(body, JsonOptions);

        Assert.NotNull(result);
        Assert.Equal(expectedAgent, result.AgentUsed);
    }

    private sealed record CopilotResponseDto(
        string Answer,
        string AgentUsed,
        string Confidence,
        string ThreadId,
        bool IsStub);
}
