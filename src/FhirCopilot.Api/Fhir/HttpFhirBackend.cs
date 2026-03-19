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

    public async Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(query) ? "Group" : $"Group?name:contains={Uri.EscapeDataString(query)}";
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapGroup).Where(g => g is not null).Select(g => g!).ToList();
    }

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

    public async Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
    {
        var parts = new List<string> { "Patient?" };
        if (!string.IsNullOrWhiteSpace(name)) parts.Add($"name:contains={Uri.EscapeDataString(name)}");
        if (!string.IsNullOrWhiteSpace(gender)) parts.Add($"gender={Uri.EscapeDataString(gender)}");
        if (!string.IsNullOrWhiteSpace(generalPractitioner)) parts.Add($"general-practitioner:contains={Uri.EscapeDataString(generalPractitioner)}");
        var url = parts[0] + string.Join("&", parts.Skip(1));
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapPatient).Where(p => p is not null).Select(p => p!).ToList();
    }

    public async Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(patientId)) parts.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(dateFrom)) parts.Add($"date=ge{Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrWhiteSpace(dateTo)) parts.Add($"date=le{Uri.EscapeDataString(dateTo)}");
        var url = "Encounter?" + string.Join("&", parts);
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapEncounter).Where(e => e is not null).Select(e => e!).ToList();
    }

    public async Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(patientId)) parts.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(code)) parts.Add($"code={Uri.EscapeDataString(code)}");
        if (!string.IsNullOrWhiteSpace(clinicalStatus)) parts.Add($"clinical-status={Uri.EscapeDataString(clinicalStatus)}");
        if (!string.IsNullOrWhiteSpace(category)) parts.Add($"category={Uri.EscapeDataString(category)}");
        var url = "Condition?" + string.Join("&", parts);
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapCondition).Where(c => c is not null).Select(c => c!).ToList();
    }

    public async Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(patientId)) parts.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(code)) parts.Add($"code={Uri.EscapeDataString(code)}");
        if (!string.IsNullOrWhiteSpace(category)) parts.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(dateFrom)) parts.Add($"date=ge{Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrWhiteSpace(dateTo)) parts.Add($"date=le{Uri.EscapeDataString(dateTo)}");
        var url = "Observation?" + string.Join("&", parts);
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapObservation).Where(o => o is not null).Select(o => o!).ToList();
    }

    public async Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(patientId)) parts.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(status)) parts.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(code)) parts.Add($"code={Uri.EscapeDataString(code)}");
        var url = "MedicationRequest?" + string.Join("&", parts);
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapMedication).Where(m => m is not null).Select(m => m!).ToList();
    }

    public async Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(patientId) ? "AllergyIntolerance" : $"AllergyIntolerance?patient={Uri.EscapeDataString(patientId)}";
        var entries = await FetchBundleEntries(url, ct);
        return entries.Select(MapAllergy).Where(a => a is not null).Select(a => a!).ToList();
    }

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

    private static GroupRecord? MapGroup(JsonElement r)
    {
        var members = new List<string>();
        if (r.TryGetProperty("member", out var arr))
            foreach (var m in arr.EnumerateArray())
                if (m.TryGetProperty("entity", out var entity) && entity.TryGetProperty("reference", out var rf))
                    members.Add(rf.GetString()?.Replace("Patient/", "", StringComparison.Ordinal) ?? "");

        return new GroupRecord(Str(r, "id"), Str(r, "name"), Str(r, "description"), members);
    }

    private static PatientRecord? MapPatient(JsonElement r)
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

    private static EncounterRecord? MapEncounter(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "type"), Coding(r, "type", "display"),
            Coding(r, "reasonCode"), Ref(r, "participant"),
            Ref(r, "location"), Str(r, "period"), Str(r, "status"));

    private static ConditionRecord? MapCondition(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "code"), Coding(r, "code", "display"),
            Coding(r, "clinicalStatus"), Coding(r, "category"));

    private static ObservationRecord? MapObservation(JsonElement r)
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

    private static MedicationRecord? MapMedication(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "subject"),
            Coding(r, "medicationCodeableConcept"), Coding(r, "medicationCodeableConcept", "display"),
            Str(r, "status"), Str(r, "authoredOn"));

    private static AllergyRecord? MapAllergy(JsonElement r) =>
        new(Str(r, "id"), Ref(r, "patient"),
            Coding(r, "code"), Coding(r, "code", "display"),
            Str(r, "criticality"), Coding(r, "clinicalStatus"));
}
