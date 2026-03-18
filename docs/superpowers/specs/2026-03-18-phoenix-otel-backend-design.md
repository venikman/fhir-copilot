# Design Spec: Arize Phoenix OTEL Backend on Fly.io

**Date:** 2026-03-18
**Status:** Approved

---

## Goal

Deploy Arize Phoenix as a self-hosted OTEL backend on Fly.io with persistent storage. Connect the existing `fhir-copilot` API to export traces, metrics, and logs to Phoenix.

## Architecture

- **Separate Fly.io app:** `fhir-copilot-phoenix` running `arizephoenix/phoenix` Docker image
- **Persistent volume:** 1GB mounted at `/data` for SQLite storage
- **Ports:** 6006 (UI, public HTTPS), 4317 (OTLP gRPC, internal only)
- **Networking:** `fhir-copilot` connects to Phoenix via Fly internal DNS (`http://fhir-copilot-phoenix.internal:4317`)
- **Region:** `iad` (same as `fhir-copilot` to minimize latency)

## Files to Create

- `fly.phoenix.toml` — Fly.io config for the Phoenix app

## Secrets to Set

On `fhir-copilot` (the API app):
- `OTEL_EXPORTER_OTLP_ENDPOINT=http://fhir-copilot-phoenix.internal:4317`
- `OTEL_EXPORTER_OTLP_PROTOCOL=grpc`

## Steps

1. Create `fly.phoenix.toml` with the Phoenix Docker image config
2. `fly apps create fhir-copilot-phoenix`
3. `fly volumes create phoenix_data --region iad --size 1 -a fhir-copilot-phoenix`
4. `fly deploy --config fly.phoenix.toml -a fhir-copilot-phoenix`
5. Set OTEL secrets on the API app: `fly secrets set OTEL_EXPORTER_OTLP_ENDPOINT=... -a fhir-copilot`
6. Verify traces appear in Phoenix UI

## No Code Changes Required

The existing OTEL pipeline in `Extensions.cs` activates the OTLP exporter automatically when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. No application code changes needed.
