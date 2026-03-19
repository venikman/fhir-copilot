using System.Text.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Options;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FhirCopilot.Api.Tests;

/// <summary>
/// Hub protocol and error-path tests using StubAgentRunner (deterministic, no LLM needed).
/// Complements the real-LLM SignalRHubTests for CI environments without LM Studio.
/// </summary>
public sealed class SignalRHubStubTests : IAsyncLifetime, IClassFixture<CopilotFixture.StubFactory>
{
    private readonly CopilotFixture.StubFactory _factory;
    private HubConnection _hub = null!;

    public SignalRHubStubTests(CopilotFixture.StubFactory factory) => _factory = factory;

    public Task InitializeAsync()
    {
        _hub = CreateHubConnection();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_hub.State != HubConnectionState.Disconnected)
            await _hub.DisposeAsync();
    }

    [Fact]
    public async Task Hub_connects_and_disconnects()
    {
        await _hub.StartAsync();
        Assert.Equal(HubConnectionState.Connected, _hub.State);

        await _hub.StopAsync();
        Assert.Equal(HubConnectionState.Disconnected, _hub.State);
    }

    [Fact]
    public async Task SendQuery_returns_response_with_stub()
    {
        await _hub.StartAsync();

        var response = await _hub.InvokeAsync<CopilotResponse>(
            "SendQuery",
            new CopilotRequest("How many diabetic patients?", "hub-send-1"));

        Assert.NotNull(response);
        Assert.False(string.IsNullOrWhiteSpace(response.Answer));
        Assert.False(string.IsNullOrWhiteSpace(response.AgentUsed));
        Assert.Equal("hub-send-1", response.ThreadId);
        Assert.True(response.IsStub);
    }

    [Fact]
    public async Task StreamQuery_emits_meta_delta_done_with_stub()
    {
        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("Clinical summary for patient-0001", "hub-stream-1"));

        Assert.True(events.Count >= 3, $"Expected at least 3 events, got {events.Count}");
        Assert.Equal("meta", events[0].Type);
        Assert.Contains(events, e => e.Type == "delta");
        Assert.Equal("done", events[^1].Type);
    }

    [Fact]
    public async Task StreamQuery_meta_contains_agent_and_thread()
    {
        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("Find patients named Carter", "hub-meta-1"));

        var meta = events.First(e => e.Type == "meta");
        Assert.False(string.IsNullOrWhiteSpace(meta.AgentType));
        Assert.Equal("hub-meta-1", meta.ThreadId);
        Assert.True(meta.IsStub);
    }

    [Fact]
    public async Task StreamQuery_deltas_reassemble_to_answer()
    {
        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("What insurance does patient-0001 have?", "hub-reassemble-1"));

        var reassembled = string.Concat(
            events.Where(e => e.Type == "delta").Select(e => e.Content ?? ""));

        var done = events.Last(e => e.Type == "done");
        Assert.NotNull(done.Response);
        Assert.Equal(done.Response.Answer, reassembled);
    }

    [Fact]
    public async Task SendQuery_preserves_threadId()
    {
        await _hub.StartAsync();

        var r1 = await _hub.InvokeAsync<CopilotResponse>(
            "SendQuery",
            new CopilotRequest("How many patients?", "hub-persist-1"));

        var r2 = await _hub.InvokeAsync<CopilotResponse>(
            "SendQuery",
            new CopilotRequest("Tell me about the first one", "hub-persist-1"));

        Assert.Equal("hub-persist-1", r1.ThreadId);
        Assert.Equal("hub-persist-1", r2.ThreadId);
    }

    [Fact]
    public void IsLocalMode_returns_true_for_local()
    {
        var options = new ProviderOptions { Mode = "Local" };
        Assert.True(options.IsLocalMode);
    }

    [Fact]
    public void IsLocalMode_returns_false_for_gemini()
    {
        var options = new ProviderOptions { Mode = "Gemini" };
        Assert.False(options.IsLocalMode);
    }

    [Fact]
    public void IsLocalMode_is_case_insensitive()
    {
        var options = new ProviderOptions { Mode = "local" };
        Assert.True(options.IsLocalMode);
    }

    [Fact]
    public void Unsupported_mode_throws_at_startup()
    {
        var factory = new UnsupportedModeFactory();
        var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("BadMode", ex.Message);
        Assert.Contains("not supported", ex.Message);
        factory.Dispose();
    }

    private HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/copilot",
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
            .Build();

    private async Task<List<CopilotStreamEvent>> CollectStreamEventsAsync(CopilotRequest request)
    {
        var events = new List<CopilotStreamEvent>();

        await foreach (var evt in _hub.StreamAsync<CopilotStreamEvent>("StreamQuery", request))
        {
            events.Add(evt);
        }

        return events;
    }

    private sealed class UnsupportedModeFactory : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override Microsoft.Extensions.Hosting.IHost CreateHost(
            Microsoft.Extensions.Hosting.IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Provider:FhirBaseUrl"] = "",
                    ["Provider:Mode"] = "BadMode",
                });
            });
            return base.CreateHost(builder);
        }
    }
}
