using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text.Json;

namespace FhirCopilot.Api.Fhir;

public sealed class FhirToolbox
{
    private readonly IFhirBackend _backend;

    public FhirToolbox(IFhirBackend backend)
    {
        _backend = backend;
    }

    [Description("Search attribution groups by name or identifier.")]
    public async Task<string> SearchGroups([Description("Group name or identifier fragment.")] string? query = null)
        => JsonSerializer.Serialize(await _backend.SearchGroupsAsync(query), Services.JsonDefaults.Serializer);

    [Description("Read a single FHIR resource by type and id.")]
    public async Task<string> ReadResource(
        [Description("FHIR resource type such as Patient, Encounter, Condition, Observation, MedicationRequest, Procedure, AllergyIntolerance, or Group.")] string resourceType,
        [Description("FHIR resource id without the resource type prefix.")] string id)
        => JsonSerializer.Serialize(await _backend.ReadResourceAsync(resourceType, id), Services.JsonDefaults.Serializer);

    [Description("List all resources for a given FHIR type in the current backend.")]
    public async Task<string> ListResources([Description("FHIR resource type to list.")] string resourceType)
        => JsonSerializer.Serialize(await _backend.ListResourcesAsync(resourceType), Services.JsonDefaults.Serializer);

    [Description("Run a bulk export for an attribution group and return resource counts.")]
    public async Task<string> BulkExport([Description("FHIR Group id to export.")] string groupId)
        => JsonSerializer.Serialize(await _backend.BulkExportAsync(groupId), Services.JsonDefaults.Serializer);

    [Description("Search Patient resources by basic demographics and PCP.")]
    public async Task<string> SearchPatients(
        [Description("Optional patient name fragment.")] string? name = null,
        [Description("Optional gender filter.")] string? gender = null,
        [Description("Optional birth year lower bound.")] string? birthYearFrom = null,
        [Description("Optional birth year upper bound.")] string? birthYearTo = null,
        [Description("Optional primary clinician or general practitioner name fragment.")] string? generalPractitioner = null)
        => JsonSerializer.Serialize(
            await _backend.SearchPatientsAsync(name, gender, birthYearFrom, birthYearTo, generalPractitioner),
            Services.JsonDefaults.Serializer);

    [Description("Search Encounter resources by patient, status, date range, practitioner, location, type, or reason code.")]
    public async Task<string> SearchEncounters(
        [Description("Optional patient id.")] string? patientId = null,
        [Description("Optional status filter.")] string? status = null,
        [Description("Optional encounter type display or code.")] string? type = null,
        [Description("Optional reason code or text.")] string? reasonCode = null,
        [Description("Optional practitioner name fragment.")] string? practitioner = null,
        [Description("Optional location name fragment.")] string? location = null,
        [Description("Optional lower bound date in ISO-8601 format.")] string? dateFrom = null,
        [Description("Optional upper bound date in ISO-8601 format.")] string? dateTo = null)
        => JsonSerializer.Serialize(
            await _backend.SearchEncountersAsync(patientId, status, type, reasonCode, practitioner, location, dateFrom, dateTo),
            Services.JsonDefaults.Serializer);

    [Description("Search Condition resources by patient, code, status, or category.")]
    public async Task<string> SearchConditions(
        [Description("Optional patient id.")] string? patientId = null,
        [Description("Optional condition code or display text.")] string? code = null,
        [Description("Optional clinical status filter.")] string? clinicalStatus = null,
        [Description("Optional category filter.")] string? category = null)
        => JsonSerializer.Serialize(
            await _backend.SearchConditionsAsync(patientId, code, clinicalStatus, category),
            Services.JsonDefaults.Serializer);

    [Description("Search Observation resources by patient, code, category, or date range.")]
    public async Task<string> SearchObservations(
        [Description("Optional patient id.")] string? patientId = null,
        [Description("Optional observation code or display text.")] string? code = null,
        [Description("Optional observation category.")] string? category = null,
        [Description("Optional lower bound date in ISO-8601 format.")] string? dateFrom = null,
        [Description("Optional upper bound date in ISO-8601 format.")] string? dateTo = null)
        => JsonSerializer.Serialize(
            await _backend.SearchObservationsAsync(patientId, code, category, dateFrom, dateTo),
            Services.JsonDefaults.Serializer);

    [Description("Search MedicationRequest resources by patient, status, or code/display text.")]
    public async Task<string> SearchMedications(
        [Description("Optional patient id.")] string? patientId = null,
        [Description("Optional medication status.")] string? status = null,
        [Description("Optional RxNorm code or medication display text.")] string? code = null)
        => JsonSerializer.Serialize(
            await _backend.SearchMedicationsAsync(patientId, status, code),
            Services.JsonDefaults.Serializer);

    [Description("Search Procedure resources by patient or code/display text.")]
    public async Task<string> SearchProcedures(
        [Description("Optional patient id.")] string? patientId = null,
        [Description("Optional CPT code or display text.")] string? code = null)
        => JsonSerializer.Serialize(
            await _backend.SearchProceduresAsync(patientId, code),
            Services.JsonDefaults.Serializer);

    [Description("Search AllergyIntolerance resources by patient.")]
    public async Task<string> SearchAllergies([Description("Optional patient id.")] string? patientId = null)
        => JsonSerializer.Serialize(await _backend.SearchAllergiesAsync(patientId), Services.JsonDefaults.Serializer);

    [Description("Evaluate a simple arithmetic expression for ratios, percentages, or counts.")]
    public string Calculator([Description("Arithmetic expression such as '(3 / 10) * 100'.")] string expression)
    {
        var table = new DataTable();
        var result = table.Compute(expression, null);
        return Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
