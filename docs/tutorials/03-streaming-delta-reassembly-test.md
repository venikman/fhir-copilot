# Streaming Delta Reassembly: Proving SignalR StreamQuery Chunks Exactly Reconstruct the Final Answer

Most streaming API test suites stop at "did I get events?" This codebase goes further: it proves that concatenating every `delta` chunk produces a string identical to the final `answer` in the `done` event. That single assertion catches an entire class of bugs that manual testing will never find.

This tutorial walks through how it works.

---

## 1. The Problem

SignalR streaming looks simple on the surface. The server chops a response into pieces, sends them one at a time over a WebSocket, and the client glues them back together. What could go wrong?

Quite a lot:

- **Off-by-one errors in chunking.** If the chunk boundary calculation is wrong by a single character, the reassembled text will have a missing or duplicated character somewhere in the middle. You will not notice this by reading the stream in a browser.
- **Encoding issues.** Multi-byte UTF-8 characters split across chunk boundaries can produce garbled output. The server might inadvertently re-encode text, doubling up escape sequences.
- **Whitespace trimming.** It is tempting to `.Trim()` chunks for "cleanliness." If a chunk legitimately ends with a space (because the word boundary fell there), trimming silently corrupts the output.
- **Race conditions between delta emission and done event assembly.** If the done event's answer is computed from a different code path than the deltas, they can drift apart silently.
- **Serialization asymmetry.** The delta events carry raw text fragments. The done event carries a structured object that goes through JSON serialization. If the `answer` field is processed differently (HTML-escaped, newline-normalized, trimmed) before being placed into the done payload, the two will not match.

Most projects test "I received some delta events" and call it a day. The test in this codebase instead asserts the **reassembly invariant**: the concatenation of all delta content must exactly equal the answer in the done event, character for character.

---

## 2. The Test: `StreamQuery_deltas_reassemble_to_answer`

Here is the test, found in `tests/FhirCopilot.Api.Tests/SignalRHubTests.cs`:

```csharp
[Fact(Timeout = 60_000)]
public async Task StreamQuery_deltas_reassemble_to_answer()
{
    await _hub.StartAsync();

    var events = await CollectStreamEventsAsync(
        new CopilotRequest("What insurance does patient-0001 have?", "stream-reassemble-1"));

    var reassembled = string.Concat(
        events.Where(e => e.Type == "delta").Select(e => e.Content ?? ""));

    var done = events.Last(e => e.Type == "done");
    Assert.NotNull(done.Response);
    Assert.Equal(done.Response.Answer, reassembled);
}
```

The `CollectStreamEventsAsync` helper drives SignalR's built-in `IAsyncEnumerable` streaming:

```csharp
private async Task<List<CopilotStreamEvent>> CollectStreamEventsAsync(CopilotRequest request)
{
    var events = new List<CopilotStreamEvent>();

    await foreach (var evt in _hub.StreamAsync<CopilotStreamEvent>("StreamQuery", request))
    {
        events.Add(evt);
    }

    return events;
}
```

Walk through it step by step:

**Step 1: Connect and invoke `StreamQuery`.** The test starts a SignalR `HubConnection` against an in-memory `WebApplicationFactory` server. It calls `_hub.StreamAsync<CopilotStreamEvent>("StreamQuery", request)`, which opens a SignalR streaming channel to `CopilotHub.StreamQuery`.

**Step 2: Collect all stream events.** The `await foreach` loop collects every `CopilotStreamEvent` yielded by the server until the stream completes. Because this runs against `WebApplicationFactory`, the full exchange completes without a real network socket.

**Step 3: Extract and concatenate all delta content.** For every event with `Type == "delta"`, the test reads the `Content` field directly from the strongly-typed `CopilotStreamEvent` record. It then concatenates all of them with `string.Concat` — no separators, no trimming, no normalization.

**Step 4: Extract the full answer from the done event.** The last event of type `"done"` carries a `CopilotStreamEvent` with a populated `Response` field — the complete `CopilotResponse` with the authoritative final text.

**Step 5: Assert exact equality.** `Assert.Equal(done.Response.Answer, reassembled)` — this is the heart of the test. If any character is missing, duplicated, trimmed, or reordered, this fails. There is no fuzzy matching, no "close enough."

---

## 3. The SignalR Streaming Implementation

The streaming hub method lives in `src/FhirCopilot.Api/Hubs/CopilotHub.cs`:

```csharp
public async IAsyncEnumerable<CopilotStreamEvent> StreamQuery(
    CopilotRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    _logger.LogInformation("StreamQuery from connection {ConnectionId}, query length {Length}",
        Context.ConnectionId, request.Query.Length);

    var enumerator = _copilot.StreamAsync(request, cancellationToken)
        .GetAsyncEnumerator(cancellationToken);

    try
    {
        while (true)
        {
            bool moved;
            try
            {
                moved = await enumerator.MoveNextAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or System.ClientModel.ClientResultException)
            {
                throw new HubException($"upstream_error: {ex.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HubException("timeout: The request timed out. Please retry.");
            }
            catch (ArgumentException ex)
            {
                throw new HubException($"invalid_request: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unexpected error in StreamQuery");
                throw new HubException("internal_error: An unexpected error occurred.");
            }

            if (!moved) break;
            yield return enumerator.Current;
        }
    }
    finally
    {
        await enumerator.DisposeAsync();
    }
}
```

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

**`delta`** events carry text chunks in their `Content` field. Each delta is a fragment of the final answer. The client appends them in order.

**`done`** arrives last and carries the complete `CopilotResponse` — the full answer text, citations, reasoning chain, tools used, confidence, and metadata. This is the authoritative final state. The client can replace whatever it accumulated from deltas with this canonical version.

### Error handling in the streaming path

Unlike SSE — where the server cannot change the HTTP status code once streaming begins — SignalR streams can signal errors cleanly by throwing a `HubException`. The client receives the error message without needing an in-band error event type. The `[EnumeratorCancellation]` parameter ensures that when the SignalR connection is cancelled, the underlying `ICopilotService.StreamAsync` is also cancelled.

The `try/catch` inside the `while (true)` loop is deliberate: it catches exceptions from `MoveNextAsync` (where the actual LLM or FHIR call happens) and wraps them in typed `HubException` messages before they propagate to the client.

---

## 5. The Chunking Strategy

The live runners (`GeminiAgentFrameworkRunner`, `OpenAiCompatibleAgentRunner`) emit real model tokens as they arrive from the LLM. Each token becomes a `delta` event. The chunking is inherent to the LLM's token stream — there is no artificial splitting.

For testing, the `AgentRunnerBase` streams the agent's answer in fixed-size chunks. Here is the core pattern:

```csharp
foreach (var chunk in Chunk(plan.Answer, 120))
{
    cancellationToken.ThrowIfCancellationRequested();
    yield return CopilotStreamEvent.Delta(chunk);
}

yield return CopilotStreamEvent.Done(new CopilotResponse(
    plan.Answer,
    // ...
));
```

The `Chunk` method is a simple fixed-size splitter:

```csharp
private static IEnumerable<string> Chunk(string text, int size)
{
    for (var index = 0; index < text.Length; index += size)
        yield return text.Substring(index, Math.Min(size, text.Length - index));
}
```

It splits text into 120-character pieces. The last chunk gets whatever remains. There is no attempt to split on word boundaries — this is deliberate. Splitting mid-word and mid-line is more realistic and more likely to expose bugs in consumer code that assumes chunks are "clean."

### Why the reassembly test works

Because the runner uses `plan.Answer` as both the source for `Chunk()` and the `Answer` field of the `CopilotResponse` in the done event, the reassembly invariant holds by construction. But the test still has value: it verifies that no layer between the runner and the test client corrupts the data. The SignalR framing, JSON serialization, WebSocket transport, and the `CollectStreamEventsAsync` helper all sit in that path. A bug in any of those layers would break reassembly.

---

## 6. The Full Test Suite

The `SignalRHubTests` class contains six tests. Each one targets a specific aspect of the SignalR streaming contract:

### `Hub_connects_and_disconnects`

```csharp
await _hub.StartAsync();
Assert.Equal(HubConnectionState.Connected, _hub.State);
await _hub.StopAsync();
Assert.Equal(HubConnectionState.Disconnected, _hub.State);
```

Verifies the hub can be connected to and cleanly disconnected. A failure here means the SignalR route is not registered or the server is not reachable.

### `SendQuery_returns_response_from_real_llm`

```csharp
var response = await _hub.InvokeAsync<CopilotResponse>(
    "SendQuery",
    new CopilotRequest("How many patients are in the panel?", "send-query-1"));

Assert.NotNull(response);
Assert.False(string.IsNullOrWhiteSpace(response.AgentUsed));
Assert.Equal("send-query-1", response.ThreadId);
```

Verifies the one-shot `SendQuery` method returns a complete `CopilotResponse` with the correct thread ID echoed back.

### `StreamQuery_emits_meta_delta_done`

```csharp
Assert.True(events.Count >= 2, $"Expected at least 2 events (meta + done), got {events.Count}");
Assert.Equal("meta", events[0].Type);
Assert.Equal("done", events[^1].Type);
if (events.Count >= 3)
    Assert.Contains(events, e => e.Type == "delta");
```

Verifies the event ordering contract: meta first, at least one delta in the middle (when the LLM produces content), done last. This is the structural invariant that clients depend on for their state machines (show spinner → append text → finalize display).

### `StreamQuery_meta_has_agent_and_thread`

```csharp
var meta = events.First(e => e.Type == "meta");
Assert.False(string.IsNullOrWhiteSpace(meta.AgentType));
Assert.Equal("stream-meta-1", meta.ThreadId);
Assert.False(meta.IsStub);
```

Verifies the meta event carries the agent name and echoes back the correct thread ID. Because `SignalRHubTests` uses a real LLM runner, `IsStub` is `false`.

### `StreamQuery_deltas_reassemble_to_answer`

The star of this tutorial. Covered in detail in Section 2 above.

### `StreamQuery_preserves_threadId`

```csharp
var events1 = await CollectStreamEventsAsync(new CopilotRequest("How many patients?", threadId));
Assert.Equal(threadId, events1.First(e => e.Type == "meta").ThreadId);

var events2 = await CollectStreamEventsAsync(new CopilotRequest("Tell me more about the first one", threadId));
Assert.Equal(threadId, events2.First(e => e.Type == "meta").ThreadId);
```

Verifies that the same `threadId` is preserved across multiple requests on the same connection, enabling multi-turn conversation state.

---

## 7. How to Apply This

If you are building a streaming API — whether SignalR, WebSocket, or chunked transfer encoding — add a reassembly test. Here is the pattern:

1. **Send a request that produces a deterministic response.** Use a stub or seed data so the expected output is known.
2. **Capture all incremental events.** Collect them into a list via `await foreach` — SignalR's `IAsyncEnumerable` streaming makes this straightforward.
3. **Concatenate the incremental content.** Use `string.Concat` or equivalent — no separators, no trimming, no normalization.
4. **Extract the final/authoritative answer** from whatever "completion" signal your protocol uses (the `done` event in this case).
5. **Assert exact equality** between the concatenation and the final answer.

This catches bugs that are invisible in manual testing:

- A trailing newline that gets trimmed by the chunker but preserved in the final answer.
- An off-by-one in the chunk size calculation that drops the last character.
- A JSON serializer that escapes characters differently in a string field vs. a standalone value.
- A race condition where the done event is assembled before the last delta is emitted, causing one version to have stale content.

The cost of this test is minimal — it fails loudly with a diff showing exactly which characters diverged. The class of bugs it prevents would otherwise surface as garbled text in production, reported by users weeks later, and nearly impossible to reproduce.

Write the test before you write the chunking code, and you will never ship a streaming endpoint that corrupts its own output.
