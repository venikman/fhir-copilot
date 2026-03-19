using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Fhir;

public interface IFhirBackend
{
    Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default);
    Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default);
    Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default);

    Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default);
    Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default);
    Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default);
    Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default);
    Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default);
    Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default);

    Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default);
}
