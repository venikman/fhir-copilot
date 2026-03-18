using System.Text.Json;
using FhirCopilot.Api.Contracts;

namespace FhirCopilot.Api.Services;

public static class SseWriter
{
    public static async Task WriteAsync(
        HttpContext httpContext,
        IAsyncEnumerable<CopilotStreamEvent> events,
        CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status200OK;
        httpContext.Response.Headers.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var evt in events.WithCancellation(cancellationToken))
            {
                var payload = JsonSerializer.Serialize(evt, JsonDefaults.Serializer);
                await httpContext.Response.WriteAsync($"event: {evt.Type}\ndata: {payload}\n\n", cancellationToken);
                await httpContext.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — no action needed.
        }
        catch (Exception ex)
        {
            var errorEvt = CopilotStreamEvent.Error(ex.Message);
            var errorPayload = JsonSerializer.Serialize(errorEvt, JsonDefaults.Serializer);
            await httpContext.Response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", CancellationToken.None);
            await httpContext.Response.Body.FlushAsync(CancellationToken.None);
        }
    }
}
