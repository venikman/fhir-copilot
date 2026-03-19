using FhirCopilot.Api.Fhir;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Tests;

/// <summary>
/// Minimal IFhirBackend for tests that need DI to resolve but never call FHIR methods.
/// Every method throws NotImplementedException — if a test hits this, it needs a real backend.
/// </summary>
internal sealed class NullFhirBackend : IFhirBackend
{
    public Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default) => throw new NotImplementedException();
}
