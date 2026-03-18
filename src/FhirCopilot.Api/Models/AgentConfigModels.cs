namespace FhirCopilot.Api.Models;

public sealed class AgentProfile
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public string PreferredApi { get; init; } = "ChatCompletion";
    public string[] AllowedTools { get; init; } = Array.Empty<string>();
    public string[] Instructions { get; init; } = Array.Empty<string>();
    public string[] DomainContext { get; init; } = Array.Empty<string>();
    public ResponseContract ResponseContract { get; init; } = new();
}

public sealed class ResponseContract
{
    public bool AnswerFirst { get; init; } = true;
    public bool IncludeCitations { get; init; } = true;
    public bool IncludeReasoningSummary { get; init; } = true;
    public string[] StructuredSections { get; init; } = Array.Empty<string>();
}

public sealed class RouterProfile
{
    public string Name { get; init; } = "router";
    public string FallbackAgent { get; init; } = "clinical";
    public Dictionary<string, string[]> KeywordHints { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class AgentTypes
{
    public const string Lookup = "lookup";
    public const string Search = "search";
    public const string Analytics = "analytics";
    public const string Clinical = "clinical";
    public const string Cohort = "cohort";
    public const string Export = "export";

    public static readonly IReadOnlyList<string> All =
    [
        Lookup,
        Search,
        Analytics,
        Clinical,
        Cohort,
        Export
    ];
}
