using System.Net;
using System.Text.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;
using FhirCopilot.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FhirCopilot.Api.Tests;

public class ErrorHandlingTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HubConnection? _hub;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_hub is not null)
            await _hub.DisposeAsync();
        _factory?.Dispose();
    }

    private async Task<HubConnection> CreateHubWithRunner<TRunner>() where TRunner : class, IAgentRunner
    {
        _factory = new ThrowingRunnerFactory<TRunner>();

        _hub = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/copilot",
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Build();

        await _hub.StartAsync();
        return _hub;
    }

    [Fact(Timeout = 10_000)]
    public async Task SendQuery_throws_HubException_on_upstream_error()
    {
        var hub = await CreateHubWithRunner<UpstreamErrorRunner>();
        var ex = await Assert.ThrowsAsync<HubException>(
            () => hub.InvokeAsync<CopilotResponse>("SendQuery", new CopilotRequest("test query")));
        Assert.Contains("upstream_error:", ex.Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task SendQuery_throws_HubException_on_timeout()
    {
        var hub = await CreateHubWithRunner<TimeoutRunner>();
        var ex = await Assert.ThrowsAsync<HubException>(
            () => hub.InvokeAsync<CopilotResponse>("SendQuery", new CopilotRequest("test query")));
        Assert.Contains("timeout:", ex.Message);
    }

    [Fact(Timeout = 10_000)]
    public async Task SendQuery_throws_HubException_on_unexpected_error()
    {
        var hub = await CreateHubWithRunner<InternalErrorRunner>();
        var ex = await Assert.ThrowsAsync<HubException>(
            () => hub.InvokeAsync<CopilotResponse>("SendQuery", new CopilotRequest("test query")));
        Assert.Contains("internal_error:", ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    // Factory that replaces the agent runner and enables detailed SignalR errors.
    private sealed class ThrowingRunnerFactory<TRunner> : WebApplicationFactory<Program>
        where TRunner : class, IAgentRunner
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Provider:Mode"] = "Local",
                    ["Provider:FhirBaseUrl"] = "https://bulk-fhir.fly.dev/fhir",
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentRunner));
                if (descriptor != null) services.Remove(descriptor);
                services.AddSingleton<IAgentRunner, TRunner>();

                var fhirDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IFhirBackend));
                if (fhirDescriptor != null) services.Remove(fhirDescriptor);
                services.AddSingleton<IFhirBackend, NullFhirBackend>();

                services.PostConfigure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
                    options.EnableDetailedErrors = true);
            });
            return base.CreateHost(builder);
        }
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
