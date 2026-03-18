# Step 8: Production OTEL Configuration

## What
Configure Fly.io and Docker for production OpenTelemetry export.

## Changes Made

### Dockerfile
- Added `COPY src/FhirCopilot.ServiceDefaults/FhirCopilot.ServiceDefaults.csproj` to the restore layer so Docker layer caching works with the new project reference

### fly.toml
- `OTEL_SERVICE_NAME=fhir-copilot` — identifies this service in traces
- `OTEL_RESOURCE_ATTRIBUTES=deployment.environment=production` — tags all telemetry as production
- `OTEL_EXPORTER_OTLP_ENDPOINT` — set via `fly secrets set` (not in fly.toml for security)

## How it works

The conditional in `ServiceDefaults/Extensions.cs`:
```csharp
var useOtlpExporter = !string.IsNullOrWhiteSpace(
    builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
if (useOtlpExporter)
    builder.Services.AddOpenTelemetry().UseOtlpExporter();
```

- **No env var** → OTEL instruments but doesn't export (safe for local dev without collector)
- **Env var set** → exports to the OTLP endpoint (Grafana, Honeycomb, Datadog, Jaeger, etc.)

## Deploy checklist

```bash
# Set the collector endpoint (required for traces to flow)
fly secrets set OTEL_EXPORTER_OTLP_ENDPOINT=https://your-collector:4317

# If your collector requires auth headers
fly secrets set OTEL_EXPORTER_OTLP_HEADERS="Authorization=Bearer your-token"

# Deploy
fly deploy
```

## What gets exported

Three trace sources:
1. `FhirCopilot.Agent` — copilot.request, copilot.stream spans (agent, thread, runner)
2. `FhirCopilot.GenAI` — GenAI semantic conventions (model, tokens, latency)
3. Auto-instrumentation — ASP.NET Core HTTP, outbound HttpClient, runtime metrics
