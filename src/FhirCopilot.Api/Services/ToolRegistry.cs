using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;
using Microsoft.Extensions.AI;

namespace FhirCopilot.Api.Services;

public static class ToolRegistry
{
    private static readonly HashSet<string> KnownToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "search_groups", "read_resource", "list_resources", "bulk_export",
        "search_patients", "search_encounters", "search_conditions",
        "search_observations", "search_medications",
        "search_allergies", "calculator"
    };

    public static IReadOnlyList<AIFunction> BuildTools(FhirToolbox toolbox, IEnumerable<string> allowedToolNames)
    {
        var all = BuildAllTools(toolbox);

        return allowedToolNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(all.ContainsKey)
            .Select(name => all[name])
            .ToList();
    }

    /// <summary>
    /// Validates that all AllowedTools in agent profiles reference known tool names.
    /// Call at startup to catch config typos early.
    /// </summary>
    public static void ValidateProfiles(IAgentProfileStore profileStore, ILogger logger)
    {
        foreach (var (agentName, profile) in profileStore.GetAllAgents())
        {
            foreach (var toolName in profile.AllowedTools.Where(t => !KnownToolNames.Contains(t)))
            {
                logger.LogWarning("Agent profile '{AgentName}' references unknown tool '{ToolName}' — this tool will be silently ignored at runtime", agentName, toolName);
            }
        }
    }

    private static Dictionary<string, AIFunction> BuildAllTools(FhirToolbox toolbox) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["search_groups"] = AIFunctionFactory.Create(toolbox.SearchGroups),
            ["read_resource"] = AIFunctionFactory.Create(toolbox.ReadResource),
            ["list_resources"] = AIFunctionFactory.Create(toolbox.ListResources),
            ["bulk_export"] = AIFunctionFactory.Create(toolbox.BulkExport),
            ["search_patients"] = AIFunctionFactory.Create(toolbox.SearchPatients),
            ["search_encounters"] = AIFunctionFactory.Create(toolbox.SearchEncounters),
            ["search_conditions"] = AIFunctionFactory.Create(toolbox.SearchConditions),
            ["search_observations"] = AIFunctionFactory.Create(toolbox.SearchObservations),
            ["search_medications"] = AIFunctionFactory.Create(toolbox.SearchMedications),
            ["search_allergies"] = AIFunctionFactory.Create(toolbox.SearchAllergies),
            ["calculator"] = AIFunctionFactory.Create(toolbox.Calculator)
        };
}
