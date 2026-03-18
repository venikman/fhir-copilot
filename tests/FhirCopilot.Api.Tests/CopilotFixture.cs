using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FhirCopilot.Api.Tests;

public class CopilotFixture : IClassFixture<CopilotFixture.StubFactory>
{
    protected readonly HttpClient Client;

    public CopilotFixture(StubFactory factory)
    {
        Client = factory.CreateClient();
    }

    /// <summary>
    /// Forces SampleFhirBackend + StubAgentRunner regardless of appsettings.json
    /// by clearing FhirBaseUrl and GEMINI_API_KEY.
    /// </summary>
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
}
