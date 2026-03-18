namespace FhirCopilot.Api.Models;

public sealed record GroupRecord(string Id, string Name, string Description, IReadOnlyList<string> MemberPatientIds);

public sealed record PatientRecord(
    string Id,
    string Name,
    string Gender,
    int BirthYear,
    string ManagingOrganization,
    string GeneralPractitioner,
    string Coverage,
    string GroupId);

public sealed record EncounterRecord(
    string Id,
    string PatientId,
    string TypeCode,
    string TypeDisplay,
    string ReasonCode,
    string Practitioner,
    string Location,
    string Date,
    string Status);

public sealed record ConditionRecord(
    string Id,
    string PatientId,
    string Code,
    string Display,
    string ClinicalStatus,
    string Category);

public sealed record ObservationRecord(
    string Id,
    string PatientId,
    string Code,
    string Display,
    string Value,
    string Unit,
    string Category,
    string EffectiveDate);

public sealed record MedicationRecord(
    string Id,
    string PatientId,
    string Code,
    string Display,
    string Status,
    string AuthoredOn);

public sealed record ProcedureRecord(
    string Id,
    string PatientId,
    string Code,
    string Display,
    string Status,
    string PerformedOn);

public sealed record AllergyRecord(
    string Id,
    string PatientId,
    string Code,
    string Display,
    string Criticality,
    string ClinicalStatus);

public sealed record ExportSummary(string GroupId, string Status, IReadOnlyDictionary<string, int> ResourceCounts);

public sealed record StubExecutionPlan(
    string Answer,
    IReadOnlyList<string> Reasoning,
    IReadOnlyList<string> ToolsUsed,
    IReadOnlyList<Contracts.Citation> Citations,
    string Confidence);
