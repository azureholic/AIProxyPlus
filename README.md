# AI Proxy

A reverse proxy for OpenAI, Azure OpenAI, and Anthropic-compatible inference APIs, with priority + round-robin
load-balanced routing (failover on `429`/`5xx`), automatic affinity for stateful calls (OpenAI Responses API
`previous_response_id` chaining, and a WebSocket proxy for the OpenAI Realtime/voice API), streaming-safe usage
capture for chargeback, and a small configuration REST API — built as a clean, testable .NET 10 solution with
.NET Aspire for local orchestration.

Full design detail lives in [specs/architecture/architecture.md](specs/architecture/architecture.md) and the
phase-by-phase build log in [specs/iterations/iteration-1/tasks.md](specs/iterations/iteration-1/tasks.md).

## Architecture at a glance

- **Clean Architecture**: `AiProxy.Domain` (zero dependencies) → `AiProxy.Application` (ports + use cases) →
  `AiProxy.Infrastructure` (EF Core/Npgsql, Vault, routing, usage capture) → `AiProxy.Api` (composition root) →
  `AiProxy.AppHost` (.NET Aspire orchestration).
- **Routing/failover** is a custom `DirectHttpProxy`/`ProxyRoutingService`, not YARP's `IHttpForwarder` — YARP
  commits the response to the client as soon as headers arrive from upstream, which makes "inspect the
  response, then decide whether to retry the next endpoint" impossible. See architecture.md §9 for the full
  reasoning (confirmed empirically during implementation).
- **Streaming usage capture**: SSE responses are relayed incrementally (`SseStreamRelay`, no full-body
  buffering) while extracting token usage per `(ApiFamily, OperationType)` via a pluggable `IUsageExtractor`;
  a forced `stream_options.include_usage` chunk is stripped from the client-facing stream if the client itself
  never asked for it.
- **Secrets**: HashiCorp Vault (KV v2) holds outbound endpoint credentials, the admin API key hash, and the
  Consumer API key pepper — never the Consumer keys themselves (those are one-way hashed in Postgres).
- **Observability**: OpenTelemetry traces/metrics (custom `Meter("AiProxy")` + `ActivitySource("AiProxy")`,
  auto-instrumented ASP.NET Core/HttpClient/Npgsql) and Serilog (rolling file + OTLP), all wired through
  `AiProxy.ServiceDefaults`.

## Running locally

### Option A — .NET Aspire (recommended for local dev)

```powershell
dotnet run --project src/AiProxy.AppHost
```

This starts Postgres and a dev-mode Vault container, then the API, and prints an Aspire dashboard URL
(traces/metrics/logs for every service). On first run:

- The database schema is **not** auto-migrated — apply it once with:
  ```powershell
  dotnet ef database update --project src/AiProxy.Infrastructure --startup-project src/AiProxy.Infrastructure
  ```
  (point `ConnectionStrings__aiproxydb` at whatever port Aspire assigned Postgres, visible in the dashboard).
- The admin API key is auto-generated on first read and logged **once** by the API process
  (`No admin API key existed in the secret store; generated a new one...`) — save it, it can't be retrieved
  again (only its hash is stored).
- `GET /health/live` is always healthy; `GET /health/ready` reports Postgres/Vault reachability individually.

### Option B — Docker Compose

```powershell
docker compose up -d
```

Brings up Postgres, dev-mode Vault, and the API (built from `src/AiProxy.Api/Dockerfile`) on `localhost:8080`.
Apply migrations the same way as above, pointing at `localhost:5432`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in
`docker-compose.yml` if you have an external OTLP collector; leave it blank to disable OTLP export.

## Example requests

Create a consumer (admin-only) and use its key for ingress calls:

```powershell
# Create a consumer — the plaintext API key is only ever shown in this response.
curl -X POST http://localhost:8080/api/v1/config/consumers `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"my-team"}'

# Register a model deployment with one endpoint.
curl -X POST http://localhost:8080/api/v1/config/model-deployments `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"gpt-4o"}'
curl -X POST http://localhost:8080/api/v1/config/model-deployments/gpt-4o/endpoints `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"baseUrl":"https://my-openai-endpoint.example.com","apiFamily":"OpenAI","priority":1,"authMode":"ApiKey","apiKeyPlaintext":"sk-..."}'
```

OpenAI-shaped ingress:

```powershell
curl -X POST http://localhost:8080/openai/v1/chat/completions `
  -H "X-Api-Key: <consumer-key>" -H "Content-Type: application/json" `
  -d '{"model":"gpt-4o","messages":[{"role":"user","content":"hello"}]}'
```

Anthropic-shaped ingress:

```powershell
curl -X POST http://localhost:8080/anthropic/v1/messages `
  -H "X-Api-Key: <consumer-key>" -H "Content-Type: application/json" `
  -d '{"model":"claude-deployment","messages":[{"role":"user","content":"hello"}],"max_tokens":256}'
```

Query usage (admin-only):

```powershell
curl http://localhost:8080/api/v1/usage -H "X-Admin-Api-Key: <admin-key>"
```

OpenAPI/Scalar UI is available at `/openapi/v1.json` and `/scalar` in the Development environment.

## Running tests

```powershell
dotnet test --results-directory "test-results/$(Get-Date -Format yyyyMMdd-HHmmss)" --logger trx --collect:"XPlat Code Coverage"
```

- TRX results + Cobertura coverage land in `test-results/<timestamp>/` (git-ignored except a `.gitkeep`
  placeholder — this folder is for manual/CI runs, not committed output).
- The running API writes rolling logs to `logs/ai-proxy-.log` (also git-ignored beyond `.gitkeep`).
- `AiProxy.Domain.Tests`/`AiProxy.Application.Tests` are pure unit tests. `AiProxy.Infrastructure.Tests` uses
  Testcontainers for Postgres/Vault (requires Docker running). `AiProxy.Api.Tests` uses `WebApplicationFactory`
  + WireMock.Net fake backends (no Docker required) to exercise the full HTTP pipeline — priority/round-robin
  routing and failover, Responses API affinity, the Realtime WebSocket proxy, streaming usage capture, the
  config REST API, and authz enforcement.
- `AiProxy.ArchitectureTests` (NetArchTest) enforces the Clean Architecture dependency direction.
