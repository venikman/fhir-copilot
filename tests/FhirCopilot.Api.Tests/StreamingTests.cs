using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FhirCopilot.Api.Tests;

public class StreamingTests : CopilotFixture
{
    public StreamingTests(CopilotFixture.StubFactory factory) : base(factory) { }

    [Fact]
    public async Task Stream_returns_sse_content_type()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot/stream",
            new { query = "Clinical summary for patient-0001", threadId = "stream-type-1" });

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_emits_meta_delta_done_events()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot/stream",
            new { query = "How many diabetic patients?", threadId = "stream-events-1" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        Assert.True(events.Count >= 3, $"Expected at least 3 SSE events, got {events.Count}");

        // First event must be meta
        Assert.Equal("meta", events[0].EventType);

        // Must have at least one delta
        Assert.Contains(events, e => e.EventType == "delta");

        // Last event must be done
        Assert.Equal("done", events[^1].EventType);
    }

    [Fact]
    public async Task Stream_meta_event_contains_agent_and_thread()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot/stream",
            new { query = "Clinical summary for patient-0001", threadId = "stream-meta-1" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);
        var meta = events.First(e => e.EventType == "meta");
        var doc = JsonDocument.Parse(meta.Data);

        Assert.True(doc.RootElement.TryGetProperty("agentType", out var agentType));
        Assert.False(string.IsNullOrWhiteSpace(agentType.GetString()));

        Assert.True(doc.RootElement.TryGetProperty("threadId", out var threadId));
        Assert.Equal("stream-meta-1", threadId.GetString());

        Assert.True(doc.RootElement.TryGetProperty("isStub", out var isStub));
        Assert.True(isStub.GetBoolean());
    }

    [Fact]
    public async Task Stream_done_event_contains_full_response()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot/stream",
            new { query = "Find patients named Carter", threadId = "stream-done-1" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);
        var done = events.Last(e => e.EventType == "done");
        var doc = JsonDocument.Parse(done.Data);

        Assert.True(doc.RootElement.TryGetProperty("response", out var resp));
        Assert.True(resp.TryGetProperty("answer", out _));
        Assert.True(resp.TryGetProperty("citations", out _));
        Assert.True(resp.TryGetProperty("agentUsed", out _));
        Assert.True(resp.TryGetProperty("isStub", out _));
    }

    [Fact]
    public async Task Stream_delta_content_reassembles_to_full_answer()
    {
        var response = await Client.PostAsJsonAsync("/api/copilot/stream",
            new { query = "What insurance does patient-0001 have?", threadId = "stream-reassemble-1" });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var events = ParseSseEvents(body);

        // Reassemble deltas
        var deltas = events
            .Where(e => e.EventType == "delta")
            .Select(e =>
            {
                var doc = JsonDocument.Parse(e.Data);
                return doc.RootElement.GetProperty("content").GetString() ?? "";
            });
        var reassembled = string.Concat(deltas);

        // Get answer from done event
        var done = events.Last(e => e.EventType == "done");
        var doneDoc = JsonDocument.Parse(done.Data);
        var fullAnswer = doneDoc.RootElement.GetProperty("response").GetProperty("answer").GetString();

        Assert.Equal(fullAnswer, reassembled);
    }

    private static List<SseEvent> ParseSseEvents(string raw)
    {
        // SSE frames are separated by double-newlines.
        // Each frame has "event: <type>\ndata: <json>" where the JSON may be
        // indented (multi-line) since the server uses WriteIndented = true.
        var events = new List<SseEvent>();
        var frames = raw.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        foreach (var frame in frames)
        {
            string? eventType = null;
            string? data = null;

            var eventPrefix = "event: ";
            var dataPrefix = "data: ";

            var eventIdx = frame.IndexOf(eventPrefix, StringComparison.Ordinal);
            var dataIdx = frame.IndexOf(dataPrefix, StringComparison.Ordinal);

            if (eventIdx >= 0)
            {
                var endOfLine = frame.IndexOf('\n', eventIdx);
                eventType = endOfLine >= 0
                    ? frame[(eventIdx + eventPrefix.Length)..endOfLine].Trim()
                    : frame[(eventIdx + eventPrefix.Length)..].Trim();
            }

            if (dataIdx >= 0)
            {
                data = frame[(dataIdx + dataPrefix.Length)..].Trim();
            }

            if (eventType is not null && data is not null)
            {
                events.Add(new SseEvent(eventType, data));
            }
        }

        return events;
    }

    private sealed record SseEvent(string EventType, string Data);
}
