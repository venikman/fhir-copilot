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
        "search_observations", "search_medications", "search_procedures",
        "search_allergies", "calculator"
    };

    public static IReadOnlyList<AIFunction> BuildTools(FhirToolbox toolbox, IEnumerable<string> allowedToolNames)
    {
        var all = new Dictionary<string, AIFunction>(StringComparer.OrdinalIgnoreCase)
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
            ["search_procedures"] = AIFunctionFactory.Create(toolbox.SearchProcedures),
            ["search_allergies"] = AIFunctionFactory.Create(toolbox.SearchAllergies),
            ["calculator"] = AIFunctionFactory.Create(toolbox.Calculator)
        };

        var selected = new List<AIFunction>();

        foreach (var toolName in allowedToolNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (all.TryGetValue(toolName, out var function))
            {
                selected.Add(function);
            }
        }

        return selected;
    }

    /// <summary>
    /// Validates that all AllowedTools in agent profiles reference known tool names.
    /// Call at startup to catch config typos early.
    /// </summary>
    public static void ValidateProfiles(IAgentProfileStore profileStore, ILogger logger)
    {
        foreach (var (agentName, profile) in profileStore.GetAllAgents())
        {
            foreach (var toolName in profile.AllowedTools)
            {
                if (!KnownToolNames.Contains(toolName))
                {
                    logger.LogWarning("Agent profile '{AgentName}' references unknown tool '{ToolName}' — this tool will be silently ignored at runtime", agentName, toolName);
                }
            }
        }
    }
}
