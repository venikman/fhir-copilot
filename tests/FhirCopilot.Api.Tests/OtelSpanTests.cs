using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Services;

namespace FhirCopilot.Api.Tests;

public class OtelSpanTests : CopilotFixture
{
    public OtelSpanTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task Alive_endpoint_returns_healthy()
    {
        var response = await Client.GetAsync("/alive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Copilot_request_emits_agent_span()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FhirCopilot.Agent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot", request);
        response.EnsureSuccessStatusCode();

        Assert.Contains(activities, a => a.OperationName == "copilot.request");

        var span = activities.First(a => a.OperationName == "copilot.request");
        Assert.NotNull(span.GetTagItem("copilot.agent"));
        Assert.NotNull(span.GetTagItem("copilot.thread_id"));
        Assert.Equal("StubAgentRunner", span.GetTagItem("copilot.runner"));
    }

    [Fact]
    public async Task Copilot_stream_emits_agent_span()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "FhirCopilot.Agent",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot/stream", request);
        response.EnsureSuccessStatusCode();

        // Read the stream to completion so the span finishes
        await response.Content.ReadAsStringAsync();

        Assert.Contains(activities, a => a.OperationName == "copilot.stream");
    }
}
