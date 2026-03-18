using System.Text.Json.Nodes;
using FhirCopilot.Api.Models;

namespace FhirCopilot.Api.Fhir;

public sealed class HttpFhirBackend : IFhirBackend
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;

    public HttpFhirBackend(IHttpClientFactory httpFactory, string fhirBaseUrl)
    {
        _httpFactory = httpFactory;
        _baseUrl = fhirBaseUrl.TrimEnd('/');
    }

    public Task<IReadOnlyList<GroupRecord>> GetGroupsAsync(CancellationToken ct = default) => SearchGroupsAsync(null, ct);
    public Task<IReadOnlyList<PatientRecord>> GetPatientsAsync(CancellationToken ct = default) => SearchPatientsAsync(null, null, null, null, null, ct);
    public Task<IReadOnlyList<EncounterRecord>> GetEncountersAsync(CancellationToken ct = default) => SearchEncountersAsync(null, null, null, null, null, null, null, null, ct);
    public Task<IReadOnlyList<ConditionRecord>> GetConditionsAsync(CancellationToken ct = default) => SearchConditionsAsync(null, null, null, null, ct);
    public Task<IReadOnlyList<ObservationRecord>> GetObservationsAsync(CancellationToken ct = default) => SearchObservationsAsync(null, null, null, null, null, ct);
    public Task<IReadOnlyList<MedicationRecord>> GetMedicationsAsync(CancellationToken ct = default) => SearchMedicationsAsync(null, null, null, ct);
    public Task<IReadOnlyList<ProcedureRecord>> GetProceduresAsync(CancellationToken ct = default) => SearchProceduresAsync(null, null, ct);
    public Task<IReadOnlyList<AllergyRecord>> GetAllergiesAsync(CancellationToken ct = default) => SearchAllergiesAsync(null, ct);

    public async Task<IReadOnlyList<GroupRecord>> SearchGroupsAsync(string? query, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(query)
            ? $"{_baseUrl}/Group?_summary=true"
            : $"{_baseUrl}/Group?name={Uri.EscapeDataString(query)}";

        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapGroup).ToList();
    }

    public async Task<object?> ReadResourceAsync(string resourceType, string id, CancellationToken ct = default)
    {
        var normalized = NormalizeFhirType(resourceType);
        var url = $"{_baseUrl}/{normalized}/{Uri.EscapeDataString(id)}";

        try
        {
            var http = CreateClient();
            var response = await http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            var node = JsonNode.Parse(json);
            if (node is null) return null;

            return MapResource(node);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<object>> ListResourcesAsync(string resourceType, CancellationToken ct = default)
    {
        var normalized = NormalizeFhirType(resourceType);
        var url = $"{_baseUrl}/{normalized}?_count=200";
        var entries = await FetchAllEntriesAsync(url, ct);

        var results = new List<object>();
        foreach (var entry in entries)
        {
            var mapped = MapResource(entry);
            if (mapped is not null) results.Add(mapped);
        }

        return results;
    }

    public async Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(string? name, string? gender, string? birthYearFrom, string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(name))
            parameters.Add($"name={Uri.EscapeDataString(name)}");
        if (!string.IsNullOrWhiteSpace(gender))
            parameters.Add($"gender={Uri.EscapeDataString(gender)}");
        if (!string.IsNullOrWhiteSpace(birthYearFrom))
            parameters.Add($"birthdate=ge{Uri.EscapeDataString(birthYearFrom)}");
        if (!string.IsNullOrWhiteSpace(birthYearTo))
            parameters.Add($"birthdate=le{Uri.EscapeDataString(birthYearTo)}");
        if (!string.IsNullOrWhiteSpace(generalPractitioner))
            parameters.Add($"general-practitioner={Uri.EscapeDataString(generalPractitioner)}");

        var url = $"{_baseUrl}/Patient?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapPatient).ToList();
    }

    public async Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(string? patientId, string? status, string? type, string? reasonCode, string? practitioner, string? location, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(status))
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(type))
            parameters.Add($"type={Uri.EscapeDataString(type)}");
        if (!string.IsNullOrWhiteSpace(reasonCode))
            parameters.Add($"reason-code={Uri.EscapeDataString(reasonCode)}");
        if (!string.IsNullOrWhiteSpace(practitioner))
            parameters.Add($"practitioner={Uri.EscapeDataString(practitioner)}");
        if (!string.IsNullOrWhiteSpace(location))
            parameters.Add($"location={Uri.EscapeDataString(location)}");
        if (!string.IsNullOrWhiteSpace(dateFrom))
            parameters.Add($"date=ge{Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrWhiteSpace(dateTo))
            parameters.Add($"date=le{Uri.EscapeDataString(dateTo)}");

        var url = $"{_baseUrl}/Encounter?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapEncounter).ToList();
    }

    public async Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(string? patientId, string? code, string? clinicalStatus, string? category, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(code))
            parameters.Add($"code={Uri.EscapeDataString(code)}");
        if (!string.IsNullOrWhiteSpace(clinicalStatus))
            parameters.Add($"clinical-status={Uri.EscapeDataString(clinicalStatus)}");
        if (!string.IsNullOrWhiteSpace(category))
            parameters.Add($"category={Uri.EscapeDataString(category)}");

        var url = $"{_baseUrl}/Condition?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapCondition).ToList();
    }

    public async Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(string? patientId, string? code, string? category, string? dateFrom, string? dateTo, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(code))
            parameters.Add($"code={Uri.EscapeDataString(code)}");
        if (!string.IsNullOrWhiteSpace(category))
            parameters.Add($"category={Uri.EscapeDataString(category)}");
        if (!string.IsNullOrWhiteSpace(dateFrom))
            parameters.Add($"date=ge{Uri.EscapeDataString(dateFrom)}");
        if (!string.IsNullOrWhiteSpace(dateTo))
            parameters.Add($"date=le{Uri.EscapeDataString(dateTo)}");

        var url = $"{_baseUrl}/Observation?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapObservation).ToList();
    }

    public async Task<IReadOnlyList<MedicationRecord>> SearchMedicationsAsync(string? patientId, string? status, string? code, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(status))
            parameters.Add($"status={Uri.EscapeDataString(status)}");
        if (!string.IsNullOrWhiteSpace(code))
            parameters.Add($"code={Uri.EscapeDataString(code)}");

        var url = $"{_baseUrl}/MedicationRequest?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapMedication).ToList();
    }

    public async Task<IReadOnlyList<ProcedureRecord>> SearchProceduresAsync(string? patientId, string? code, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");
        if (!string.IsNullOrWhiteSpace(code))
            parameters.Add($"code={Uri.EscapeDataString(code)}");

        var url = $"{_baseUrl}/Procedure?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapProcedure).ToList();
    }

    public async Task<IReadOnlyList<AllergyRecord>> SearchAllergiesAsync(string? patientId, CancellationToken ct = default)
    {
        var parameters = new List<string> { "_count=200" };

        if (!string.IsNullOrWhiteSpace(patientId))
            parameters.Add($"patient={Uri.EscapeDataString(patientId)}");

        var url = $"{_baseUrl}/AllergyIntolerance?{string.Join("&", parameters)}";
        var entries = await FetchAllEntriesAsync(url, ct);
        return entries.Select(MapAllergy).ToList();
    }

    public async Task<ExportSummary> BulkExportAsync(string groupId, CancellationToken ct = default)
    {
        var kickOffUrl = $"{_baseUrl}/Group/{Uri.EscapeDataString(groupId)}/$davinci-data-export" +
                         "?exportType=hl7.fhir.us.davinci-atr" +
                         "&_type=Group,Patient,Coverage,Encounter,Condition,Observation,MedicationRequest,Procedure,AllergyIntolerance";

        try
        {
            var http = CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, kickOffUrl);
            request.Headers.Add("Accept", "application/fhir+json");
            request.Headers.Add("Prefer", "respond-async");

            var response = await http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted &&
                response.Headers.TryGetValues("Content-Location", out var locations))
            {
                var statusUrl = locations.First();
                return await PollExportStatusAsync(statusUrl, groupId, ct);
            }

            return new ExportSummary(groupId, "error", new Dictionary<string, int>());
        }
        catch (HttpRequestException)
        {
            return new ExportSummary(groupId, "error", new Dictionary<string, int>());
        }
    }

    private async Task<ExportSummary> PollExportStatusAsync(string statusUrl, string groupId, CancellationToken ct)
    {
        var http = CreateClient();

        for (var attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(1000, ct);

            var response = await http.GetAsync(statusUrl, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                continue;

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                var root = JsonNode.Parse(body);

                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (root?["output"] is JsonArray output)
                {
                    foreach (var item in output)
                    {
                        var type = item?["type"]?.GetValue<string>() ?? "";
                        if (!string.IsNullOrEmpty(type))
                        {
                            counts.TryGetValue(type, out var current);
                            var count = item?["count"]?.GetValue<int>() ?? 1;
                            counts[type] = current + count;
                        }
                    }
                }

                return new ExportSummary(groupId, "completed", counts);
            }
        }

        return new ExportSummary(groupId, "timeout", new Dictionary<string, int>());
    }

    // --- HTTP helpers ---

    private HttpClient CreateClient()
    {
        var client = _httpFactory.CreateClient("FhirApi");
        return client;
    }

    private async Task<List<JsonNode>> FetchAllEntriesAsync(string url, CancellationToken ct)
    {
        var allEntries = new List<JsonNode>();
        string? nextUrl = url;
        const int maxPages = 5;
        var http = CreateClient();

        for (var page = 0; page < maxPages && nextUrl is not null; page++)
        {
            var response = await http.GetAsync(nextUrl, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var root = JsonNode.Parse(json);
            if (root is null) break;

            if (root["entry"] is JsonArray entries)
            {
                foreach (var entry in entries)
                {
                    var resource = entry?["resource"];
                    if (resource is not null)
                        allEntries.Add(resource);
                }
            }

            nextUrl = FindNextLink(root);
        }

        return allEntries;
    }

    private static string? FindNextLink(JsonNode root)
    {
        if (root["link"] is not JsonArray links) return null;

        foreach (var link in links)
        {
            if (link?["relation"]?.GetValue<string>() == "next")
                return link?["url"]?.GetValue<string>();
        }

        return null;
    }

    // --- Mapping helpers ---

    private static PatientRecord MapPatient(JsonNode resource)
    {
        var id = Str(resource, "id");

        var name = "";
        if (resource["name"] is JsonArray names && names.Count > 0)
        {
            var nameObj = names[0];
            var given = nameObj?["given"] is JsonArray g
                ? string.Join(" ", g.Select(x => x?.GetValue<string>()))
                : "";
            var family = nameObj?["family"]?.GetValue<string>() ?? "";
            name = $"{given} {family}".Trim();
        }

        var gender = Str(resource, "gender");
        var birthYear = 0;
        var bdStr = Str(resource, "birthDate");
        if (bdStr.Length >= 4)
            int.TryParse(bdStr[..4], out birthYear);

        var managingOrg = resource["managingOrganization"]?["display"]?.GetValue<string>()
                          ?? Str(resource["managingOrganization"], "reference");

        var gp = "";
        if (resource["generalPractitioner"] is JsonArray gpArr && gpArr.Count > 0)
            gp = gpArr[0]?["display"]?.GetValue<string>() ?? Str(gpArr[0], "reference");

        return new PatientRecord(id, name, gender, birthYear, managingOrg, gp, "", "");
    }

    private static EncounterRecord MapEncounter(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["subject"], "reference"));

        var typeCode = "";
        var typeDisplay = "";
        if (resource["type"] is JsonArray types && types.Count > 0)
            (typeCode, typeDisplay) = FirstCoding(types[0]);

        var reasonCode = "";
        if (resource["reasonCode"] is JsonArray reasons && reasons.Count > 0)
            (reasonCode, _) = FirstCoding(reasons[0]);

        var practitioner = "";
        if (resource["participant"] is JsonArray parts)
        {
            foreach (var p in parts)
            {
                var ind = p?["individual"];
                if (ind is not null)
                {
                    practitioner = ind["display"]?.GetValue<string>() ?? Str(ind, "reference");
                    break;
                }
            }
        }

        var location = "";
        if (resource["location"] is JsonArray locs && locs.Count > 0)
        {
            var locRef = locs[0]?["location"];
            if (locRef is not null)
                location = locRef["display"]?.GetValue<string>() ?? Str(locRef, "reference");
        }

        var date = Str(resource["period"], "start");
        var status = Str(resource, "status");

        return new EncounterRecord(id, patientId, typeCode, typeDisplay, reasonCode, practitioner, location, date, status);
    }

    private static ConditionRecord MapCondition(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["subject"], "reference"));
        var (code, display) = FirstCoding(resource["code"]);
        var (clinicalStatus, _) = FirstCoding(resource["clinicalStatus"]);
        var category = "";
        if (resource["category"] is JsonArray cat && cat.Count > 0)
            (category, _) = FirstCoding(cat[0]);

        return new ConditionRecord(id, patientId, code, display, clinicalStatus, category);
    }

    private static ObservationRecord MapObservation(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["subject"], "reference"));
        var (code, display) = FirstCoding(resource["code"]);

        var value = "";
        var unit = "";
        if (resource["valueQuantity"] is JsonNode vq)
        {
            value = vq["value"]?.ToString() ?? "";
            unit = Str(vq, "unit");
        }
        else if (resource["valueString"] is JsonNode vs)
        {
            value = vs.GetValue<string>();
        }
        else if (resource["valueCodeableConcept"] is JsonNode vcc)
        {
            (_, value) = FirstCoding(vcc);
        }

        var category = "";
        if (resource["category"] is JsonArray cat && cat.Count > 0)
            (category, _) = FirstCoding(cat[0]);

        var effectiveDate = Str(resource, "effectiveDateTime");
        if (string.IsNullOrEmpty(effectiveDate))
            effectiveDate = Str(resource["effectivePeriod"], "start");

        return new ObservationRecord(id, patientId, code, display, value, unit, category, effectiveDate);
    }

    private static MedicationRecord MapMedication(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["subject"], "reference"));
        var (code, display) = FirstCoding(resource["medicationCodeableConcept"]);
        var status = Str(resource, "status");
        var authoredOn = Str(resource, "authoredOn");

        return new MedicationRecord(id, patientId, code, display, status, authoredOn);
    }

    private static ProcedureRecord MapProcedure(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["subject"], "reference"));
        var (code, display) = FirstCoding(resource["code"]);
        var status = Str(resource, "status");
        var performedOn = Str(resource, "performedDateTime");
        if (string.IsNullOrEmpty(performedOn))
            performedOn = Str(resource["performedPeriod"], "start");

        return new ProcedureRecord(id, patientId, code, display, status, performedOn);
    }

    private static AllergyRecord MapAllergy(JsonNode resource)
    {
        var id = Str(resource, "id");
        var patientId = ExtractId(Str(resource["patient"], "reference"));
        var (code, display) = FirstCoding(resource["code"]);
        var criticality = Str(resource, "criticality");
        var (clinicalStatus, _) = FirstCoding(resource["clinicalStatus"]);

        return new AllergyRecord(id, patientId, code, display, criticality, clinicalStatus);
    }

    private static GroupRecord MapGroup(JsonNode resource)
    {
        var id = Str(resource, "id");
        var name = Str(resource, "name");
        var description = resource["text"]?["div"]?.GetValue<string>() ?? "";

        var memberIds = new List<string>();
        if (resource["member"] is JsonArray members)
        {
            foreach (var m in members)
            {
                var reference = m?["entity"]?["reference"]?.GetValue<string>() ?? "";
                if (!string.IsNullOrEmpty(reference))
                    memberIds.Add(ExtractId(reference));
            }
        }

        return new GroupRecord(id, name, description, memberIds);
    }

    private object? MapResource(JsonNode resource)
    {
        return Str(resource, "resourceType") switch
        {
            "Patient" => MapPatient(resource),
            "Encounter" => MapEncounter(resource),
            "Condition" => MapCondition(resource),
            "Observation" => MapObservation(resource),
            "MedicationRequest" => MapMedication(resource),
            "Procedure" => MapProcedure(resource),
            "AllergyIntolerance" => MapAllergy(resource),
            "Group" => MapGroup(resource),
            _ => resource.ToJsonString()
        };
    }

    // --- Utility helpers ---

    private static (string code, string display) FirstCoding(JsonNode? codeableConcept)
    {
        if (codeableConcept is null) return ("", "");

        if (codeableConcept["coding"] is JsonArray codings && codings.Count > 0)
        {
            var first = codings[0];
            return (Str(first, "code"), Str(first, "display"));
        }

        var text = Str(codeableConcept, "text");
        return (text, text);
    }

    private static string Str(JsonNode? node, string property)
        => node?[property]?.GetValue<string>() ?? "";

    private static string ExtractId(string fhirReference)
    {
        var slashIndex = fhirReference.LastIndexOf('/');
        return slashIndex >= 0 ? fhirReference[(slashIndex + 1)..] : fhirReference;
    }

    private static string NormalizeFhirType(string resourceType)
    {
        var clean = resourceType.Trim().Replace("_", "", StringComparison.Ordinal);
        return clean.ToLowerInvariant() switch
        {
            "patient" => "Patient",
            "encounter" => "Encounter",
            "condition" => "Condition",
            "observation" => "Observation",
            "medicationrequest" => "MedicationRequest",
            "procedure" => "Procedure",
            "allergyintolerance" => "AllergyIntolerance",
            "group" => "Group",
            "coverage" => "Coverage",
            "practitioner" => "Practitioner",
            "practitionerrole" => "PractitionerRole",
            "organization" => "Organization",
            "location" => "Location",
            "relatedperson" => "RelatedPerson",
            _ => resourceType
        };
    }
}
