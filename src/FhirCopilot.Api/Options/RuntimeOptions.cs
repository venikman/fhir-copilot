namespace FhirCopilot.Api.Options;

public sealed class RuntimeOptions
{
    public string AgentProfilesPath { get; set; } = "config/agents";
}

public sealed class ProviderOptions
{
    public string Mode { get; set; } = "Gemini";
    public string? GeminiModel { get; set; } = "gemini-3-flash-preview";
    public List<string>? GeminiModels { get; set; }
    public string? FhirBaseUrl { get; set; }
    public string? LocalEndpoint { get; set; } = "http://localhost:1234";
    public string? LocalModel { get; set; } = "zai-org/glm-4.7-flash";

    public bool IsLocalMode =>
        string.Equals(Mode, "Local", StringComparison.OrdinalIgnoreCase);

    private readonly Lazy<string?> _geminiApiKey = new(() => Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    public string? GeminiApiKey => _geminiApiKey.Value;

    public bool IsGeminiMode =>
        string.Equals(Mode, "Gemini", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GeminiApiKey);

    public bool HasFhirBaseUrl => !string.IsNullOrWhiteSpace(FhirBaseUrl);

    private const string DefaultModel = "gemini-3-flash-preview";

    public IReadOnlyList<string> GetModelChain() =>
        GeminiModels is { Count: > 0 } ? GeminiModels : [GeminiModel ?? DefaultModel];
}
