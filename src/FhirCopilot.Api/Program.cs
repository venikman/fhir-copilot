using System.Text.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Options;
using FhirCopilot.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<RuntimeOptions>(builder.Configuration.GetSection("Runtime"));
builder.Services.Configure<ProviderOptions>(builder.Configuration.GetSection("Provider"));

builder.Services.AddSingleton<IAgentProfileStore, FileAgentProfileStore>();
builder.Services.AddSingleton<IIntentRouter, KeywordIntentRouter>();

var providerConfig = builder.Configuration.GetSection("Provider").Get<ProviderOptions>() ?? new ProviderOptions();
if (providerConfig.HasFhirBaseUrl)
{
    builder.Services.AddHttpClient("FhirApi", client =>
    {
        client.DefaultRequestHeaders.Add("Accept", "application/fhir+json");
    });
    builder.Services.AddSingleton<IFhirBackend>(sp =>
    {
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var logger = sp.GetRequiredService<ILogger<HttpFhirBackend>>();
        return new HttpFhirBackend(factory, providerConfig.FhirBaseUrl!, logger);
    });
}
else
{
    builder.Services.AddSingleton<IFhirBackend, SampleFhirBackend>();
}

builder.Services.AddSingleton<FhirToolbox>();
builder.Services.AddSingleton<StubAgentRunner>();
builder.Services.AddSingleton<GeminiAgentFrameworkRunner>();
builder.Services.AddSingleton<ICopilotService, CopilotService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

var app = builder.Build();

// Validate agent tool configs at startup to catch typos early
ToolRegistry.ValidateProfiles(
    app.Services.GetRequiredService<IAgentProfileStore>(),
    app.Services.GetRequiredService<ILogger<Program>>());

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Text("FHIR Copilot Agent Framework Starter is running. See /health or /api/copilot."));

app.MapPost("/api/copilot", async (CopilotRequest request, ICopilotService service, CancellationToken cancellationToken) =>
{
    var response = await service.RunAsync(request, cancellationToken);
    return Results.Ok(response);
});

// POST keeps clinical queries out of URLs, server logs, and browser history.
// FHIR R4 spec endorses POST-based search (POST [base]/[type]/_search) for the same reason.
// See: https://hl7.org/fhir/R4/search.html and https://hl7.org/fhir/security.html
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
            var payload = JsonSerializer.Serialize(evt, FhirCopilot.Api.Services.JsonDefaults.Serializer);
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
        var errorPayload = JsonSerializer.Serialize(errorEvt, FhirCopilot.Api.Services.JsonDefaults.Serializer);
        await httpContext.Response.WriteAsync($"event: error\ndata: {errorPayload}\n\n", CancellationToken.None);
        await httpContext.Response.Body.FlushAsync(CancellationToken.None);
    }
});

app.Run();

// Make the implicit Program class visible to WebApplicationFactory in integration tests.
public partial class Program { }
