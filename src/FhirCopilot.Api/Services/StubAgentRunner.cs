using System.Text;
using System.Text.RegularExpressions;
using FhirCopilot.Api.Contracts;
using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Services;

public sealed class StubAgentRunner
{
    private readonly IFhirBackend _backend;

    public StubAgentRunner(IFhirBackend backend)
    {
        _backend = backend;
    }

    public async Task<CopilotResponse> RunAsync(AgentProfile profile, string query, string threadId, CancellationToken cancellationToken)
    {
        var plan = await ExecuteAsync(profile.Name, query, cancellationToken);

        return new CopilotResponse(
            plan.Answer,
            plan.Citations,
            plan.Reasoning,
            plan.ToolsUsed,
            profile.Name,
            plan.Confidence,
            threadId,
            IsStub: true);
    }

    public async IAsyncEnumerable<CopilotStreamEvent> StreamAsync(
        AgentProfile profile,
        string query,
        string threadId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var plan = await ExecuteAsync(profile.Name, query, cancellationToken);

        yield return CopilotStreamEvent.Meta(profile.Name, threadId, isStub: true);

        foreach (var chunk in Chunk(plan.Answer, 120))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return CopilotStreamEvent.Delta(chunk);
            await Task.Delay(30, cancellationToken);
        }

        yield return CopilotStreamEvent.Done(new CopilotResponse(
            plan.Answer,
            plan.Citations,
            plan.Reasoning,
            plan.ToolsUsed,
            profile.Name,
            plan.Confidence,
            threadId,
            IsStub: true));
    }

    private Task<StubExecutionPlan> ExecuteAsync(string agentType, string query, CancellationToken ct)
    {
        return agentType switch
        {
            AgentTypes.Lookup => ExecuteLookupAsync(query, ct),
            AgentTypes.Search => ExecuteSearchAsync(query, ct),
            AgentTypes.Analytics => ExecuteAnalyticsAsync(query, ct),
            AgentTypes.Clinical => ExecuteClinicalAsync(query, ct),
            AgentTypes.Cohort => ExecuteCohortAsync(query, ct),
            AgentTypes.Export => ExecuteExportAsync(query, ct),
            _ => ExecuteClinicalAsync(query, ct)
        };
    }

    private async Task<StubExecutionPlan> ExecuteLookupAsync(string query, CancellationToken ct)
    {
        var patient = await FindPatientAsync(query, ct) ?? (await _backend.GetPatientsAsync(ct)).First();

        if (query.Contains("insurance", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("coverage", StringComparison.OrdinalIgnoreCase))
        {
            var coverageLabel = string.IsNullOrEmpty(patient.Coverage) ? "unknown coverage" : patient.Coverage;
            var answer = $"{patient.Name} is covered by {coverageLabel}. Evidence: Patient/{patient.Id}.";
            return new StubExecutionPlan(
                answer,
                ["Selected lookup agent.", $"Resolved patient from query to Patient/{patient.Id}.", "Returned coverage from patient record."],
                ["read_resource"],
                [new Citation($"Patient/{patient.Id}", patient.Name)],
                "high");
        }

        var groups = await _backend.GetGroupsAsync(ct);
        var group = groups.FirstOrDefault(g => g.Id == patient.GroupId) ?? groups.FirstOrDefault();
        var groupName = group?.Name ?? "unknown group";
        var groupId = group?.Id ?? "unknown";
        var generic = $"{patient.Name} is managed under {groupName}, primary clinician {patient.GeneralPractitioner}. Evidence: Patient/{patient.Id}, Group/{groupId}.";
        return new StubExecutionPlan(
            generic,
            ["Selected lookup agent.", $"Resolved patient from query to Patient/{patient.Id}.", "Joined patient and group records."],
            ["read_resource", "search_groups"],
            [new Citation($"Patient/{patient.Id}", patient.Name), new Citation($"Group/{groupId}", groupName)],
            "high");
    }

    private async Task<StubExecutionPlan> ExecuteSearchAsync(string query, CancellationToken ct)
    {
        if (query.Contains("encounter", StringComparison.OrdinalIgnoreCase))
        {
            var patient = await FindPatientAsync(query, ct) ?? (await _backend.GetPatientsAsync(ct)).First();
            var encounters = await _backend.SearchEncountersAsync(patient.Id, null, null, null, null, null, null, null, ct);
            var lines = encounters.Select(encounter => $"- Encounter/{encounter.Id}: {encounter.TypeDisplay} on {encounter.Date} with {encounter.Practitioner} at {encounter.Location}");
            var answer = $"Found {encounters.Count} encounters for {patient.Name}.\n" + string.Join("\n", lines);
            var citations = encounters.Select(encounter => new Citation($"Encounter/{encounter.Id}", encounter.TypeDisplay)).ToList<Citation>();
            citations.Add(new Citation($"Patient/{patient.Id}", patient.Name));

            return new StubExecutionPlan(
                answer,
                ["Selected search agent.", $"Resolved patient from query to Patient/{patient.Id}.", "Queried encounter index."],
                ["search_encounters", "read_resource"],
                citations,
                "high");
        }

        var results = await _backend.SearchPatientsAsync(ExtractNameHint(query), null, null, null, null, ct);
        if (results.Count == 0)
            results = await _backend.GetPatientsAsync(ct);

        var answerText = $"Found {results.Count} patient record(s):\n" +
                         string.Join("\n", results.Select(patient => $"- Patient/{patient.Id}: {patient.Name}, {patient.Gender}, birth year {patient.BirthYear}, PCP {patient.GeneralPractitioner}"));

        return new StubExecutionPlan(
            answerText,
            ["Selected search agent.", "Queried patient index."],
            ["search_patients"],
            results.Select(patient => new Citation($"Patient/{patient.Id}", patient.Name)).ToList<Citation>(),
            "high");
    }

    private async Task<StubExecutionPlan> ExecuteAnalyticsAsync(string query, CancellationToken ct)
    {
        if (query.Contains("diabet", StringComparison.OrdinalIgnoreCase))
        {
            var diabeticIds = await GetDiabeticPatientIdsAsync(ct);
            var allPatients = await _backend.GetPatientsAsync(ct);
            var patients = allPatients.Where(patient => diabeticIds.Contains(patient.Id)).ToList();
            var answer = $"There are {patients.Count} diabetic patient(s) in the panel: {string.Join(", ", patients.Select(patient => $"{patient.Name} (Patient/{patient.Id})"))}.";
            return new StubExecutionPlan(
                answer,
                ["Selected analytics agent.", "Counted unique patient ids for diabetes-coded conditions."],
                ["search_conditions", "calculator"],
                patients.Select(patient => new Citation($"Patient/{patient.Id}", patient.Name)).ToList<Citation>(),
                "high");
        }

        var allPats = await _backend.GetPatientsAsync(ct);
        return new StubExecutionPlan(
            $"There are {allPats.Count} patient(s) in the panel.",
            ["Selected analytics agent.", "Counted patient records."],
            ["search_patients", "calculator"],
            allPats.Select(patient => new Citation($"Patient/{patient.Id}", patient.Name)).ToList<Citation>(),
            "high");
    }

    private async Task<StubExecutionPlan> ExecuteClinicalAsync(string query, CancellationToken ct)
    {
        var patient = await FindPatientAsync(query, ct) ?? (await _backend.GetPatientsAsync(ct)).First();

        var conditionsTask = _backend.SearchConditionsAsync(patient.Id, null, null, null, ct);
        var medicationsTask = _backend.SearchMedicationsAsync(patient.Id, null, null, ct);
        var observationsTask = _backend.SearchObservationsAsync(patient.Id, null, null, null, null, ct);
        var encountersTask = _backend.SearchEncountersAsync(patient.Id, null, null, null, null, null, null, null, ct);
        var allergiesTask = _backend.SearchAllergiesAsync(patient.Id, ct);
        await Task.WhenAll(conditionsTask, medicationsTask, observationsTask, encountersTask, allergiesTask);

        var conditions = conditionsTask.Result;
        var medications = medicationsTask.Result;
        var observations = observationsTask.Result;
        var encounters = encountersTask.Result;
        var allergies = allergiesTask.Result;

        var builder = new StringBuilder();
        builder.AppendLine($"Clinical summary for {patient.Name} (Patient/{patient.Id})");
        builder.AppendLine();
        builder.AppendLine("1. Demographics");
        builder.AppendLine($"- Gender: {patient.Gender}");
        builder.AppendLine($"- Birth year: {patient.BirthYear}");
        if (!string.IsNullOrEmpty(patient.Coverage))
            builder.AppendLine($"- Coverage: {patient.Coverage}");
        builder.AppendLine($"- PCP: {patient.GeneralPractitioner}");
        builder.AppendLine();
        builder.AppendLine("2. Conditions");
        foreach (var condition in conditions)
            builder.AppendLine($"- {condition.Display} ({condition.Code}) — Condition/{condition.Id}");
        builder.AppendLine();
        builder.AppendLine("3. Medications");
        foreach (var medication in medications)
            builder.AppendLine($"- {medication.Display} — MedicationRequest/{medication.Id}");
        builder.AppendLine();
        builder.AppendLine("4. Observations");
        foreach (var observation in observations)
            builder.AppendLine($"- {observation.Display}: {observation.Value} {observation.Unit} — Observation/{observation.Id}");
        builder.AppendLine();
        builder.AppendLine("5. Encounters");
        foreach (var encounter in encounters)
            builder.AppendLine($"- {encounter.TypeDisplay} on {encounter.Date} with {encounter.Practitioner} — Encounter/{encounter.Id}");
        builder.AppendLine();
        builder.AppendLine("6. Allergies");
        foreach (var allergy in allergies)
            builder.AppendLine($"- {allergy.Display} ({allergy.Criticality}) — AllergyIntolerance/{allergy.Id}");

        var citations = new List<Citation> { new($"Patient/{patient.Id}", patient.Name) };
        citations.AddRange(conditions.Select(condition => new Citation($"Condition/{condition.Id}", condition.Display)));
        citations.AddRange(medications.Select(medication => new Citation($"MedicationRequest/{medication.Id}", medication.Display)));
        citations.AddRange(observations.Select(observation => new Citation($"Observation/{observation.Id}", observation.Display)));
        citations.AddRange(encounters.Select(encounter => new Citation($"Encounter/{encounter.Id}", encounter.TypeDisplay)));
        citations.AddRange(allergies.Select(allergy => new Citation($"AllergyIntolerance/{allergy.Id}", allergy.Display)));

        return new StubExecutionPlan(
            builder.ToString().Trim(),
            ["Selected clinical agent.", $"Resolved patient from query to Patient/{patient.Id}.", "Joined conditions, meds, observations, encounters, and allergies."],
            ["search_conditions", "search_medications", "search_observations", "search_encounters", "search_allergies"],
            citations,
            "high");
    }

    private async Task<StubExecutionPlan> ExecuteCohortAsync(string query, CancellationToken ct)
    {
        var diabeticIds = await GetDiabeticPatientIdsAsync(ct);

        var medications = await _backend.GetMedicationsAsync(ct);
        var metforminIds = medications
            .Where(medication => medication.Display.Contains("metformin", StringComparison.OrdinalIgnoreCase))
            .Select(medication => medication.PatientId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allPatients = await _backend.GetPatientsAsync(ct);
        var gapPatients = allPatients
            .Where(patient => diabeticIds.Contains(patient.Id) && !metforminIds.Contains(patient.Id))
            .ToList();

        var answer = gapPatients.Count == 0
            ? "No diabetic patients without metformin were found."
            : $"Diabetic patients without metformin: {string.Join(", ", gapPatients.Select(patient => $"{patient.Name} (Patient/{patient.Id})"))}.";

        return new StubExecutionPlan(
            answer,
            ["Selected cohort agent.", "Built the diabetes patient set from Condition records.", "Built the metformin patient set from MedicationRequest records.", "Computed the set difference."],
            ["search_conditions", "search_medications", "calculator"],
            gapPatients.Select(patient => new Citation($"Patient/{patient.Id}", patient.Name)).ToList<Citation>(),
            gapPatients.Count > 0 ? "high" : "medium");
    }

    private async Task<StubExecutionPlan> ExecuteExportAsync(string query, CancellationToken ct)
    {
        var groups = await _backend.GetGroupsAsync(ct);
        var group = groups.First();
        var export = await _backend.BulkExportAsync(group.Id, ct);
        var resourceSummary = string.Join(", ", export.ResourceCounts.Select(pair => $"{pair.Key}: {pair.Value}"));
        var answer = $"Started and completed export for Group/{group.Id} ({group.Name}). Status: {export.Status}. Resource counts: {resourceSummary}.";
        return new StubExecutionPlan(
            answer,
            ["Selected export agent.", $"Resolved export group to Group/{group.Id}.", "Returned export summary."],
            ["search_groups", "bulk_export"],
            [new Citation($"Group/{group.Id}", group.Name)],
            "high");
    }

    private async Task<HashSet<string>> GetDiabeticPatientIdsAsync(CancellationToken ct)
    {
        var conditions = await _backend.GetConditionsAsync(ct);
        return conditions
            .Where(condition => condition.Code.StartsWith("E11", StringComparison.OrdinalIgnoreCase))
            .Select(condition => condition.PatientId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Regex PatientIdRegex = new(@"patient[-/ ](?<id>\d{4}|[a-z0-9\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private async Task<PatientRecord?> FindPatientAsync(string query, CancellationToken ct)
    {
        var idMatch = PatientIdRegex.Match(query);
        var patients = await _backend.GetPatientsAsync(ct);

        if (idMatch.Success)
        {
            var raw = idMatch.Groups["id"].Value;
            var normalized = raw.StartsWith("patient-", StringComparison.OrdinalIgnoreCase) ? raw : $"patient-{raw}";
            return patients.FirstOrDefault(patient => string.Equals(patient.Id, normalized, StringComparison.OrdinalIgnoreCase));
        }

        return patients.FirstOrDefault(patient => query.Contains(patient.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ExtractNameHint(string query)
    {
        var parts = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^1] : null;
    }

    private static IEnumerable<string> Chunk(string text, int size)
    {
        for (var index = 0; index < text.Length; index += size)
            yield return text.Substring(index, Math.Min(size, text.Length - index));
    }
}
