using System.Text.Json;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Hubs;
using FhirCopilot.Api.Options;
using FhirCopilot.Api.Services;

// Load .env file if present (dev convenience — prod uses real env vars)
static void LoadEnvFile()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var envFile = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in File.ReadAllLines(envFile))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    continue;

                var sep = trimmed.IndexOf('=');
                if (sep <= 0)
                    continue;

                var key = trimmed[..sep].Trim();
                var value = trimmed[(sep + 1)..].Trim();
                Environment.SetEnvironmentVariable(key, value);
            }
            return;
        }
        dir = dir.Parent;
    }
}
LoadEnvFile();

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

if (providerConfig.IsGeminiMode)
{
    builder.Services.AddSingleton<IAgentRunner, GeminiAgentFrameworkRunner>();
}
else if (providerConfig.IsLocalMode)
{
    builder.Services.AddSingleton<IAgentRunner, OpenAiCompatibleAgentRunner>();
}
else
{
    throw new InvalidOperationException(
        $"Provider:Mode '{providerConfig.Mode}' is not supported. Use 'Gemini' or 'Local'.");
}

builder.Services.AddSingleton<ICopilotService, CopilotService>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.WriteIndented = true;
});

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PayloadSerializerOptions.WriteIndented = false;
    });

var app = builder.Build();

// Validate agent tool configs at startup to catch typos early
ToolRegistry.ValidateProfiles(
    app.Services.GetRequiredService<IAgentProfileStore>(),
    app.Services.GetRequiredService<ILogger<Program>>());

app.MapDefaultEndpoints();

app.MapHub<CopilotHub>("/hubs/copilot");

app.MapGet("/", () => Results.Text("FHIR Copilot Agent Framework Starter is running. See /health or /api/copilot."));

app.MapPost("/api/copilot", async (CopilotRequest request, ICopilotService service, CancellationToken cancellationToken) =>
{
    try
    {
        var response = await service.RunAsync(request, cancellationToken);
        return Results.Ok(response);
    }
    catch (HttpRequestException)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("upstream_error", "The AI service returned an error. Please retry.")),
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("timeout", "The request timed out. Please retry.")),
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("invalid_request", ex.Message)),
            statusCode: StatusCodes.Status400BadRequest);
    }
    catch (Exception)
    {
        return Results.Json(
            new CopilotErrorResponse(new CopilotError("internal_error", "An unexpected error occurred.")),
            statusCode: StatusCodes.Status500InternalServerError);
    }
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
    await SseWriter.WriteAsync(httpContext, service.StreamAsync(request, cancellationToken), cancellationToken);
});

app.Run();

// Make the implicit Program class visible to WebApplicationFactory in integration tests.
public partial class Program { }
