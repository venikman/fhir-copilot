using FhirCopilot.Api.Fhir;
using Microsoft.Extensions.AI;

namespace FhirCopilot.Api.Services;

public static class ToolRegistry
{
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
}
