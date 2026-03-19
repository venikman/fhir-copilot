using System.Text.Json;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Fhir;

/// <summary>
/// FHIR R4 backend that forwards searches to a real FHIR server via HTTP.
/// Maps FHIR Bundle entries to flat domain records.
/// </summary>
public sealed class HttpFhirBackend(IHttpClientFactory httpClientFactory, ILogger<HttpFhirBackend> logger) : IFhirBackend
{
    private HttpClient CreateClient() => httpClientFactory.CreateClient("FhirApi");

    public Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Group", ("name:contains", query)), MapGroup, ct);

    public async Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync($"{resourceType}/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read {ResourceType}/{Id}", resourceType, id);
            return null;
        }
    }

    public async Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default)
    {
        var entries = await FetchBundleEntries(resourceType, ct);
        return entries.Select(e => (object)e).ToList();
    }

    public Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Patient",
            ("name:contains", name), ("gender", gender),
            ("general-practitioner:contains", generalPractitioner)), MapPatient, ct);

    public Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Encounter",
            ("patient", patientId), ("status", status),
            ("date", !string.IsNullOrWhiteSpace(dateFrom) ? $"ge{dateFrom}" : null),
            ("date", !string.IsNullOrWhiteSpace(dateTo) ? $"le{dateTo}" : null)), MapEncounter, ct);

    public Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Condition",
            ("patient", patientId), ("code", code),
            ("clinical-status", clinicalStatus), ("category", category)), MapCondition, ct);

    public Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Observation",
            ("patient", patientId), ("code", code), ("category", category),
            ("date", !string.IsNullOrWhiteSpace(dateFrom) ? $"ge{dateFrom}" : null),
            ("date", !string.IsNullOrWhiteSpace(dateTo) ? $"le{dateTo}" : null)), MapObservation, ct);

    public Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("MedicationRequest",
            ("patient", patientId), ("status", status), ("code", code)), MapMedication, ct);

    public Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("AllergyIntolerance", ("patient", patientId)), MapAllergy, ct);

    public Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default) =>
        SearchAsync(BuildSearchUrl("Procedure", ("patient", patientId), ("code", code)), MapProcedure, ct);

    public async Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default)
    {
        // Simplified: count resources for the group's members
        var groups = await SearchGroupsAsync(null, ct);
        var group = groups.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.OrdinalIgnoreCase)) ?? groups.FirstOrDefault();
        if (group is null)
            return new ExportSummary(groupId, "error", new Dictionary<string, int>());

        return new ExportSummary(group.Id, "completed", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Patient"] = group.MemberPatientIds.Count,
            ["Group"] = 1
        });
    }

    // --- Pure URL builder ---

    private static string BuildSearchUrl(string resourceType, params (string param, string? value)[] filters) =>
        filters
            .Where(f => !string.IsNullOrWhiteSpace(f.value))
            .Select(f => $"{f.param}={Uri.EscapeDataString(f.value!)}")
            .Aggregate(resourceType, (url, param) => $"{url}{(url.Contains('?') ? '&' : '?')}{param}");

    // --- Generic search-and-map pipeline ---

    private async Task<IReadOnlyList<T>> SearchAsync<T>(
        string url, Func<JsonElement, T> mapper, CancellationToken ct)
    {
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(mapper).ToList();
    }

    // --- Bundle fetching ---

    private async Task<IReadOnlyList<JsonElement>> FetchBundleEntries(string relativeUrl, CancellationToken ct)
    {
        try
        {
            using var client = CreateClient();
            var response = await client.GetAsync(relativeUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("FHIR search failed: {StatusCode} for {Url}", response.StatusCode, relativeUrl);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("entry", out var entries))
                return [];

            return entries.EnumerateArray()
                .Where(e => e.TryGetProperty("resource", out _))
                .Select(e => e.GetProperty("resource"))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FHIR fetch failed for {Url}", relativeUrl);
            return [];
        }
    }

    // --- Resource mappers (best-effort extraction from FHIR JSON) ---

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string Ref(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetProperty("reference", out var r) ? r.GetString() ?? "" : "";

    private static string Coding(JsonElement el, string prop, string field = "code")
    {
        if (!el.TryGetProperty(prop, out var cc)) return "";
        if (cc.TryGetProperty("coding", out var arr) && arr.GetArrayLength() > 0)
            return arr[0].TryGetProperty(field, out var v) ? v.GetString() ?? "" : "";
        if (cc.TryGetProperty("text", out var text)) return text.GetString() ?? "";
        return "";
    }

    private static GroupRecord MapGroup(JsonElement r)
    {
        var members = new List<string>();
        if (r.TryGetProperty("member", out var arr))
            foreach (var m in arr.EnumerateArray())
                if (m.TryGetProperty("entity", out var entity) && entity.TryGetProperty("reference", out var rf))
                    members.Add(rf.GetString()?.Replace("Patient/", "", StringComparison.Ordinal) ?? "");

        return new GroupRecord(Str(r, "id"), Str(r, "name"), Str(r, "description"), members);
    }

    private static PatientRecord MapPatient(JsonElement r)
    {
        var name = "";
        if (r.TryGetProperty("name", out var names) && names.GetArrayLength() > 0)
        {
            var n = names[0];
            var family = Str(n, "family");
            var given = n.TryGetProperty("given", out var g) && g.GetArrayLength() > 0 ? g[0].GetString() ?? "" : "";
            name = $"{given} {family}".Trim();
        }

        var birthYear = 0;
        if (r.TryGetProperty("birthDate", out var bd) && bd.GetString() is { Length: >= 4 } bds)
            int.TryParse(bds[..4], out birthYear);

        return new PatientRecord(Str(r, "id"), name, Str(r, "gender"), birthYear,
            Ref(r, "managingOrganization"), Ref(r, "generalPractitioner"), "", "");
    }

    private static EncounterRecord MapEncounter(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "type"), Coding(r, "type", "display"),
            Coding(r, "reasonCode"), Ref(r, "participant"),
            Ref(r, "location"), Str(r, "period"), Str(r, "status"));

    private static ConditionRecord MapCondition(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "code"), Coding(r, "code", "display"),
            Coding(r, "clinicalStatus"), Coding(r, "category"));

    private static ObservationRecord MapObservation(JsonElement r)
    {
        var value = "";
        var unit = "";
        if (r.TryGetProperty("valueQuantity", out var vq))
        {
            value = vq.TryGetProperty("value", out var v) ? v.ToString() : "";
            unit = Str(vq, "unit");
        }

        return new ObservationRecord(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "code"), Coding(r, "code", "display"),
            value, unit, Coding(r, "category"), Str(r, "effectiveDateTime"));
    }

    private static MedicationRecord MapMedication(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "medicationCodeableConcept"), Coding(r, "medicationCodeableConcept", "display"),
            Str(r, "status"), Str(r, "authoredOn"));

    private static AllergyRecord MapAllergy(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "patient"),
            Coding(r, "code"), Coding(r, "code", "display"),
            Str(r, "criticality"), Coding(r, "clinicalStatus"));

    private static ProcedureRecord MapProcedure(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "code"), Coding(r, "code", "display"),
            Str(r, "status"), Str(r, "performedDateTime"));
}
