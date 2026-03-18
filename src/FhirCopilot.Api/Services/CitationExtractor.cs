using System.Text.RegularExpressions;
using FhirCopilot.Api.Contracts;

namespace FhirCopilot.Api.Services;

public static class CitationExtractor
{
    private static readonly Regex CitationRegex = new(
        @"\b(?:Group|Patient|Encounter|Condition|Observation|MedicationRequest|Procedure|AllergyIntolerance)/[A-Za-z0-9\-_.]+\b",
        RegexOptions.Compiled);

    public static IReadOnlyList<Citation> Extract(string answer)
    {
        var citations = CitationRegex.Matches(answer)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(id => new Citation(id))
            .ToList();

        return citations;
    }
}
