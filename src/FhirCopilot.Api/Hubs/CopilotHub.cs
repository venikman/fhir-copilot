using System.Runtime.CompilerServices;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace FhirCopilot.Api.Hubs;

public sealed class CopilotHub : Hub
{
    private readonly ICopilotService _copilot;
    private readonly ILogger<CopilotHub> _logger;

    public CopilotHub(ICopilotService copilot, ILogger<CopilotHub> logger)
    {
        _copilot = copilot;
        _logger = logger;
    }

    public async Task<CopilotResponse> SendQuery(CopilotRequest request)
    {
        var cancellationToken = Context.ConnectionAborted;

        _logger.LogInformation("SendQuery from connection {ConnectionId}, query length {Length}",
            Context.ConnectionId, request.Query.Length);

        try
        {
            return await _copilot.RunAsync(request, cancellationToken);
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
        catch (HubException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SendQuery");
            throw new HubException("internal_error: An unexpected error occurred.");
        }
    }

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
}
