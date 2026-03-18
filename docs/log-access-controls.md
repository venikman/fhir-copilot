# Log Access Controls & Encryption at Rest

## Status: future — not yet implemented

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
- [ ] Add PHI scrubbing middleware (see `docs/phi-log-scrubbing.md`) as defense-in-depth
- [ ] Ship logs to an ops-only sink (Datadog, Application Insights, etc.)
- [ ] Set retention policy: 30-90 days
- [ ] Verify: run a test request with PHI, confirm no PHI in ops logs

### For Tier 2 (HIPAA audit trail)

- [ ] Implement `IAuditLogger` (see `docs/hipaa-audit-logging.md`)
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
