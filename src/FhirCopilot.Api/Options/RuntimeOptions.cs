namespace FhirCopilot.Api.Options;

public sealed class RuntimeOptions
{
    public string AgentProfilesPath { get; set; } = "config/agents";
    public bool UseStubWhenProviderMissing { get; set; } = true;
}

public sealed class ProviderOptions
{
    public string Mode { get; set; } = "Stub";
    public string? GeminiModel { get; set; } = "gemini-3.1-flash";
    public string? FhirBaseUrl { get; set; }

    private readonly Lazy<string?> _geminiApiKey = new(() => Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    public string? GeminiApiKey => _geminiApiKey.Value;

    public bool IsGeminiMode =>
        string.Equals(Mode, "Gemini", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GeminiApiKey);

    public bool HasFhirBaseUrl => !string.IsNullOrWhiteSpace(FhirBaseUrl);
}
