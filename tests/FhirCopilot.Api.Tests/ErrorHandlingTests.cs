using System.Net;
using System.Net.Http.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FhirCopilot.Api.Tests;

public class ErrorHandlingTests
{
    private static HttpClient CreateClientWithRunner<TRunner>() where TRunner : class, IAgentRunner
    {
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentRunner));
                    if (descriptor != null) services.Remove(descriptor);
                    services.AddSingleton<IAgentRunner, TRunner>();
                });
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Provider:Mode"] = "Stub",
                        ["Provider:FhirBaseUrl"] = "",
                    });
                });
            });
        return factory.CreateClient();
    }

    [Fact]
    public async Task Post_copilot_returns_502_on_HttpRequestException()
    {
        using var client = CreateClientWithRunner<UpstreamErrorRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("upstream_error", body!.Error.Type);
        Assert.DoesNotContain("StackTrace", body.Error.Message);
    }

    [Fact]
    public async Task Post_copilot_returns_504_on_TaskCanceledException()
    {
        using var client = CreateClientWithRunner<TimeoutRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("timeout", body!.Error.Type);
    }

    [Fact]
    public async Task Post_copilot_returns_500_on_unexpected_exception()
    {
        using var client = CreateClientWithRunner<InternalErrorRunner>();
        var response = await client.PostAsJsonAsync("/api/copilot", new CopilotRequest("test query"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CopilotErrorResponse>();
        Assert.NotNull(body);
        Assert.Equal("internal_error", body!.Error.Type);
        Assert.DoesNotContain("secret", body.Error.Message);
    }

    // --- Throwing IAgentRunner stubs ---

    private class UpstreamErrorRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new HttpRequestException("Gemini API error", null, HttpStatusCode.TooManyRequests);
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new HttpRequestException("Gemini API error");
    }

    private class TimeoutRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new TaskCanceledException("Request timed out");
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new TaskCanceledException("Request timed out");
    }

    private class InternalErrorRunner : IAgentRunner
    {
        public Task<CopilotResponse> RunAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new InvalidOperationException("secret internal details here");
        public IAsyncEnumerable<CopilotStreamEvent> StreamAsync(AgentProfile p, string q, string t, CancellationToken ct)
            => throw new InvalidOperationException("secret internal details");
    }
}
