# HIPAA Compliance: Logging & PHI Protection

## Status: future — not yet implemented

This document covers three related concerns for HIPAA-compliant logging:
1. [Audit logging](#hipaa-audit-logging) — tracking who accessed what patient data
2. [Log access controls & encryption](#log-access-controls--encryption-at-rest) — protecting log storage
3. [PHI log scrubbing](#phi-log-scrubbing-middleware) — preventing PHI leaks into operational logs

---

## HIPAA Audit Logging

## Problem

HIPAA requires covered entities to track **who accessed what patient data, when, and why**.
This is fundamentally different from operational logging (latency, errors, throughput):

| Concern | Operational logs | HIPAA audit trail |
|---------|-----------------|-------------------|
| Purpose | Debug, monitor, alert | Compliance, investigation, breach response |
| Contains PHI? | Should not | Must — it records which patient data was accessed |
| Retention | Days to weeks | **6 years minimum** (HIPAA §164.530(j)) |
| Access | Dev/ops teams | Compliance/privacy officers only |
| Format | Unstructured text | Structured, queryable, tamper-evident |

## What to log in the audit trail

Every FHIR data access should produce an audit entry:

```json
{
  "timestamp": "2026-03-17T14:22:01Z",
  "eventType": "fhir-read",
  "userId": "dr-rao@northwind.health",
  "userRole": "physician",
  "action": "read",
  "resourceType": "Patient",
  "resourceId": "patient-0001",
  "patientId": "patient-0001",
  "agentUsed": "clinical",
  "query": "[hash or reference — not plaintext]",
  "outcome": "success",
  "sourceIp": "10.0.1.42",
  "sessionId": "thread-abc123"
}
```

### FHIR AuditEvent resource

FHIR R4 defines an `AuditEvent` resource (based on IHE ATNA / DICOM Audit Message)
that standardizes this format. Consider writing audit entries as FHIR AuditEvent
resources to the same FHIR server or a dedicated audit repository.

Spec: https://hl7.org/fhir/R4/auditevent.html

## Implementation approach for this project

### Step 1: Define an audit event model

```csharp
public sealed record ClinicalAuditEvent(
    DateTime Timestamp,
    string EventType,       // "copilot-query", "fhir-read", "fhir-search", "bulk-export"
    string UserId,          // From auth context (once auth is added)
    string Action,          // "read", "search", "export"
    string ResourceType,    // "Patient", "Encounter", etc.
    string? ResourceId,     // Specific resource, if applicable
    string? PatientId,      // Patient scope
    string AgentUsed,       // "clinical", "lookup", etc.
    string Outcome,         // "success", "error", "denied"
    string ThreadId);
```

### Step 2: Add an `IAuditLogger` interface

```csharp
public interface IAuditLogger
{
    Task LogAsync(ClinicalAuditEvent auditEvent, CancellationToken ct = default);
}
```

### Step 3: Instrument at the right layer

The audit point is in `FhirToolbox` — every tool call that reads patient data
should emit an audit event. This is better than auditing at the HTTP layer
because it captures the actual FHIR resources accessed, not just the copilot request.

```csharp
// In FhirToolbox.SearchPatients:
await _auditLogger.LogAsync(new ClinicalAuditEvent(
    Timestamp: DateTime.UtcNow,
    EventType: "fhir-search",
    UserId: currentUser,      // from auth context
    Action: "search",
    ResourceType: "Patient",
    ResourceId: null,
    PatientId: patientId,
    AgentUsed: "contextual",  // resolved from current agent context
    Outcome: "success",
    ThreadId: currentThread));
```

### Step 4: Choose a sink

| Sink | Pros | Cons |
|------|------|------|
| Append-only database table | Queryable, tamper-evident with checksums | Must manage retention, encryption |
| FHIR AuditEvent resources | Standards-based, interoperable | Adds load to FHIR server |
| Cloud audit service (AWS CloudTrail, Azure Monitor) | Managed retention, encryption, access controls | Vendor lock-in |
| Dedicated log stream (separate from ops logs) | Isolation is simple | Must build query/retention yourself |

For this project, starting with a separate append-only table or a dedicated
log file is sufficient. The key is **separation from operational logs**.

## What NOT to do

- Do not put audit events in the same log stream as operational logs
- Do not allow dev teams to query audit logs in normal workflows
- Do not store the full query text — store a hash or reference ID, with
  the plaintext in the audit-only store
- Do not skip audit logging for stub mode — the access pattern is the same

## References

- HIPAA §164.312(b) — Audit controls requirement
- HIPAA §164.530(j) — 6-year retention requirement
- FHIR AuditEvent: https://hl7.org/fhir/R4/auditevent.html
- IHE ATNA profile: https://profiles.ihe.net/ITI/TF/Volume1/ch-9.html
- HHS audit guidance: https://www.hhs.gov/hipaa/for-professionals/security/guidance/index.html

---

## Log Access Controls & Encryption at Rest

## Problem

Logs that contain PHI (audit trail) or could be combined to reconstruct PHI
(operational logs with timestamps + resource types) need:

1. **Encryption at rest** — logs stored on disk or in cloud storage must be encrypted
2. **Access controls** — only authorized personnel can read audit logs
3. **Tamper evidence** — audit logs should be append-only and detectable if modified
4. **Retention policies** — automatic lifecycle management (6 years for HIPAA audit logs)

## The two-tier model

```
                    ┌─────────────────────────┐
                    │    Copilot API Server    │
                    └──────┬──────────┬────────┘
                           │          │
              Operational  │          │  HIPAA Audit
              Logs         │          │  Events
                           ▼          ▼
                    ┌────────┐  ┌──────────┐
                    │  Tier 1 │  │  Tier 2   │
                    │  Ops    │  │  Audit    │
                    └────────┘  └──────────┘
                    PHI: NO      PHI: YES
                    Access: Devs Access: Compliance
                    Retain: 30d  Retain: 6 years
                    Encrypt: Yes Encrypt: Yes + KMS
```

### Tier 1: Operational logs (no PHI)

- **Who reads them:** Developers, SREs, on-call engineers
- **What's in them:** Request latency, error rates, agent routing decisions,
  tool call counts, status codes, thread IDs (no patient data)
- **Retention:** 30-90 days (enough for debugging)
- **Encryption:** Standard encryption at rest (cloud default)
- **Tools:** Datadog, Application Insights, CloudWatch, Seq, Serilog file sink

### Tier 2: HIPAA audit logs (contains PHI references)

- **Who reads them:** Privacy officers, compliance team, security investigators
- **What's in them:** Who accessed which patient, when, via which agent, outcome
- **Retention:** 6 years minimum (HIPAA §164.530(j))
- **Encryption:** Customer-managed keys (AWS KMS, Azure Key Vault, GCP CMEK)
- **Tamper evidence:** Append-only storage, integrity checksums, or
  immutable storage (e.g. AWS S3 Object Lock, Azure immutable blob storage)
- **Access:** IAM policies restricting to compliance role; access itself is audited

## Implementation checklist

### For Tier 1 (operational logs)

- [ ] Configure ASP.NET Core logging to `Warning` level in production
  (suppresses framework `Information`/`Debug` logs that may contain request details)
- [ ] Add PHI scrubbing middleware (see [#phi-log-scrubbing-middleware](#phi-log-scrubbing-middleware)) as defense-in-depth
- [ ] Ship logs to an ops-only sink (Datadog, Application Insights, etc.)
- [ ] Set retention policy: 30-90 days
- [ ] Verify: run a test request with PHI, confirm no PHI in ops logs

### For Tier 2 (HIPAA audit trail)

- [ ] Implement `IAuditLogger` (see [#hipaa-audit-logging](#hipaa-audit-logging))
- [ ] Ship audit events to a separate, isolated store
- [ ] Enable encryption with customer-managed keys
- [ ] Enable append-only / immutable storage
- [ ] Restrict read access to compliance IAM role
- [ ] Set retention policy: 6 years, with lifecycle transitions
  (e.g. move to cold storage after 1 year)
- [ ] Audit the audit: log who queries the audit trail

### Cloud-specific options

| Feature | AWS | Azure | GCP |
|---------|-----|-------|-----|
| Audit sink | CloudTrail + S3 | Azure Monitor + Blob | Cloud Audit Logs + GCS |
| Encryption | KMS (CMK) | Key Vault (CMK) | CMEK |
| Immutability | S3 Object Lock | Immutable blob | Bucket Lock |
| Access control | IAM + Resource Policy | RBAC + Private Endpoint | IAM + VPC-SC |
| Retention | S3 Lifecycle | Lifecycle Management | Object Lifecycle |

## ASP.NET Core configuration sketch

```json
// appsettings.Production.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "System.Net.Http.HttpClient": "Warning",
      "FhirCopilot": "Information"
    }
  }
}
```

This keeps framework noise (which may contain request details) at `Warning`
while allowing the application's own structured `Information` logs through.

## Testing

1. **PHI leak test:** Send a request containing known PHI markers, capture all log
   output, assert no PHI appears in Tier 1 logs
2. **Audit completeness test:** Send a request that triggers FHIR reads, assert
   corresponding audit events were written to Tier 2
3. **Access control test:** Attempt to read Tier 2 logs with a dev-role credential,
   assert access denied
4. **Retention test:** Verify lifecycle policies are configured correctly via
   infrastructure-as-code (Terraform, Pulumi, Bicep)

## References

- HIPAA §164.312(a)(2)(iv) — Encryption standard
- HIPAA §164.312(b) — Audit controls
- HIPAA §164.312(c)(1) — Integrity controls
- FHIR Security spec: https://hl7.org/fhir/security.html
- Protected Health Information and Logging: https://www.loggly.com/blog/protected-health-information-logging/
- Datadog HIPAA-compliant log management: https://www.datadoghq.com/blog/hipaa-compliant-log-management/
- HIPAA best practices for developers: https://neon.com/blog/hipaa-best-practices-for-developers

---

## PHI Log Scrubbing Middleware

## Problem

Even with POST-based endpoints (which keep PHI out of URLs), PHI can still leak into
application logs through:
- Exception stack traces that include request body content
- Framework-level request/response logging (e.g. `ILogger` at `Debug`/`Trace` level)
- Middleware that logs deserialized objects (`CopilotRequest.Query` contains patient names)
- Third-party library logs (HTTP client logging of FHIR responses)

## What to scrub

| Pattern | Example | Regex |
|---------|---------|-------|
| Patient names | `Alice Carter` | Names are hard to regex — use allowlist/denylist from the FHIR backend |
| Patient IDs | `patient-0001` | `patient[-/]\w+` |
| MRNs | `MRN: 123456` | `MRN[:\s]*\d+` |
| Dates of birth | `1979-03-15` | ISO date patterns in clinical context |
| SSNs | `123-45-6789` | `\d{3}-\d{2}-\d{4}` |
| FHIR resource paths | `Patient/patient-0001` | `(Patient\|Encounter\|Condition)/[\w-]+` |

## Implementation approach

### Option A: ASP.NET Core middleware (recommended for this project)

```csharp
public sealed class PhiRedactionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PhiRedactionMiddleware> _logger;

    public PhiRedactionMiddleware(RequestDelegate next, ILogger<PhiRedactionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Wrap the response body to intercept logs written during request processing
        await _next(context);
    }
}
```

Key: don't try to scrub the actual response — scrub what goes into **log sinks**.

### Option B: Custom `ILoggerProvider` with redaction

Wrap the default logger provider with a decorator that runs regex replacement
on every log message before it reaches the sink:

```csharp
public sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;
    private static readonly Regex[] Patterns = { /* SSN, MRN, patient ID patterns */ };

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        var redacted = RedactPatterns(message);
        _inner.Log(logLevel, eventId, redacted, exception,
            (s, _) => s);
    }

    private static string RedactPatterns(string input)
    {
        foreach (var pattern in Patterns)
            input = pattern.Replace(input, "[REDACTED]");
        return input;
    }
}
```

### Option C: Serilog destructuring policy

If using Serilog, create a `IDestructuringPolicy` that strips PHI fields
from structured log events before they reach any sink.

## What NOT to scrub

- FHIR resource **types** (Patient, Encounter) — these are not PHI
- Agent names, thread IDs, tool names — operational metadata
- Timestamps, status codes, latency — standard observability
- Error codes and categories — needed for debugging

## Testing strategy

1. Write integration tests that log a request containing known PHI patterns
2. Capture log output
3. Assert no PHI patterns appear in captured output
4. Run as part of CI

## References

- HIPAA-Compliant Logging in .NET: https://medium.com/bytehide/hipaa-compliant-logging-in-net-healthcare-applications-d3fd4bda121f
- AWS FHIR Works secure logging: https://github.com/aws-solutions/fhir-works-on-aws/blob/main/solutions/documentation/SECURE_LOGGING.md
- FHIR Security spec: https://hl7.org/fhir/security.html
