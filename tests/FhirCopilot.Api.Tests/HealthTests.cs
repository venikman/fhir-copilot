using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FhirCopilot.Api.Tests;

public class HealthTests : CopilotFixture
{
    public HealthTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        var response = await Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Root_returns_welcome_text()
    {
        var response = await Client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("FHIR Copilot", body);
    }
}
