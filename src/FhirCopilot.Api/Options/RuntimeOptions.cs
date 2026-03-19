namespace FhirCopilot.Api.Options;

public sealed class RuntimeOptions
{
    public string AgentProfilesPath { get; set; } = "config/agents";
}

public sealed class ProviderOptions
{
    public string Mode { get; set; } = "Gemini";
    public List<string>? GeminiModels { get; set; }
    public string? FhirBaseUrl { get; set; }
    public string? LocalEndpoint { get; set; } = "http://localhost:1234/v1";
    public string? LocalModel { get; set; } = "zai-org/glm-4.7-flash";

    public bool IsLocalMode =>
        string.Equals(Mode, "Local", StringComparison.OrdinalIgnoreCase);

    public string? GeminiApiKey { get; init; } = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

    public bool IsGeminiMode =>
        string.Equals(Mode, "Gemini", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GeminiApiKey);

    public bool HasFhirBaseUrl => !string.IsNullOrWhiteSpace(FhirBaseUrl);

    private static readonly string[] DefaultChain =
    [
        "gemini-3-flash-preview",
        "gemini-3.1-flash-lite-preview",
        "gemini-3.1-pro-preview",
        "gemini-2.5-flash",
        "gemini-2.5-pro",
        "gemini-2.0-flash"
    ];

    public IReadOnlyList<string> GetModelChain() =>
        GeminiModels is { Count: > 0 } ? GeminiModels : DefaultChain;
}
