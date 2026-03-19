namespace FhirCopilot.Api.Models;

public sealed record AgentProfile
{
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Instructions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DomainContext { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> StructuredSections { get; init; } = Array.Empty<string>();
}

public sealed record RouterProfile
{
    public string Name { get; init; } = "router";
    public string FallbackAgent { get; init; } = "clinical";
    public IReadOnlyDictionary<string, string[]> KeywordHints { get; init; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
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
