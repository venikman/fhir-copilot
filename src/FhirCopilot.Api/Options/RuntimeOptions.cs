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

    private readonly Lazy<string?> _geminiApiKey = new(() => Environment.GetEnvironmentVariable("GEMINI_API_KEY"));

    public string? GeminiApiKey => _geminiApiKey.Value;

    public bool IsGeminiMode =>
        string.Equals(Mode, "Gemini", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GeminiApiKey);

    public bool HasFhirBaseUrl => !string.IsNullOrWhiteSpace(FhirBaseUrl);

    public List<string> GetModelChain()
    {
        if (GeminiModels?.Count > 0)
        {
            return GeminiModels;
        }

        return string.IsNullOrWhiteSpace(GeminiModel)
            ? new List<string> { "gemini-3-flash-preview" }
            : new List<string> { GeminiModel };
    }
}
