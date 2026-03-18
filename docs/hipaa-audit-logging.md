# HIPAA Audit Logging (Structured)

## Status: future — not yet implemented

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
