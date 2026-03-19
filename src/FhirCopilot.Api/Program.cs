using System.Text.Json;
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

if (!providerConfig.HasFhirBaseUrl)
{
    throw new InvalidOperationException(
        "Provider:FhirBaseUrl is required. Set it to a FHIR R4 server URL (e.g., https://bulk-fhir.fly.dev/fhir).");
}

builder.Services.AddHttpClient("FhirApi", client =>
{
    client.BaseAddress = new Uri(providerConfig.FhirBaseUrl!.TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("Accept", "application/fhir+json");
});
builder.Services.AddSingleton<IFhirBackend>(sp =>
    new HttpFhirBackend(
        sp.GetRequiredService<IHttpClientFactory>(),
        sp.GetRequiredService<ILogger<HttpFhirBackend>>()));

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

app.MapGet("/", () => Results.Text("FHIR Copilot Agent Framework Starter is running. Connect via SignalR at /hubs/copilot. See /health."));

app.Run();

// Make the implicit Program class visible to WebApplicationFactory in integration tests.
public partial class Program { }
