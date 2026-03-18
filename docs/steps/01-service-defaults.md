# Step 1: Create ServiceDefaults Project

## What
Create `src/FhirCopilot.ServiceDefaults/` — an Aspire shared project that centralizes OpenTelemetry (traces + metrics + logs), health checks, and HTTP client resilience. This project is additive; nothing references it yet.

## New Files
- `src/FhirCopilot.ServiceDefaults/FhirCopilot.ServiceDefaults.csproj`
- `src/FhirCopilot.ServiceDefaults/Extensions.cs`

## Modified Files
- `Directory.Packages.props` — added OTEL and resilience package versions
- `fhir-copilot-agentframework-starter.sln` — added project under src/ folder

## Key Design Decisions
- **Namespace `Microsoft.Extensions.Hosting`** — extension methods are auto-discoverable on `IHostApplicationBuilder` without extra `using` statements
- **OTLP exporter is conditional** — only activates when `OTEL_EXPORTER_OTLP_ENDPOINT` env var is set, so local dev runs fine without a collector
- **`<IsAspireSharedProject>true`** — signals Aspire tooling this is a cross-cutting concerns library, not a standalone service
- **`FrameworkReference Include="Microsoft.AspNetCore.App"`** — needed because this is a class library (not Web SDK) that uses ASP.NET Core types like `WebApplication`

## Validation Checks
1. `dotnet build src/FhirCopilot.ServiceDefaults/` — compiles clean (0 warnings, 0 errors)
2. `dotnet build` — full solution compiles clean
3. `dotnet test` — all 35 existing tests pass
4. Extensions.cs contains: `AddServiceDefaults()`, `ConfigureOpenTelemetry()`, `AddDefaultHealthChecks()`, `MapDefaultEndpoints()`
5. csproj has `<IsAspireSharedProject>true</IsAspireSharedProject>`
6. OTLP exporter is conditional on `OTEL_EXPORTER_OTLP_ENDPOINT` env var
