# Streaming Delta Reassembly: Proving SSE Chunks Exactly Reconstruct the Final Answer

Most streaming API test suites stop at "did I get events?" This codebase goes further: it proves that concatenating every `delta` chunk produces a string identical to the final `answer` in the `done` event. That single assertion catches an entire class of bugs that manual testing will never find.

This tutorial walks through how it works.

---

## 1. The Problem

Server-Sent Events (SSE) streaming looks simple on the surface. The server chops a response into pieces, sends them one at a time, and the client glues them back together. What could go wrong?

Quite a lot:

- **Off-by-one errors in chunking.** If the chunk boundary calculation is wrong by a single character, the reassembled text will have a missing or duplicated character somewhere in the middle. You will not notice this by reading the stream in a browser.
- **Encoding issues.** Multi-byte UTF-8 characters split across chunk boundaries can produce garbled output. The server might inadvertently re-encode text, doubling up escape sequences.
- **Whitespace trimming.** It is tempting to `.Trim()` chunks for "cleanliness." If a chunk legitimately ends with a space (because the word boundary fell there), trimming silently corrupts the output.
- **Race conditions between delta emission and done event assembly.** If the done event's answer is computed from a different code path than the deltas, they can drift apart silently.
- **Serialization asymmetry.** The delta events carry raw text fragments. The done event carries a structured object that goes through JSON serialization. If the `answer` field is processed differently (HTML-escaped, newline-normalized, trimmed) before being placed into the done payload, the two will not match.

Most projects test "I received some delta events" and call it a day. The test in this codebase instead asserts the **reassembly invariant**: the concatenation of all delta content must exactly equal the answer in the done event, character for character.

---

## 2. The Test: `Stream_delta_content_reassembles_to_full_answer`

Here is the test, found in `tests/FhirCopilot.Api.Tests/StreamingTests.cs`:

```csharp
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
```

Walk through it step by step:

**Step 1: Send a query via the streaming endpoint.** The test POSTs to `/api/copilot/stream` with a natural-language question about a specific patient. The `threadId` is hardcoded per test to avoid cross-test interference.

**Step 2: Read the entire SSE response body as a string.** Because this runs against `WebApplicationFactory` (an in-memory test server), the response completes fully before `ReadAsStringAsync` returns. In production, a real client would consume the stream incrementally.

**Step 3: Parse all SSE frames.** The custom `ParseSseEvents` helper (covered in Section 4) splits the raw response into structured `SseEvent` records, each with an `EventType` and `Data` field.

**Step 4: Extract and concatenate all delta content.** For every event with type `"delta"`, the test parses its JSON data and pulls out the `content` field. It then concatenates all of them with `string.Concat` -- no separators, no trimming, no normalization.

**Step 5: Extract the full answer from the done event.** The last event of type `"done"` contains a complete `CopilotResponse` object nested under `response`. The test navigates into `response.answer` to get the authoritative final text.

**Step 6: Assert exact equality.** `Assert.Equal(fullAnswer, reassembled)` -- this is the heart of the test. If any character is missing, duplicated, trimmed, or reordered, this fails. There is no fuzzy matching, no "close enough."

---

## 3. The SSE Protocol Implementation

The streaming endpoint lives in `src/FhirCopilot.Api/Program.cs`. Here is the full handler:

```csharp
app.MapPost("/api/copilot/stream", async (
    HttpContext httpContext,
    CopilotRequest request,
    ICopilotService service,
    CancellationToken cancellationToken) =>
{
    httpContext.Response.StatusCode = StatusCodes.Status200OK;
    httpContext.Response.Headers.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers["X-Accel-Buffering"] = "no";

    try
    {
        await foreach (var evt in service.StreamAsync(request, cancellationToken))
        {
            var payload = JsonSerializer.Serialize(evt, JsonDefaults.Serializer);
            await httpContext.Response.WriteAsync($"event: {evt.Type}\ndata: {payload}\n\n", cancellationToken);
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Client disconnected -- no action needed.
    }
    catch (Exception ex)
    {
        var errorEvt = CopilotStreamEvent.Error(ex.Message);
        var errorPayload = JsonSerializer.Serialize(errorEvt, JsonDefaults.Serializer);
        await httpContext.Response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", CancellationToken.None);
        await httpContext.Response.Body.FlushAsync(CancellationToken.None);
    }
});
```

### The SSE headers

Three headers are set before any content is written:

- **`Content-Type: text/event-stream`** -- This tells the browser (and any `EventSource` client) to treat the response as an SSE stream rather than buffering it as a regular HTTP response.
- **`Cache-Control: no-cache`** -- Prevents proxies and CDNs from caching the response, which would defeat the purpose of real-time streaming.
- **`X-Accel-Buffering: no`** -- This is an Nginx directive. When running behind Nginx as a reverse proxy, Nginx buffers responses by default. This header tells it to pass bytes through immediately.

### The event lifecycle

The stream produces four types of events, defined in `src/FhirCopilot.Api/Contracts/CopilotContracts.cs`:

```csharp
public sealed record CopilotStreamEvent(
    string Type,
    string? AgentType = null,
    string? ThreadId = null,
    string? Content = null,
    CopilotResponse? Response = null,
    string? Message = null,
    bool IsStub = false)
{
    public static CopilotStreamEvent Meta(string agentType, string threadId, bool isStub)
        => new("meta", AgentType: agentType, ThreadId: threadId, IsStub: isStub);

    public static CopilotStreamEvent Delta(string content)
        => new("delta", Content: content);

    public static CopilotStreamEvent Done(CopilotResponse response)
        => new("done", Response: response);

    public static CopilotStreamEvent Error(string message)
        => new("error", Message: message);
}
```

**`meta`** arrives first. It tells the client which agent was selected, what thread ID is in play, and whether this is a stub response. A UI client can use this to display "Thinking..." with the agent name before any content arrives.

**`delta`** events carry text chunks in their `content` field. Each delta is a fragment of the final answer. The client appends them in order.

**`done`** arrives last and carries the complete `CopilotResponse` -- the full answer text, citations, reasoning chain, tools used, confidence, and metadata. This is the authoritative final state. The client can replace whatever it accumulated from deltas with this canonical version.

**`error`** is emitted if an exception occurs after headers have already been sent. At that point, the server cannot change the HTTP status code (it is already 200), so it signals the error in-band as an SSE event. Note how the `catch` block uses `CancellationToken.None` -- if the error happened because of a server-side failure (not a client disconnect), the server still needs to flush the error event.

### The write-and-flush pattern

Each event is written and then explicitly flushed:

```csharp
await httpContext.Response.WriteAsync($"event: {evt.Type}\ndata: {payload}\n\n", cancellationToken);
await httpContext.Response.Body.FlushAsync(cancellationToken);
```

Without the flush, ASP.NET Core may buffer the output, and the client would receive events in unpredictable batches rather than one at a time.

---

## 4. The `ParseSseEvents` Helper

The test file includes a custom SSE parser. This is worth examining closely because it handles a subtlety that most SSE parsers miss:

```csharp
private static List<SseEvent> ParseSseEvents(string raw)
{
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
```

### The multi-line JSON subtlety

The server serializes events using `JsonDefaults.Serializer`, which has `WriteIndented = true`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    // ...
};
```

This means the JSON payload for a `done` event is not a single line. It looks something like:

```
event: done
data: {
  "type": "done",
  "response": {
    "answer": "...",
    "citations": [ ... ]
  }
}
```

Per the SSE specification (RFC 8895 / W3C EventSource), each line of a multi-line data field should start with `data:`. In other words, a strictly compliant server would emit:

```
event: done
data: {
data:   "type": "done",
data:   "response": {
data:     "answer": "..."
data:   }
data: }
```

This server does not do that -- it writes the entire indented JSON block after a single `data: ` prefix. The parser handles this by taking everything from `data: ` to the end of the frame (the next `\n\n` boundary):

```csharp
if (dataIdx >= 0)
{
    data = frame[(dataIdx + dataPrefix.Length)..].Trim();
}
```

This is technically non-compliant SSE, but it works in practice because:
1. The test's custom parser expects this format.
2. Browser `EventSource` APIs would not parse it correctly, but this endpoint is consumed by JavaScript `fetch` + manual parsing, not by `EventSource`.
3. The `\n\n` double-newline frame separator is unambiguous -- the indented JSON will never contain a double-newline within a value (JSON encodes newlines as `\n` inside strings).

---

## 5. The Chunking Strategy

The `StubAgentRunner` in `src/FhirCopilot.Api/Services/StubAgentRunner.cs` shows how text gets split into delta events:

```csharp
public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
    AgentProfile profile,
    string query,
    string threadId,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
{
    var plan = await ExecuteAsync(profile.Name, query, cancellationToken);

    yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: true);

    foreach (var chunk in Chunk(plan.Answer, 120))
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return CopilotStreamEvent.Delta(chunk);
        await Task.Delay(30, cancellationToken);
    }

    yield return CopilotStreamEvent.Done(new CopilotResponse(
        plan.Answer,
        plan.Citations,
        plan.Reasoning,
        plan.ToolsUsed,
        profile.Name,
        plan.Confidence,
        threadId,
        IsStub: true));
}
```

Three things to note here:

**First**, the full answer is computed before any streaming begins (`ExecuteAsync` runs first). The deltas are synthetic -- they simulate streaming by chopping the already-known answer into pieces. This is the stub implementation used for testing and development. The live runner (`GeminiAgentFrameworkRunner`) emits real model tokens as they arrive from the LLM.

**Second**, the `Chunk` method is a simple fixed-size splitter:

```csharp
private static IEnumerable<string> Chunk(string text, int size)
{
    for (var index = 0; index < text.Length; index += size)
        yield return text.Substring(index, Math.Min(size, text.Length - index));
}
```

It splits text into 120-character pieces. The last chunk gets whatever remains. There is no attempt to split on word boundaries -- this is deliberate. Splitting mid-word and mid-line is more realistic and more likely to expose bugs in consumer code that assumes chunks are "clean."

**Third**, each chunk is followed by `Task.Delay(30, cancellationToken)` -- a 30-millisecond pause. This simulates the latency of token-by-token generation from an LLM. Without this delay, all chunks would arrive in a single TCP segment and the test would not exercise the streaming path realistically.

### Why the reassembly test works

Because `StreamAsync` uses `plan.Answer` as both the source for `Chunk()` and the `Answer` field of the `CopilotResponse` in the done event, the reassembly invariant holds by construction in the stub. But the test still has value: it verifies that no layer between the `StubAgentRunner` and the test client corrupts the data. The SSE framing, JSON serialization, HTTP transport, response buffering, and the test's own SSE parser all sit in that path. A bug in any of those layers would break reassembly.

---

## 6. The Full Test Suite

The `StreamingTests` class contains five tests. Each one targets a specific aspect of the SSE contract:

### `Stream_returns_sse_content_type`

```csharp
Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
```

Verifies the response has the correct MIME type. Without this, browser-based SSE clients will reject the stream entirely.

### `Stream_emits_meta_delta_done_events`

```csharp
Assert.Equal("meta", events[0].EventType);
Assert.Contains(events, e => e.EventType == "delta");
Assert.Equal("done", events[^1].EventType);
```

Verifies the event ordering contract: meta first, at least one delta in the middle, done last. This is the structural invariant that clients depend on for their state machines (show spinner -> append text -> finalize display).

### `Stream_meta_event_contains_agent_and_thread`

```csharp
Assert.True(doc.RootElement.TryGetProperty("agentType", out var agentType));
Assert.False(string.IsNullOrWhiteSpace(agentType.GetString()));
Assert.True(doc.RootElement.TryGetProperty("threadId", out var threadId));
Assert.Equal("stream-meta-1", threadId.GetString());
Assert.True(doc.RootElement.TryGetProperty("isStub", out var isStub));
Assert.True(isStub.GetBoolean());
```

Verifies the meta event carries the agent name, echoes back the correct thread ID, and reports the stub flag. This catches serialization mistakes where fields are omitted by the JSON options (e.g., camelCase policy turning `IsStub` into `isStub` but a typo sending `isstub`).

### `Stream_done_event_contains_full_response`

```csharp
Assert.True(resp.TryGetProperty("answer", out _));
Assert.True(resp.TryGetProperty("citations", out _));
Assert.True(resp.TryGetProperty("agentUsed", out _));
Assert.True(resp.TryGetProperty("isStub", out _));
```

Verifies the done event contains a complete response with all required fields. This is a structural check -- it does not assert specific values, just that the shape is correct. It catches cases where a new field was added to `CopilotResponse` but the serializer skips it (e.g., because it is `null` and `DefaultIgnoreCondition` is set).

### `Stream_delta_content_reassembles_to_full_answer`

The star of this tutorial. Covered in detail in Section 2 above.

---

## 7. How to Apply This

If you are building a streaming API -- whether SSE, WebSocket, or chunked transfer encoding -- add a reassembly test. Here is the pattern:

1. **Send a request that produces a deterministic response.** Use a stub or seed data so the expected output is known.
2. **Capture all incremental events.** Parse them into structured objects, not just raw strings.
3. **Concatenate the incremental content.** Use `string.Concat` or equivalent -- no separators, no trimming, no normalization.
4. **Extract the final/authoritative answer** from whatever "completion" signal your protocol uses.
5. **Assert exact equality** between the concatenation and the final answer.

This catches bugs that are invisible in manual testing:

- A trailing newline that gets trimmed by the chunker but preserved in the final answer.
- An off-by-one in the chunk size calculation that drops the last character.
- A JSON serializer that escapes characters differently in a string field vs. a standalone value.
- A race condition where the done event is assembled before the last delta is emitted, causing one version to have stale content.

The cost of this test is minimal -- it runs in milliseconds, requires no external dependencies, and fails loudly with a diff showing exactly which characters diverged. The class of bugs it prevents would otherwise surface as garbled text in production, reported by users weeks later, and nearly impossible to reproduce.

Write the test before you write the chunking code, and you will never ship a streaming endpoint that corrupts its own output.
