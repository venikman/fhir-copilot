using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Services;

namespace FhirCopilot.Api.Tests;

public class OtelMetricTests : CopilotFixture
{
    public OtelMetricTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task Copilot_request_emits_request_counter()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "FhirCopilot.Agent" && instrument.Name == "copilot.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot", request);
        response.EnsureSuccessStatusCode();

        Assert.NotEmpty(measurements);

        var recorded = measurements[^1];
        Assert.Equal(1, recorded.Value);
        Assert.Contains(recorded.Tags, t => t.Key == "copilot.agent" && t.Value is string);
        Assert.Contains(recorded.Tags, t => t.Key == "copilot.status" && (string?)t.Value == "success");
    }

    [Fact]
    public async Task Copilot_request_emits_duration_histogram()
    {
        var measurements = new List<(double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "FhirCopilot.Agent" && instrument.Name == "copilot.request.duration_ms")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot", request);
        response.EnsureSuccessStatusCode();

        Assert.NotEmpty(measurements);
        Assert.True(measurements[^1].Value > 0, "Duration should be positive");
        Assert.Contains(measurements[^1].Tags, t => t.Key == "copilot.agent" && t.Value is string);
    }

    [Fact]
    public async Task Copilot_stream_emits_request_counter()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "FhirCopilot.Agent" && instrument.Name == "copilot.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot/stream", request);
        response.EnsureSuccessStatusCode();

        // Read stream to completion so the finally block fires
        await response.Content.ReadAsStringAsync();

        Assert.NotEmpty(measurements);

        var recorded = measurements[^1];
        Assert.Equal(1, recorded.Value);
        Assert.Contains(recorded.Tags, t => t.Key == "copilot.status" && (string?)t.Value == "success");
    }

    [Fact]
    public async Task Copilot_request_emits_routing_decision()
    {
        var measurements = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "FhirCopilot.Agent" && instrument.Name == "copilot.routing.decisions")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            measurements.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var request = new CopilotRequest("show me patient demographics", null);
        var response = await Client.PostAsJsonAsync("/api/copilot", request);
        response.EnsureSuccessStatusCode();

        Assert.NotEmpty(measurements);
        Assert.Contains(measurements[^1].Tags, t => t.Key == "copilot.agent" && t.Value is string);
    }
}
