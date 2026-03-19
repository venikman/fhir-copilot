using System.Text.Json;
using FhirCopilot.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace FhirCopilot.Api.Tests;

[Trait("Category", "Integration")]
public sealed class SignalRHubTests : IAsyncLifetime, IClassFixture<SignalRHubTests.LocalLlmFactory>
{
    private readonly LocalLlmFactory _factory;
    private readonly ITestOutputHelper _output;
    private HubConnection _hub = null!;
    private static bool? _llmAvailable;

    public SignalRHubTests(LocalLlmFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _llmAvailable ??= await CheckLlmAvailableAsync();
        _hub = CreateHubConnection();
    }

    public async Task DisposeAsync()
    {
        if (_hub.State != HubConnectionState.Disconnected)
            await _hub.DisposeAsync();
    }

    [Fact(Timeout = 10_000)]
    public async Task Hub_connects_and_disconnects()
    {
        await _hub.StartAsync();
        Assert.Equal(HubConnectionState.Connected, _hub.State);

        await _hub.StopAsync();
        Assert.Equal(HubConnectionState.Disconnected, _hub.State);
    }

    [Fact(Timeout = 60_000)]
    public async Task SendQuery_returns_response_from_real_llm()
    {
        if (SkipIfNoLlm()) return;

        await _hub.StartAsync();

        var response = await _hub.InvokeAsync<CopilotResponse>(
            "SendQuery",
            new CopilotRequest("How many patients are in the panel?", "send-query-1"));

        Assert.NotNull(response);
        Assert.NotNull(response.Answer);
        Assert.False(string.IsNullOrWhiteSpace(response.AgentUsed));
        Assert.Equal("send-query-1", response.ThreadId);
        Assert.False(response.IsStub);
    }

    [Fact(Timeout = 60_000)]
    public async Task StreamQuery_emits_meta_delta_done()
    {
        if (SkipIfNoLlm()) return;

        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("Clinical summary for patient-0001", "stream-protocol-1"));

        Assert.True(events.Count >= 2, $"Expected at least 2 events (meta + done), got {events.Count}");
        Assert.Equal("meta", events[0].Type);
        Assert.Equal("done", events[^1].Type);
        // Deltas are expected but optional — tool-heavy queries may produce no text chunks
        if (events.Count >= 3)
            Assert.Contains(events, e => e.Type == "delta");
    }

    [Fact(Timeout = 60_000)]
    public async Task StreamQuery_meta_has_agent_and_thread()
    {
        if (SkipIfNoLlm()) return;

        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("Find patients named Carter", "stream-meta-1"));

        var meta = events.First(e => e.Type == "meta");
        Assert.False(string.IsNullOrWhiteSpace(meta.AgentType));
        Assert.Equal("stream-meta-1", meta.ThreadId);
        Assert.False(meta.IsStub);
    }

    [Fact(Timeout = 60_000)]
    public async Task StreamQuery_deltas_reassemble_to_answer()
    {
        if (SkipIfNoLlm()) return;

        await _hub.StartAsync();

        var events = await CollectStreamEventsAsync(
            new CopilotRequest("What insurance does patient-0001 have?", "stream-reassemble-1"));

        var reassembled = string.Concat(
            events.Where(e => e.Type == "delta").Select(e => e.Content ?? ""));

        var done = events.Last(e => e.Type == "done");
        Assert.NotNull(done.Response);
        Assert.Equal(done.Response.Answer, reassembled);
    }

    [Fact(Timeout = 120_000)]
    public async Task StreamQuery_preserves_threadId()
    {
        if (SkipIfNoLlm()) return;

        await _hub.StartAsync();

        var threadId = "stream-persist-1";
        var events1 = await CollectStreamEventsAsync(
            new CopilotRequest("How many patients?", threadId));

        Assert.Equal(threadId, events1.First(e => e.Type == "meta").ThreadId);

        var events2 = await CollectStreamEventsAsync(
            new CopilotRequest("Tell me more about the first one", threadId));

        Assert.Equal(threadId, events2.First(e => e.Type == "meta").ThreadId);
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

    private static async Task<bool> CheckLlmAvailableAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync("http://localhost:1234/v1/models");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private bool SkipIfNoLlm()
    {
        if (_llmAvailable != true)
        {
            _output.WriteLine("SKIPPED: Local LLM (LM Studio at localhost:1234) is not available.");
            return true;
        }
        return false;
    }

    public sealed class LocalLlmFactory : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Provider:Mode"] = "Local",
                    ["Provider:LocalEndpoint"] = "http://localhost:1234",
                    ["Provider:LocalModel"] = "zai-org/glm-4.7-flash",
                    ["Provider:FhirBaseUrl"] = "",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSignalR(options => options.EnableDetailedErrors = true);
            });
            return base.CreateHost(builder);
        }
    }
}
