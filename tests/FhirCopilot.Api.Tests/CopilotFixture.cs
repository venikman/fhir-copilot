using FhirCopilot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// Forces SampleFhirBackend + StubAgentRunner regardless of appsettings.json.
    /// Uses Mode=Local to pass the startup mode validation, then overrides the runner
    /// via DI so tests run deterministically without a real LLM.
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
                    ["Provider:Mode"] = "Local",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IAgentRunner, StubAgentRunner>();
            });

            return base.CreateHost(builder);
        }
    }
}
