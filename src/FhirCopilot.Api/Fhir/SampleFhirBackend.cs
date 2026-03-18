using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Fhir;

public interface IFhirBackend
{
    Task<IReadOnlyList<GroupRecord>> GetGroupsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PatientRecord>> GetPatientsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ConditionRecord>> GetConditionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MedicationRecord>> GetMedicationsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default);
    Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default);
    Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default);

    Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default);
    Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default);
    Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default);
    Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default);
    Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default);

    Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default);
}

public sealed class SampleFhirBackend : IFhirBackend
{
    private readonly IReadOnlyList<GroupRecord> _groups =
    [
        new GroupRecord("group-2026-northwind", "Northwind ACO 2026", "Starter attributed population for the Northwind panel.", ["patient-0001", "patient-0002", "patient-0003"])
    ];

    private readonly IReadOnlyList<PatientRecord> _patients =
    [
        new("patient-0001", "Alice Carter", "female", 1979, "Northwind Primary Care", "Dr Rao", "Aetna PPO", "group-2026-northwind"),
        new("patient-0002", "Bob Nguyen", "male", 1968, "Northwind Primary Care", "Dr Patel", "Medicare Advantage", "group-2026-northwind"),
        new("patient-0003", "Carla Gomez", "female", 1958, "Northwind Primary Care", "Dr Singh", "Blue Shield HMO", "group-2026-northwind")
    ];

    private readonly IReadOnlyList<EncounterRecord> _encounters =
    [
        new("enc-0001", "patient-0001", "99213", "Office visit", "I10", "Dr Rao", "Northwind Clinic A", "2026-01-15", "finished"),
        new("enc-0002", "patient-0002", "99214", "Extended office visit", "I10", "Dr Patel", "Northwind Clinic B", "2026-02-03", "finished"),
        new("enc-0003", "patient-0003", "99213", "Office visit", "E11.65", "Dr Singh", "Northwind Clinic A", "2026-02-18", "finished")
    ];

    private readonly IReadOnlyList<ConditionRecord> _conditions =
    [
        new("cond-0001", "patient-0001", "E11.9", "Type 2 diabetes mellitus without complications", "active", "problem-list-item"),
        new("cond-0002", "patient-0001", "I10", "Essential hypertension", "active", "problem-list-item"),
        new("cond-0003", "patient-0002", "I10", "Essential hypertension", "active", "problem-list-item"),
        new("cond-0004", "patient-0003", "E11.65", "Type 2 diabetes mellitus with hyperglycemia", "active", "problem-list-item")
    ];

    private readonly IReadOnlyList<ObservationRecord> _observations =
    [
        new("obs-0001", "patient-0001", "4548-4", "Hemoglobin A1c", "8.6", "%", "laboratory", "2026-01-18"),
        new("obs-0002", "patient-0001", "8480-6", "Systolic blood pressure", "148", "mmHg", "vital-signs", "2026-01-15"),
        new("obs-0003", "patient-0003", "4548-4", "Hemoglobin A1c", "7.9", "%", "laboratory", "2026-02-19")
    ];

    private readonly IReadOnlyList<MedicationRecord> _medications =
    [
        new("med-0001", "patient-0001", "860975", "Metformin 500 mg tablet", "active", "2025-12-01"),
        new("med-0002", "patient-0002", "617314", "Lisinopril 20 mg tablet", "active", "2026-01-04")
    ];

    private readonly IReadOnlyList<ProcedureRecord> _procedures =
    [
        new("proc-0001", "patient-0001", "83036", "HbA1c test", "completed", "2026-01-18"),
        new("proc-0002", "patient-0003", "83036", "HbA1c test", "completed", "2026-02-19")
    ];

    private readonly IReadOnlyList<AllergyRecord> _allergies =
    [
        new("alg-0001", "patient-0001", "227493005", "Cashew nuts", "low", "active")
    ];

    public Task<IReadOnlyList<GroupRecord>> GetGroupsAsync(CancellationToken ct = default) => Task.FromResult(_groups);
    public Task<IReadOnlyList<PatientRecord>> GetPatientsAsync(CancellationToken ct = default) => Task.FromResult(_patients);
    public Task<IReadOnlyList<ConditionRecord>> GetConditionsAsync(CancellationToken ct = default) => Task.FromResult(_conditions);
    public Task<IReadOnlyList<MedicationRecord>> GetMedicationsAsync(CancellationToken ct = default) => Task.FromResult(_medications);

    public Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(_groups);

        IReadOnlyList<GroupRecord> result = _groups.Where(group =>
                group.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                group.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default)
    {
        var normalizedType = NormalizeResourceType(resourceType);

        object? result = normalizedType switch
        {
            "group" => _groups.FirstOrDefault(group => IsIdMatch(group.Id, id)),
            "patient" => _patients.FirstOrDefault(patient => IsIdMatch(patient.Id, id)),
            "encounter" => _encounters.FirstOrDefault(encounter => IsIdMatch(encounter.Id, id)),
            "condition" => _conditions.FirstOrDefault(condition => IsIdMatch(condition.Id, id)),
            "observation" => _observations.FirstOrDefault(observation => IsIdMatch(observation.Id, id)),
            "medicationrequest" => _medications.FirstOrDefault(medication => IsIdMatch(medication.Id, id)),
            "procedure" => _procedures.FirstOrDefault(procedure => IsIdMatch(procedure.Id, id)),
            "allergyintolerance" => _allergies.FirstOrDefault(allergy => IsIdMatch(allergy.Id, id)),
            _ => null
        };

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default)
    {
        var normalizedType = NormalizeResourceType(resourceType);

        IReadOnlyList<object> result = normalizedType switch
        {
            "group" => _groups.Cast<object>().ToList(),
            "patient" => _patients.Cast<object>().ToList(),
            "encounter" => _encounters.Cast<object>().ToList(),
            "condition" => _conditions.Cast<object>().ToList(),
            "observation" => _observations.Cast<object>().ToList(),
            "medicationrequest" => _medications.Cast<object>().ToList(),
            "procedure" => _procedures.Cast<object>().ToList(),
            "allergyintolerance" => _allergies.Cast<object>().ToList(),
            _ => Array.Empty<object>()
        };

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
    {
        IEnumerable<PatientRecord> query = _patients;

        if (!string.IsNullOrWhiteSpace(name))
            query = query.Where(patient => patient.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(gender))
            query = query.Where(patient => string.Equals(patient.Gender, gender, StringComparison.OrdinalIgnoreCase));
        if (int.TryParse(birthYearFrom, out var lower))
            query = query.Where(patient => patient.BirthYear >= lower);
        if (int.TryParse(birthYearTo, out var upper))
            query = query.Where(patient => patient.BirthYear <= upper);
        if (!string.IsNullOrWhiteSpace(generalPractitioner))
            query = query.Where(patient => patient.GeneralPractitioner.Contains(generalPractitioner, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<PatientRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        IEnumerable<EncounterRecord> query = _encounters;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(encounter => IsIdMatch(encounter.PatientId, patientId));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(encounter => string.Equals(encounter.Status, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(encounter => encounter.TypeCode.Contains(type, StringComparison.OrdinalIgnoreCase) || encounter.TypeDisplay.Contains(type, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(reasonCode))
            query = query.Where(encounter => encounter.ReasonCode.Contains(reasonCode, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(practitioner))
            query = query.Where(encounter => encounter.Practitioner.Contains(practitioner, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(encounter => encounter.Location.Contains(location, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dateFrom))
            query = query.Where(encounter => string.CompareOrdinal(encounter.Date, dateFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(dateTo))
            query = query.Where(encounter => string.CompareOrdinal(encounter.Date, dateTo) <= 0);

        IReadOnlyList<EncounterRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default)
    {
        IEnumerable<ConditionRecord> query = _conditions;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(condition => IsIdMatch(condition.PatientId, patientId));
        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(condition => condition.Code.Contains(code, StringComparison.OrdinalIgnoreCase) || condition.Display.Contains(code, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(clinicalStatus))
            query = query.Where(condition => string.Equals(condition.ClinicalStatus, clinicalStatus, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(condition => string.Equals(condition.Category, category, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<ConditionRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        IEnumerable<ObservationRecord> query = _observations;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(observation => IsIdMatch(observation.PatientId, patientId));
        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(observation => observation.Code.Contains(code, StringComparison.OrdinalIgnoreCase) || observation.Display.Contains(code, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(observation => string.Equals(observation.Category, category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(dateFrom))
            query = query.Where(observation => string.CompareOrdinal(observation.EffectiveDate, dateFrom) >= 0);
        if (!string.IsNullOrWhiteSpace(dateTo))
            query = query.Where(observation => string.CompareOrdinal(observation.EffectiveDate, dateTo) <= 0);

        IReadOnlyList<ObservationRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default)
    {
        IEnumerable<MedicationRecord> query = _medications;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(medication => IsIdMatch(medication.PatientId, patientId));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(medication => string.Equals(medication.Status, status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(medication => medication.Code.Contains(code, StringComparison.OrdinalIgnoreCase) || medication.Display.Contains(code, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<MedicationRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default)
    {
        IEnumerable<ProcedureRecord> query = _procedures;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(procedure => IsIdMatch(procedure.PatientId, patientId));
        if (!string.IsNullOrWhiteSpace(code))
            query = query.Where(procedure => procedure.Code.Contains(code, StringComparison.OrdinalIgnoreCase) || procedure.Display.Contains(code, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<ProcedureRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default)
    {
        IEnumerable<AllergyRecord> query = _allergies;

        if (!string.IsNullOrWhiteSpace(patientId))
            query = query.Where(allergy => IsIdMatch(allergy.PatientId, patientId));

        IReadOnlyList<AllergyRecord> result = query.ToList();
        return Task.FromResult(result);
    }

    public Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default)
    {
        var targetGroup = _groups.FirstOrDefault(group => IsIdMatch(group.Id, groupId)) ?? _groups.First();

        var memberSet = targetGroup.MemberPatientIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var summary = new ExportSummary(
            targetGroup.Id,
            "completed",
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Patient"] = memberSet.Count,
                ["Encounter"] = _encounters.Count(encounter => memberSet.Contains(encounter.PatientId)),
                ["Condition"] = _conditions.Count(condition => memberSet.Contains(condition.PatientId)),
                ["Observation"] = _observations.Count(observation => memberSet.Contains(observation.PatientId)),
                ["MedicationRequest"] = _medications.Count(medication => memberSet.Contains(medication.PatientId)),
                ["Procedure"] = _procedures.Count(procedure => memberSet.Contains(procedure.PatientId)),
                ["AllergyIntolerance"] = _allergies.Count(allergy => memberSet.Contains(allergy.PatientId))
            });

        return Task.FromResult(summary);
    }

    private static string NormalizeResourceType(string resourceType)
        => resourceType.Trim().Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static bool IsIdMatch(string actual, string requested)
        => string.Equals(actual, requested.Trim(), StringComparison.OrdinalIgnoreCase);
}
