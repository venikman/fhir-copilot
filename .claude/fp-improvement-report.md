# Functional Programming Improvement Report
## FhirCopilot.Api Codebase Analysis

**Date:** 2026-03-19  
**Scope:** Complete analysis of `/src/FhirCopilot.Api/` directory (24 C# files)  
**Framework:** ASP.NET Core 9+, Agent Framework, FHIR R4

---

## Executive Summary

The codebase demonstrates **strong FP foundation** in domain models and contracts (all sealed records with immutable collections), but shows **improvement opportunities in I/O layers** (HttpFhirBackend, FhirToolbox, Program.cs) where imperative patterns, exception handling, and null returns need functional abstractions.

**Key Statistics:**
- Files analyzed: 24 C# files in API project
- Strong FP adherence: Domain models, Contracts, Interfaces
- Improvement opportunities: 7 files with specific patterns to refactor
- FP categories needing work: Error handling (Result<T>), null handling (Option<T>), I/O concerns, imperative loops

---

## Category 1: Domain Model Types and Immutability Status

### Summary
**Status: EXCELLENT** - All domain models follow FP best practices with sealed records and immutable collections.

### Strong Patterns (No Changes Needed)

#### DomainModels.cs (Models/, 69 lines)
All seven record types demonstrate perfect immutability:
- **GroupRecord** (line 3): `public sealed record GroupRecord(string Id, string Name, string Description, IReadOnlyList<string> MemberPatientIds)`
- **PatientRecord** (lines 5-7): Sealed record with IReadOnlyList properties
- **EncounterRecord** (lines 9-13): Sealed record
- **ConditionRecord** (lines 15-19): Sealed record
- **ObservationRecord** (lines 21-28): Sealed record
- **MedicationRecord** (lines 30-36): Sealed record
- **AllergyRecord** (lines 38-44): Sealed record
- **ExportSummary** (line 46-50): Uses `IReadOnlyDictionary<string, int> ResourceCounts`

**Key Insight:** All properties are positional parameters in records, making them immutable by design. Collections use `IReadOnlyList<T>` and `IReadOnlyDictionary<K,V>` to prevent external mutation.

#### Contracts/CopilotContracts.cs (37 lines)
- **CopilotRequest** (line 3): `sealed record CopilotRequest(string Query, string? ThreadId = null)` - immutable
- **Citation** (line 5): `sealed record Citation(string ResourceId, string? Label = null)` - immutable
- **CopilotResponse** (lines 7-15): All properties use `IReadOnlyList<T>` for citations, reasoning, and tools used
- **CopilotStreamEvent** (lines 17-24): Factory methods (Meta, Delta, Done, Error) provide controlled object creation

---

### One Minor Improvement Identified

#### AgentConfigModels.cs (Models/, 40 lines)

**Location:** Line 18 (RouterProfile.KeywordHints)

**Current Pattern:**
```csharp
public sealed record RouterProfile(
    string Name,
    Dictionary<string, string[]> KeywordHints);  // ← Mutable collection type
```

**Issue:** Uses mutable `Dictionary<string, string[]>` instead of immutable collection types.

**Recommendation:**
```csharp
public sealed record RouterProfile(
    string Name,
    IReadOnlyDictionary<string, IReadOnlyList<string>> KeywordHints);  // ← Immutable pattern
```

**Implementation Notes:**
- Change `Dictionary<string, string[]>` to `IReadOnlyDictionary<string, IReadOnlyList<string>>`
- Update initialization sites to use `new Dictionary<...>().AsReadOnly()` or immutable dictionary factory methods
- If configuration is loaded from JSON, use custom serialization converter for immutable types

---

## Category 2: Error Handling Patterns

### Summary
**Status: NEEDS WORK** - Codebase uses traditional exception handling (catch-rethrow, catch-return null) instead of functional Result<T> or Option<T> monads.

### Pattern 1: Exception Catch-Rethrow (HTTP 429 Handling)

#### AgentRunner.cs (Services/, lines 74-81)

**Current Pattern:**
```csharp
foreach (var model in models)
{
    try
    {
        var agent = GetOrCreateAgent(profile, model);
        var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);
        
        var answerBuilder = new StringBuilder();
        await foreach (var update in agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                answerBuilder.Append(update.Text);
            }
        }
        
        var answer = answerBuilder.ToString().Trim();
        return BuildResponse(answer, profile, threadId);
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
        lastException = ex;
        _logger.LogWarning("Model {Model} rate-limited (429), falling back to next model", model);
    }
}
throw lastException!;  // Non-null assertion, can throw null reference
```

**Issue:**
- Exception handling using pattern match on StatusCode is low-level
- Mutable `lastException` variable tracks state across loop
- Non-null assertion (`lastException!`) can hide logic errors
- Fallback chain logic mixed with exception handling

**Recommendation - Result<T> Monad Pattern:**
```csharp
public abstract record Result<T>
{
    public sealed record Ok(T Value) : Result<T>;
    public sealed record Error(string Message) : Result<T>;
    
    public Result<TResult> Bind<TResult>(Func<T, Result<TResult>> f) =>
        this switch 
        { 
            Ok(var v) => f(v), 
            Error(var m) => new Result<TResult>.Error(m) 
        };
}

private async Task<Result<string>> TryModelAsync(
    AgentProfile profile, 
    string model, 
    string query, 
    string threadId, 
    CancellationToken cancellationToken)
{
    try
    {
        var agent = GetOrCreateAgent(profile, model);
        var session = await GetOrCreateSessionAsync(threadId, profile.Name, agent);
        var answerBuilder = new StringBuilder();
        
        await foreach (var update in agent.RunStreamingAsync(query, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(update.Text))
                answerBuilder.Append(update.Text);
        }
        
        return new Result<string>.Ok(answerBuilder.ToString().Trim());
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
    {
        return new Result<string>.Error($"Model {model} rate-limited (429)");
    }
    catch (Exception ex)
    {
        return new Result<string>.Error($"Model {model} failed: {ex.Message}");
    }
}

// Usage in RunAsync:
public async Task<CopilotResponse> RunAsync(
    AgentProfile profile, 
    string query, 
    string threadId, 
    CancellationToken cancellationToken)
{
    var models = _clientFactory.ModelChain;
    
    foreach (var model in models)
    {
        var result = await TryModelAsync(profile, model, query, threadId, cancellationToken);
        
        if (result is Result<string>.Ok(var answer))
        {
            return BuildResponse(answer, profile, threadId);
        }
        
        // Log warning from error message
        if (result is Result<string>.Error(var message))
        {
            _logger.LogWarning(message);
        }
    }
    
    // Cleaner fallback behavior
    throw new InvalidOperationException("All models in chain failed to respond");
}
```

**Benefits:**
- Eliminates null assertion `lastException!`
- Explicit error type in signature
- Caller forced to handle both success and failure
- Composable with other Result-returning functions

---

### Pattern 2: Exception Catch-Return Null (Resource Mapping)

#### HttpFhirBackend.cs (Fhir/, lines 31-35)

**Current Pattern:**
```csharp
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
        return null;  // ← Silent failure, caller sees null without context
    }
}
```

**Issue:**
- Returns `null` on exception, losing error context
- Caller cannot distinguish between "resource not found" and "network error"
- Two different failure modes (404 vs exception) produce same return value

**Recommendation - Option<T> or Result<T>:**
```csharp
public async Task<Result<JsonElement>> ReadResourceAsync(
    string resourceType, 
    string id, 
    CancellationToken ct = default)
{
    try
    {
        using var client = CreateClient();
        var response = await client.GetAsync($"{resourceType}/{id}", ct);
        
        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? new Result<JsonElement>.Error($"{resourceType}/{id} not found")
                : new Result<JsonElement>.Error($"HTTP {response.StatusCode}");
        }
        
        var json = await response.Content.ReadAsStringAsync(ct);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return new Result<JsonElement>.Ok(element);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to read {ResourceType}/{Id}", resourceType, id);
        return new Result<JsonElement>.Error($"Read failed: {ex.Message}");
    }
}
```

**Usage:**
```csharp
var result = await backend.ReadResourceAsync("Patient", "123");
return result switch
{
    Result<JsonElement>.Ok(var element) => ProcessResource(element),
    Result<JsonElement>.Error(var message) => HandleError(message)
};
```

#### HttpFhirBackend.cs (Fhir/, lines 150-154)

Similar pattern in `FetchBundleEntries`:
```csharp
catch (Exception ex)
{
    logger.LogWarning(ex, "FHIR fetch failed for {Url}", relativeUrl);
    return [];  // ← Empty list hides whether fetch succeeded with 0 results or failed
}
```

**Recommendation:** Same Result<T> pattern to distinguish success-with-empty-results from failure.

---

## Category 3: Collection Type Declarations

### Summary
**Status: VERY GOOD** - 95% of codebase uses `IReadOnlyList<T>` and `IReadOnlyDictionary<K,V>`. One identified improvement in RouterProfile.

### Strong Patterns (No Changes Needed)

**Excellent Examples:**

1. **IFhirBackend.cs (Fhir/, 19 lines)** - All search methods return immutable collections:
   ```csharp
   Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(...);
   Task<IReadOnlyList<EncounterRecord>> SearchEncountersAsync(...);
   Task<IReadOnlyList<ConditionRecord>> SearchConditionsAsync(...);
   Task<IReadOnlyList<ObservationRecord>> SearchObservationsAsync(...);
   ```

2. **LocalChatClientFactory.cs (Services/, line 18):**
   ```csharp
   public IReadOnlyList<string> ModelChain => [options.Value.LocalModel!];
   ```

3. **CopilotContracts.cs (lines 9-11):**
   ```csharp
   public sealed record CopilotResponse(
       string Answer,
       IReadOnlyList<Citation> Citations,
       IReadOnlyList<string> Reasoning,
       IReadOnlyList<string> ToolsUsed,
       ...
   );
   ```

### Identified Improvement

See **Category 1** - RouterProfile (line 18) needs `IReadOnlyDictionary<string, IReadOnlyList<string>>`.

---

## Category 4: Methods Mixing I/O Operations with Pure Business Logic

### Summary
**Status: NEEDS REFACTORING** - Several methods combine HTTP/file I/O with JSON transformation and business logic, violating functional separation of concerns.

### Pattern 1: HTTP I/O + JSON Serialization + Wrapping

#### FhirToolbox.cs (Fhir/, lines 14-88)

**Current Pattern:**
```csharp
public sealed class FhirToolbox(IFhirBackend backend)
{
    public async Task<string> SearchPatientsJsonAsync(
        string? name = null,
        string? gender = null,
        string? birthYearFrom = null,
        string? birthYearTo = null,
        string? generalPractitioner = null,
        CancellationToken ct = default)
    {
        var patients = await backend.SearchPatientsAsync(name, gender, birthYearFrom, birthYearTo, generalPractitioner, ct);  // ← I/O
        return JsonSerializer.Serialize(patients);  // ← Transformation
    }
    
    // Similar pattern repeated for ~8 methods
}
```

**Issue:**
- Couples HTTP I/O (backend.SearchPatientsAsync) with JSON serialization
- Cannot test serialization independently of backend availability
- Difficult to reuse serialization logic or modify without affecting I/O

**Recommendation - Separation of Concerns:**
```csharp
// Pure function: serialize domain record to JSON
public static string SerializePatients(IReadOnlyList<PatientRecord> patients) =>
    JsonSerializer.Serialize(patients, new JsonSerializerOptions 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true 
    });

// I/O function: fetch and serialize (composition of pure + impure)
public async Task<string> SearchPatientsJsonAsync(
    string? name = null,
    string? gender = null,
    string? birthYearFrom = null,
    string? birthYearTo = null,
    string? generalPractitioner = null,
    CancellationToken ct = default)
{
    var patients = await backend.SearchPatientsAsync(
        name, gender, birthYearFrom, birthYearTo, generalPractitioner, ct);
    return SerializePatients(patients);  // Pure transformation
}
```

**Architecture Pattern:**
```csharp
// Domain layer: pure logic
public static class PatientLogic
{
    public static string Serialize(IReadOnlyList<PatientRecord> patients) => 
        JsonSerializer.Serialize(patients);
    
    public static IReadOnlyList<PatientRecord> FilterByAge(
        IReadOnlyList<PatientRecord> patients, 
        int minYear, 
        int maxYear) =>
        patients
            .Where(p => p.BirthYear >= minYear && p.BirthYear <= maxYear)
            .ToList();
}

// Application layer: I/O orchestration
public async Task<string> SearchPatientsWithFilterJsonAsync(
    string? name = null,
    int? ageMinYear = null,
    int? ageMaxYear = null,
    CancellationToken ct = default)
{
    var patients = await backend.SearchPatientsAsync(name, null, null, null, null, ct);
    
    if (ageMinYear.HasValue && ageMaxYear.HasValue)
    {
        patients = PatientLogic.FilterByAge(patients, ageMinYear.Value, ageMaxYear.Value);
    }
    
    return PatientLogic.Serialize(patients);
}
```

---

### Pattern 2: HTTP I/O + JSON Deserialization + Data Extraction

#### HttpFhirBackend.cs (Fhir/, lines 127-155)

**Current Pattern:**
```csharp
private async Task<IReadOnlyList<JsonElement>> FetchBundleEntries(string relativeUrl, CancellationToken ct)
{
    try
    {
        using var client = CreateClient();  // ← I/O
        var response = await client.GetAsync(relativeUrl, ct);  // ← I/O
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("FHIR search failed: {StatusCode} for {Url}", response.StatusCode, relativeUrl);
            return [];
        }

        var json = await response.Content.ReadAsStringAsync(ct);  // ← I/O
        var doc = JsonDocument.Parse(json);  // ← Transformation

        if (!doc.RootElement.TryGetProperty("entry", out var entries))
            return [];

        return entries.EnumerateArray()  // ← Pure transformation
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
```

**Issue:**
- I/O (network request) tightly coupled with data extraction and transformation
- Exception handling at I/O boundary hides failures from callers
- Cannot test data extraction logic independently

**Recommendation - Layered Architecture:**
```csharp
// Pure function: extract entries from parsed FHIR Bundle JSON
public static IReadOnlyList<JsonElement> ExtractBundleEntries(JsonDocument doc) =>
    doc.RootElement.TryGetProperty("entry", out var entries)
        ? entries.EnumerateArray()
            .Where(e => e.TryGetProperty("resource", out _))
            .Select(e => e.GetProperty("resource"))
            .ToList()
        : Array.Empty<JsonElement>();

// I/O function: fetch with error handling
private async Task<Result<IReadOnlyList<JsonElement>>> FetchBundleEntriesAsync(
    string relativeUrl, 
    CancellationToken ct)
{
    try
    {
        using var client = CreateClient();
        var response = await client.GetAsync(relativeUrl, ct);
        
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("FHIR search failed: {StatusCode} for {Url}", 
                response.StatusCode, relativeUrl);
            return new Result<IReadOnlyList<JsonElement>>.Error(
                $"HTTP {response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(json);
        var entries = ExtractBundleEntries(doc);  // Pure transformation
        
        return new Result<IReadOnlyList<JsonElement>>.Ok(entries);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "FHIR fetch failed for {Url}", relativeUrl);
        return new Result<IReadOnlyList<JsonElement>>.Error(ex.Message);
    }
}
```

---

## Category 5: Pattern Matching Usage

### Summary
**Status: GOOD** - Codebase uses pattern matching in appropriate places. Some opportunities for exhaustive matching.

### Strong Patterns (No Changes Needed)

#### CopilotContracts.cs (lines 26-36)

Excellent use of factory pattern with sealed record:
```csharp
public sealed record CopilotStreamEvent(...)
{
    public static CopilotStreamEvent Meta(string agentType, string threadId, bool isStub)
        => new("meta", AgentType: agentType, ThreadId: threadId, IsStub: isStub);

    public static CopilotStreamEvent Delta(string content)
        => new("delta", Content: content);

    public static CopilotStreamEvent Done(CopilotResponse response)
        => new("done", Response: response);

    public static CopilotStreamEvent Error(string message)
        => new("error", Message: message);
}
```

**Key Insight:** Factory methods enforce valid states, preventing invalid CopilotStreamEvent combinations (e.g., Delta without Content).

---

### Improvement Opportunity: Exhaustive Pattern Matching

#### HttpFhirBackend.cs (Mapping functions, lines 165-238)

**Current Pattern - Imperative Extraction:**
```csharp
private static string Coding(JsonElement el, string prop, string field = "code")
{
    if (!el.TryGetProperty(prop, out var cc)) return "";
    if (cc.TryGetProperty("coding", out var arr) && arr.GetArrayLength() > 0)
        return arr[0].TryGetProperty(field, out var v) ? v.GetString() ?? "" : "";
    if (cc.TryGetProperty("text", out var text)) return text.GetString() ?? "";
    return "";
}
```

**Recommendation - Switch Expression Pattern:**
```csharp
private static string Coding(JsonElement el, string prop, string field = "code") =>
    (
        el.TryGetProperty(prop, out var cc),
        cc.TryGetProperty("coding", out var arr),
        arr.GetArrayLength() > 0,
        arr[0].TryGetProperty(field, out var v)
    ) switch
    {
        (true, true, true, true) => v.GetString() ?? "",
        (true, false, _, _) when cc.TryGetProperty("text", out var text) 
            => text.GetString() ?? "",
        _ => ""
    };
```

Or more readably with helper:
```csharp
private static Option<string> ExtractCodingValue(JsonElement cc, string field)
{
    if (cc.TryGetProperty("coding", out var arr) && arr.GetArrayLength() > 0 &&
        arr[0].TryGetProperty(field, out var v))
    {
        return Option<string>.Some(v.GetString() ?? "");
    }
    
    if (cc.TryGetProperty("text", out var text))
    {
        return Option<string>.Some(text.GetString() ?? "");
    }
    
    return Option<string>.None;
}
```

---

## Category 6: Imperative Loops vs LINQ Transformations

### Summary
**Status: SIGNIFICANT OPPORTUNITY** - Several locations use imperative loops that should be functional LINQ transformations.

### Pattern 1: Imperative Directory Traversal

#### Program.cs (lines 8-34)

**Current Pattern - Imperative Loops:**
```csharp
static void LoadEnvFile()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)  // ← Imperative loop
    {
        var envFile = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envFile))
        {
            foreach (var line in File.ReadAllLines(envFile))  // ← Imperative loop
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))  // ← Imperative filtering
                    continue;

                var sep = trimmed.IndexOf('=');  // ← Imperative string manipulation
                if (sep <= 0)
                    continue;

                var key = trimmed[..sep].Trim();  // ← Imperative extraction
                var value = trimmed[(sep + 1)..].Trim();
                Environment.SetEnvironmentVariable(key, value);  // ← Side effect
            }
            return;
        }
        dir = dir.Parent;  // ← Imperative traversal
    }
}
```

**Recommendation - LINQ-Based Functional Style:**
```csharp
static void LoadEnvFile()
{
    var envVariables = FindAndParseEnvFile();
    
    foreach (var (key, value) in envVariables)
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}

static IReadOnlyList<(string Key, string Value)> FindAndParseEnvFile()
{
    return EnumerateDirectoriesUpward(new DirectoryInfo(Directory.GetCurrentDirectory()))
        .Select(dir => Path.Combine(dir.FullName, ".env"))
        .FirstOrDefault(File.Exists)
        is string envFile
            ? File.ReadAllLines(envFile)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .Select(ParseEnvLine)
                .Where(kvp => kvp.HasValue)
                .Select(kvp => kvp!.Value)
                .ToList()
            : Array.Empty<(string, string)>();
}

static IEnumerable<DirectoryInfo> EnumerateDirectoriesUpward(DirectoryInfo? dir)
{
    while (dir is not null)
    {
        yield return dir;
        dir = dir.Parent;
    }
}

static (string Key, string Value)? ParseEnvLine(string line)
{
    var sep = line.IndexOf('=');
    return sep > 0
        ? (line[..sep].Trim(), line[(sep + 1)..].Trim())
        : null;
}
```

**Benefits:**
- Pure functions `EnumerateDirectoriesUpward` and `ParseEnvLine` are testable
- Clear separation between finding file, parsing content, and setting environment variables
- LINQ composition shows intent declaratively

---

### Pattern 2: Imperative URL Building

#### HttpFhirBackend.cs (lines 46-50, 57-62, 69-74, 81-86, 94-98)

**Current Pattern - Imperative String List:**
```csharp
public async Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(
    string? name, string? gender, string? birthYearFrom, 
    string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
{
    var parts = new List<string> { "Patient?" };  // ← Mutable list
    if (!string.IsNullOrWhiteSpace(name)) 
        parts.Add($"name:contains={Uri.EscapeDataString(name)}");  // ← Imperative mutation
    if (!string.IsNullOrWhiteSpace(gender)) 
        parts.Add($"gender={Uri.EscapeDataString(gender)}");
    if (!string.IsNullOrWhiteSpace(generalPractitioner)) 
        parts.Add($"general-practitioner:contains={Uri.EscapeDataString(generalPractitioner)}");
    
    var url = parts[0] + string.Join("&", parts.Skip(1));  // ← Awkward reconstruction
    var entries = await FetchBundleEntries(url, ct);
    return entries.Select(MapPatient).Where(p => p is not null).Select(p => p!).ToList();
}
```

**Issue:**
- Mutable `parts` list accumulates query parameters
- Logic for building URL is scattered and hard to understand
- Same pattern duplicated across 5 search methods

**Recommendation - Functional URL Building:**
```csharp
static string BuildSearchUrl(string resourceType, params (string name, string? value)[] parameters) =>
    resourceType + "?" +
    string.Join("&", 
        parameters
            .Where(p => !string.IsNullOrWhiteSpace(p.value))
            .Select(p => $"{p.name}={Uri.EscapeDataString(p.value!)}"));

public async Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(
    string? name, string? gender, string? birthYearFrom, 
    string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
{
    var url = BuildSearchUrl("Patient",
        ("name:contains", name),
        ("gender", gender),
        ("general-practitioner:contains", generalPractitioner));
    
    var entries = await FetchBundleEntries(url, ct);
    return entries
        .Select(MapPatient)
        .Where(p => p is not null)
        .Select(p => p!)
        .ToList();
}
```

**Or with more advanced LINQ composition:**
```csharp
static string BuildFhirSearchUrl(
    string resourceType, 
    IReadOnlyDictionary<string, string?> parameters) =>
    $"{resourceType}?" +
    string.Join("&",
        parameters
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value))
            .Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value!)}"));

public async Task<IReadOnlyList<PatientRecord>> SearchPatientsAsync(
    string? name, string? gender, string? birthYearFrom, 
    string? birthYearTo, string? generalPractitioner, CancellationToken ct = default)
{
    var parameters = new Dictionary<string, string?>
    {
        ["name:contains"] = name,
        ["gender"] = gender,
        ["general-practitioner:contains"] = generalPractitioner
    };
    
    var url = BuildFhirSearchUrl("Patient", parameters);
    var entries = await FetchBundleEntries(url, ct);
    return entries
        .Select(MapPatient)
        .OfType<PatientRecord>()  // ← Better than Where().Select()
        .ToList();
}
```

---

### Pattern 3: Imperative Member Extraction

#### HttpFhirBackend.cs (lines 176-181)

**Current Pattern - Imperative Loop:**
```csharp
private static GroupRecord? MapGroup(JsonElement r)
{
    var members = new List<string>();  // ← Mutable list
    if (r.TryGetProperty("member", out var arr))
        foreach (var m in arr.EnumerateArray())  // ← Imperative loop
            if (m.TryGetProperty("entity", out var entity) && 
                entity.TryGetProperty("reference", out var rf))
                members.Add(rf.GetString()?.Replace("Patient/", "", StringComparison.Ordinal) ?? "");

    return new GroupRecord(Str(r, "id"), Str(r, "name"), Str(r, "description"), members);
}
```

**Recommendation - LINQ Transformation:**
```csharp
private static GroupRecord? MapGroup(JsonElement r)
{
    var members = r.TryGetProperty("member", out var memberArray)
        ? memberArray.EnumerateArray()
            .Where(m => m.TryGetProperty("entity", out var entity) && 
                       entity.TryGetProperty("reference", out var _))
            .Select(m => 
            {
                var entity = m.GetProperty("entity");
                var rf = entity.GetProperty("reference").GetString() ?? "";
                return rf.Replace("Patient/", "", StringComparison.Ordinal);
            })
            .ToList()
        : new List<string>();

    return new GroupRecord(
        Str(r, "id"), 
        Str(r, "name"), 
        Str(r, "description"), 
        members);
}
```

Or more elegantly:
```csharp
private static IReadOnlyList<string> ExtractGroupMembers(JsonElement groupElement) =>
    groupElement.TryGetProperty("member", out var memberArray)
        ? memberArray.EnumerateArray()
            .Where(m => m.TryGetProperty("entity", out _))
            .Select(m => m.GetProperty("entity"))
            .Where(e => e.TryGetProperty("reference", out _))
            .Select(e => e.GetProperty("reference").GetString() ?? "")
            .Select(ref => ref.Replace("Patient/", "", StringComparison.Ordinal))
            .ToList()
        : Array.Empty<string>();

private static GroupRecord? MapGroup(JsonElement r) =>
    new(
        Str(r, "id"),
        Str(r, "name"),
        Str(r, "description"),
        ExtractGroupMembers(r));
```

---

## Category 7: Null Returns vs Option<T> Pattern

### Summary
**Status: SIGNIFICANT OPPORTUNITY** - Multiple mapping functions return nullable domain objects and use `.Where(x => x is not null).Select(x => x!)` pattern that could use Option<T>.

### Pattern 1: Nullable Object Return

#### HttpFhirBackend.cs (lines 185-202)

**Current Pattern - Imperative Null Handling:**
```csharp
private static PatientRecord? MapPatient(JsonElement r)
{
    var name = "";  // ← Side effect / mutable state
    if (r.TryGetProperty("name", out var names) && names.GetArrayLength() > 0)
    {
        var n = names[0];
        var family = Str(n, "family");
        var given = n.TryGetProperty("given", out var g) && g.GetArrayLength() > 0 
            ? g[0].GetString() ?? "" 
            : "";
        name = $"{given} {family}".Trim();  // ← Mutation
    }

    var birthYear = 0;  // ← Mutable default
    if (r.TryGetProperty("birthDate", out var bd) && bd.GetString() is { Length: >= 4 } bds)
        int.TryParse(bds[..4], out birthYear);  // ← Out parameter mutation

    return new PatientRecord(
        Str(r, "id"), 
        name, 
        Str(r, "gender"), 
        birthYear,
        Ref(r, "managingOrganization"), 
        Ref(r, "generalPractitioner"), 
        "", 
        "");
}

// Usage:
var patients = entries
    .Select(MapPatient)
    .Where(p => p is not null)  // ← Null filtering
    .Select(p => p!)  // ← Non-null assertion after filtering
    .ToList();
```

**Issues:**
- `MapPatient` returns nullable `PatientRecord?`, but domain logic shouldn't produce invalid states
- Either patient is successfully mapped OR not mapped at all (should not return default/invalid patient)
- `.Where(p => p is not null).Select(p => p!)` pattern is verbose and suggests design error
- Mutable local variables (`name`, `birthYear`) within function body

**Recommendation - Option<T> Pattern:**

First, define Option<T> monad:
```csharp
public readonly struct Option<T>
{
    private readonly T _value;
    private readonly bool _hasValue;
    
    private Option(T value) { _value = value; _hasValue = true; }
    public static Option<T> Some(T value) => new(value);
    public static Option<T> None => default;
    
    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
        _hasValue ? some(_value) : none();
    
    public Option<TResult> Map<TResult>(Func<T, TResult> f) =>
        _hasValue ? Option<TResult>.Some(f(_value)) : Option<TResult>.None;
    
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> f) =>
        _hasValue ? f(_value) : Option<TResult>.None;
    
    public T GetValueOrDefault(T defaultValue) =>
        _hasValue ? _value : defaultValue;
    
    public bool TryGetValue(out T value)
    {
        value = _value;
        return _hasValue;
    }
}
```

Then refactor mapper:
```csharp
private static Option<PatientRecord> TryMapPatient(JsonElement r) =>
    ExtractPatientName(r)
        .Map(name => new PatientRecord(
            Str(r, "id"),
            name,
            Str(r, "gender"),
            ExtractBirthYear(r),
            Ref(r, "managingOrganization"),
            Ref(r, "generalPractitioner"),
            "",
            ""));

private static Option<string> ExtractPatientName(JsonElement r) =>
    r.TryGetProperty("name", out var names) && names.GetArrayLength() > 0
        ? Option<string>.Some(BuildPatientName(names[0]))
        : Option<string>.None;

private static string BuildPatientName(JsonElement nameElement)
{
    var family = Str(nameElement, "family");
    var given = nameElement.TryGetProperty("given", out var g) && g.GetArrayLength() > 0
        ? g[0].GetString() ?? ""
        : "";
    return $"{given} {family}".Trim();
}

private static int ExtractBirthYear(JsonElement r) =>
    r.TryGetProperty("birthDate", out var bd) && bd.GetString() is { Length: >= 4 } bds &&
    int.TryParse(bds[..4], out var year)
        ? year
        : 0;

// Usage - no more null assertions:
var patients = entries
    .Select(TryMapPatient)
    .Where(opt => opt.TryGetValue(out _))
    .Select(opt => opt.Match(p => p, () => throw new InvalidOperationException()))
    .ToList();

// Or more elegantly with helper:
var patients = entries
    .Select(TryMapPatient)
    .Where(opt => opt.Match(_ => true, () => false))
    .Select(opt => opt.Match(p => p, () => throw new InvalidOperationException()))
    .ToList();

// Or collect only successful mappings:
var patients = entries
    .Select(TryMapPatient)
    .Aggregate(
        new List<PatientRecord>(),
        (list, opt) => opt.Match(
            patient => { list.Add(patient); return list; },
            () => list));
```

---

### Pattern 2: Helper Methods Returning Empty String for Missing Values

#### HttpFhirBackend.cs (lines 159-172)

**Current Pattern - Silent Defaults:**
```csharp
private static string Str(JsonElement el, string prop) =>
    el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";  // ← Returns "" when missing

private static string Ref(JsonElement el, string prop) =>
    el.TryGetProperty(prop, out var v) && v.TryGetProperty("reference", out var r) 
        ? r.GetString() ?? "" 
        : "";  // ← Returns "" when missing

private static string Coding(JsonElement el, string prop, string field = "code")
{
    if (!el.TryGetProperty(prop, out var cc)) return "";  // ← Silent default
    if (cc.TryGetProperty("coding", out var arr) && arr.GetArrayLength() > 0)
        return arr[0].TryGetProperty(field, out var v) ? v.GetString() ?? "" : "";
    if (cc.TryGetProperty("text", out var text)) return text.GetString() ?? "";
    return "";
}
```

**Issue:**
- Functions return empty string whether property was truly missing or legitimately empty
- Mappers cannot distinguish between "unmapped" and "mapped to empty string"
- Leads to silent data loss in mapping

**Recommendation - Option<string> Pattern:**
```csharp
private static Option<string> TryStr(JsonElement el, string prop) =>
    el.TryGetProperty(prop, out var v) && v.GetString() is { Length: > 0 } str
        ? Option<string>.Some(str)
        : Option<string>.None;

private static Option<string> TryRef(JsonElement el, string prop) =>
    el.TryGetProperty(prop, out var v) && 
    v.TryGetProperty("reference", out var r) && 
    r.GetString() is { Length: > 0 } refStr
        ? Option<string>.Some(refStr)
        : Option<string>.None;

private static Option<string> TryCoding(
    JsonElement el, 
    string prop, 
    string field = "code") =>
    el.TryGetProperty(prop, out var cc)
        ? (cc.TryGetProperty("coding", out var arr) && arr.GetArrayLength() > 0 &&
           arr[0].TryGetProperty(field, out var v) && v.GetString() is { Length: > 0 } coding)
            ? Option<string>.Some(coding)
            : (cc.TryGetProperty("text", out var text) && text.GetString() is { Length: > 0 } t)
                ? Option<string>.Some(t)
                : Option<string>.None
        : Option<string>.None;

// Usage in mappers:
private static EncounterRecord? MapEncounter(JsonElement r) =>
    (
        id: TryStr(r, "id"),
        type: TryCoding(r, "type"),
        typeDisplay: TryCoding(r, "type", "display")
    ) switch
    {
        (var id, var type, _) when id.TryGetValue(out var idVal) && 
                                   type.TryGetValue(out var typeVal)
            => new EncounterRecord(
                idVal,
                Ref(r, "subject"),
                typeVal,
                TryCoding(r, "type", "display").Match(d => d, () => ""),
                TryCoding(r, "reasonCode").Match(rc => rc, () => ""),
                Ref(r, "participant"),
                Ref(r, "location"),
                Str(r, "period"),
                Str(r, "status")),
        _ => null
    };
```

---

## Summary Table: All FP Improvements

| Category | File | Lines | Issue | Severity | Complexity |
|----------|------|-------|-------|----------|------------|
| Domain Models | AgentConfigModels.cs | 18 | Mutable Dictionary for KeywordHints | Low | Low |
| Error Handling | AgentRunner.cs | 74-81 | Exception catch-rethrow without Result<T> | High | Medium |
| Error Handling | HttpFhirBackend.cs | 31-35, 150-154 | Catch-return null without error context | High | Medium |
| I/O Separation | FhirToolbox.cs | 14-88 | HTTP I/O + serialization coupling | Medium | Medium |
| I/O Separation | HttpFhirBackend.cs | 127-155 | HTTP fetch + JSON extraction coupling | Medium | Medium |
| Loops | Program.cs | 8-34 | Imperative directory traversal + file parsing | Medium | High |
| Loops | HttpFhirBackend.cs | 46-50, 57-62, etc. | Imperative URL building with mutable list | Medium | Low |
| Loops | HttpFhirBackend.cs | 176-181 | Imperative member extraction | Low | Low |
| Null Handling | HttpFhirBackend.cs | 185-202 | Nullable return + null filtering pattern | Medium | High |
| Null Handling | HttpFhirBackend.cs | 159-172 | Helper methods returning empty strings | Medium | High |

---

## Implementation Priority

### Phase 1 (High Impact, Lower Effort)
1. **Collection Immutability** - RouterProfile KeywordHints: Add `IReadOnlyDictionary<string, IReadOnlyList<string>>`
2. **Functional URL Building** - HttpFhirBackend search methods: Replace imperative List<string> with LINQ composition

### Phase 2 (High Impact, Medium Effort)
3. **Result<T> Monad** - Define Result<T> abstraction in shared utilities
4. **Error Handling** - Refactor AgentRunner and HttpFhirBackend exception handling to use Result<T>

### Phase 3 (Medium Impact, Higher Effort)
5. **Option<T> Pattern** - Define Option<T>, refactor mappers to TryMapX methods
6. **I/O Separation** - Extract pure transformation functions from FhirToolbox and HttpFhirBackend

### Phase 4 (Lower Priority)
7. **Imperative Loop Refactoring** - Program.cs LoadEnvFile using LINQ composition
8. **Pattern Matching** - Enhanced switch expressions in mapper functions

---

## Conclusion

The FHIR Copilot API demonstrates a **strong functional programming foundation** in domain models and contracts, with consistent use of sealed records and immutable collections. The main improvement opportunities cluster in **I/O boundary layers** (HttpFhirBackend, FhirToolbox) and **error handling** (exception-based flow), where introducing Result<T> and Option<T> abstractions would significantly improve code safety, testability, and composability.

Implementing these improvements would align the entire codebase with the functional programming principles already established in the project and documented in `.claude/rules/functional-programming.md`.
