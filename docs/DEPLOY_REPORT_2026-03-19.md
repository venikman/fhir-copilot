# Deployment Report — 2026-03-19

## Summary

Five unpushed commits consolidate the copilot transport from HTTP REST + SSE + SignalR down to **SignalR-only**. This is a **breaking change** for any client using the HTTP endpoints.

## Commits (oldest → newest)

| SHA | Message | Breaking? |
|-----|---------|-----------|
| `8d242b4` | feat: add SignalR WebSocket hub + OpenAI-compatible local LLM runner | No — additive |
| `3c41025` | refactor: remove HTTP REST/SSE endpoints — SignalR is sole transport | **YES** |
| `bfc2072` | refactor: remove CopilotError/CopilotErrorResponse — unused after endpoint removal | No — internal |
| `ead3011` | docs: update architecture and tutorials for SignalR-only transport | No — docs only |
| `fa8aa5c` | test: port error-handling tests from HTTP to SignalR hub | No — tests only |

## What Was Removed

| Endpoint / Component | Was | Now |
|----------------------|-----|-----|
| `POST /api/copilot` | Synchronous JSON request/response | **Removed** → 404 |
| `POST /api/copilot/stream` | SSE streaming response | **Removed** → 404 |
| `SseWriter.cs` | SSE framing utility | **Deleted** |
| `CopilotError` / `CopilotErrorResponse` | REST error envelope records | **Deleted** |

## What Is Unchanged

| Endpoint / Component | Status |
|----------------------|--------|
| `GET /` | Updated splash text (cosmetic) |
| `GET /health` | Unchanged |
| `GET /alive` | Unchanged |
| `/hubs/copilot` (SignalR) | Unchanged — `SendQuery` + `StreamQuery` |
| `Dockerfile` | Unchanged |
| `fly.toml` | Unchanged |
| OTEL / tracing | Unchanged |
| Model fallback chain | Unchanged |

## Migration Guide

### For HTTP clients (curl, fetch, Postman)

**Before:**
```bash
curl -X POST http://localhost:5075/api/copilot \
  -H "Content-Type: application/json" \
  -d '{"query":"How many patients?","threadId":"t1"}'
```

**After:** Use a SignalR client. Hub URL: `/hubs/copilot`

- **One-shot:** `hub.InvokeAsync<CopilotResponse>("SendQuery", new CopilotRequest(...))`
- **Streaming:** `hub.StreamAsync<CopilotStreamEvent>("StreamQuery", new CopilotRequest(...))`

### For SSE clients (EventSource, fetch + ReadableStream)

SSE endpoint is gone. Use SignalR streaming instead — it provides the same `meta → delta* → done` event sequence via `IAsyncEnumerable<CopilotStreamEvent>`.

### Error handling changes

| Before (HTTP) | After (SignalR) |
|---------------|-----------------|
| HTTP 502 + `{"error":{"type":"upstream_error",...}}` | `HubException` message contains `"upstream_error:"` |
| HTTP 504 + `{"error":{"type":"timeout",...}}` | `HubException` message contains `"timeout:"` |
| HTTP 500 + `{"error":{"type":"internal_error",...}}` | `HubException` message contains `"internal_error:"` |
| HTTP 400 + `{"error":{"type":"invalid_request",...}}` | `HubException` message contains `"invalid_request:"` |

## Pre-Deploy Checklist

- [ ] Confirm no external clients depend on `POST /api/copilot` or `POST /api/copilot/stream`
- [ ] Confirm no monitoring/alerting targets the removed endpoints
- [ ] Confirm no load balancer health checks hit `/api/copilot` (current: `/health` via fly.toml — safe)
- [ ] Push to remote: `git push origin main`
- [ ] Deploy: `fly deploy`
- [ ] Post-deploy: verify `curl https://fhir-copilot.fly.dev/health` returns `Healthy`
- [ ] Post-deploy: verify `curl https://fhir-copilot.fly.dev/` shows updated splash text

## Risk Assessment

**Risk: LOW** — This is a demo/prototype project (see D-0013 in DECISIONS.md). The SignalR hub was added in `8d242b4` and the HTTP endpoints were exact duplicates. No known external consumers exist for the HTTP endpoints.

**Rollback:** `git revert 3c41025` would restore the HTTP endpoints without affecting other commits, since the endpoint removal is isolated in a single commit.
