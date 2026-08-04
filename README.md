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
  buffering) while extracting token usage per `(Provider, OperationType)` via a pluggable `IUsageExtractor`;
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

This starts persistent Postgres, dev-mode Vault, and Keycloak containers, then the API, and prints an Aspire
dashboard URL (traces/metrics/logs for every service). Before the first run, set the local OIDC secrets:

```powershell
dotnet user-secrets set "Parameters:oidc-client-secret" "<random-development-secret>" --project src/AiProxy.AppHost
dotnet user-secrets set "Parameters:keycloak-admin-password" "<local-keycloak-admin-password>" --project src/AiProxy.AppHost
dotnet user-secrets set "Parameters:proxy-admin-password" "<local-proxy-admin-password>" --project src/AiProxy.AppHost
```

Open the API or Vite URL and sign in to the `ai-proxy-local` realm as the development-only user `proxy-admin`.
Keycloak imports the realm only into a fresh `keycloak-data` volume; later starts reuse it without overwriting
changed credentials. To reset only local identity data, stop the AppHost, remove its Keycloak container, run
`docker volume rm keycloak-data`, and restart the AppHost. Postgres and Vault are unaffected. Never reuse these
development credentials outside local testing. The seeded `proxy-admin` subject is pinned so resetting Keycloak
does not create a different OIDC identity or collide with its Consumer row in persistent Postgres.

On first run:

- The database schema is **not** auto-migrated — apply it once with:
  ```powershell
  dotnet ef database update --project src/AiProxy.Infrastructure --startup-project src/AiProxy.Infrastructure
  ```
  (point `ConnectionStrings__aiproxydb` at whatever port Aspire assigned Postgres, visible in the dashboard).
- The admin API key is auto-generated on first read and logged **once** by the API process
  (`No admin API key existed in the secret store; generated a new one...`) — save it, it can't be retrieved
  again (only its hash is stored).
- `GET /health/live` is always healthy; `GET /health/ready` reports Postgres/Vault reachability individually.

Aspire keeps the Postgres, Vault, and Keycloak containers running across AppHost restarts. Postgres and Keycloak
also use named storage volumes. Vault runs in dev mode and stores secrets in memory, so its data survives an
AppHost restart only while that persistent container still exists; deleting/recreating the container or restarting
Docker loses those dev-mode secrets.

### Option B — Docker Compose

```powershell
docker compose up -d
```

Brings up Postgres, dev-mode Vault, and the API (built from `src/AiProxy.Api/Dockerfile`) on `localhost:8080`.
Apply migrations the same way as above, pointing at `localhost:5432`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in
`docker-compose.yml` if you have an external OTLP collector; leave it blank to disable OTLP export.

## Frontend (`src/AiProxy.Web`)

A React + TypeScript + FluentUI admin/ops UI (Config, Playground, Usage dashboard — see
[specs/iterations/iteration-3/tasks.md](specs/iterations/iteration-3/tasks.md)), served by the **same**
container as the API — no separate frontend host/deployment.

- **Pages**: Config → Models, Endpoints, API Routes, Consumers; Playground → Chat
  (streaming/non-streaming), Images, Realtime Voice (push-to-talk microphone input, streaming speaker output, and
  raw Realtime event diagnostics), and Speech (file/microphone transcription plus text-to-speech playback and
  download); Dashboard → Usage (filters, table, token/request charts).
- **Production / container path**: `npm run build` (or just `docker build`/`docker compose up`, which run it
  for you) writes the compiled static files straight into `src/AiProxy.Api/wwwroot`, which `AiProxy.Api` then
  serves at `/` (with a client-side-routing fallback to `index.html`) alongside its existing `/api/*`,
  `/openai/*`, `/anthropic/*` routes. `wwwroot`'s contents are git-ignored — they're a build artifact, not
  source (same convention as `logs/`/`test-results/`).
- **Local frontend development** (hot reload, no rebuild loop):
  ```powershell
  # Terminal 1 — the API (Aspire or plain `dotnet run`)
  dotnet run --project src/AiProxy.Api
  # Terminal 2 — Vite dev server; proxies /api, /openai, /anthropic, /health to the API above
  cd src/AiProxy.Web
  npm install
  npm run dev
  ```
  Open the printed Vite URL (not the API's own port) — `vite.config.ts`'s dev proxy forwards backend calls so
  the frontend never needs its own copy of the auth/CORS story.
- On first load, sign in through the configured OIDC provider. React receives only minimal session metadata;
  provider tokens and the confidential-client credential remain on the server. Config, Usage, Chat, and Images
  use the encrypted same-origin session. Voice exchanges that session for a 30-second, single-use Realtime ticket.
- Voice requires a browser with Web Audio, AudioWorklet, and microphone support. Connect to a configured OpenAI
  Realtime model, hold **Hold to talk** while speaking, and release to commit the turn. The browser requests
  microphone permission on first use and plays response audio automatically. Microphone access requires HTTPS or
  a loopback development origin such as `localhost`.
- Every model has one or more administrator-managed capabilities: `Chat`, `ImageGeneration`, `Realtime`,
  `SpeechToText`, and `TextToSpeech`. Playground selectors show only models with the matching capability. These
  values are discovery metadata; provider and operation compatibility are still enforced by routing.
- Speech is separate from Realtime Voice. Speech-to-text sends a selected audio file or an ephemeral microphone
  recording to `/openai/v1/audio/transcriptions`; text-to-speech sends text and a voice to
  `/openai/v1/audio/speech`, then offers browser playback and download. Audio is not persisted by AI Proxy.
  Microphone recording requires `MediaRecorder`, media-device support, and a secure or loopback origin. Uploads
  remain subject to the API server's configured request-body limit.
- Frontend tests: `cd src/AiProxy.Web && npm test` (Vitest + React Testing Library).

## OIDC provider setup

AI Proxy uses generic OIDC discovery and a server-side Authorization Code + PKCE flow. Register one confidential
web client per deployment with exact callback and post-logout URIs. The provider must supply stable `iss` + `sub`,
the configured client ID in `aud`, and the configured username claim in the ID token or UserInfo response. Restrict
who may sign in to this client at the provider: every admitted browser user is an AI Proxy administrator.

Configure the provider only in `src/AiProxy.AppHost/appsettings.json`. AppHost resolves
`ClientSecretParameter` from an Aspire secret parameter and passes `Authentication__Oidc__*` values only to the
API. The frontend receives no provider settings. `Authority` must be HTTPS except for loopback development,
`Scopes` must contain `openid`, and `AllowedSigningAlgorithms` is an explicit asymmetric allow-list.

OIDC users become Consumers on their first inference request. Their immutable identity is exact issuer + subject;
the username claim is mutable display and chargeback metadata. A username collision is rejected rather than merged.
Disabling an OIDC Consumer blocks local inference but does not disable the upstream identity. OIDC Consumers have no
Consumer API key and cannot rotate one. External clients continue to authenticate with Consumer API keys.

The static Admin API Key is a disabled-by-default migration/break-glass path. Set
`Authentication__AdminApiKeyFallbackEnabled=true` only during a controlled migration. Browser sessions require the
antiforgery header on unsafe requests; API-key clients do not. Before onboarding another provider, verify discovery,
exact issuer, confidential Code + PKCE support, JWKS/signing-algorithm overlap, client audience, username source,
callback URIs, key rotation, session expiry, and optional logout behavior. Providers without discovery/JWKS, stable
`iss` + `sub`, or a usable username source require a separately designed adapter.

Common failures are usually exact-value mismatches: compare the discovery `issuer`, registered client ID, callback
URI including scheme/host/port/path, username claim location, and secure-cookie behavior at the TLS-terminating
proxy. Production reverse proxies must preserve the external HTTPS scheme and host through forwarded headers.

## Example requests

Create a consumer (admin-only) and use its key for ingress calls:

```powershell
# Create a consumer — the plaintext API key is only ever shown in this response.
curl -X POST http://localhost:8080/api/v1/config/consumers `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"my-team"}'

# Create reusable connectivity. Save the endpoint id; its plaintext API key is never returned.
curl -X POST http://localhost:8080/api/v1/config/endpoints `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"primary-foundry","baseUrl":"https://my-foundry-resource.example.com","authMode":"ApiKey","apiKeyPlaintext":"sk-...","timeoutSeconds":100,"maxRetries":2}'

# Create a reusable provider API surface. The migration also supplies deterministic built-in routes.
curl -X POST http://localhost:8080/api/v1/config/api-routes `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"custom-foundry-openai","provider":"OpenAI","pathPrefix":"/openai/v1"}'

# Register a model and assign an endpoint + API route combination.
curl -X POST http://localhost:8080/api/v1/config/models `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"gpt-4o","strategy":"Priority","capabilities":["Chat","Realtime"]}'
curl -X POST http://localhost:8080/api/v1/config/models/gpt-4o/endpoints `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"endpointId":"<endpoint-id>","apiRouteId":"<api-route-id>","priority":1,"enabled":true}'
```

`Endpoint` owns only the host, credentials, timeout, and retry settings. `ApiRoute` owns provider behavior and
the normalized upstream prefix. A model assignment combines both, so one Foundry endpoint can be reused with the
built-in Foundry OpenAI (`/openai/v1`) and Foundry Microsoft AI (`/mai/v1`) routes. OpenAI uses bearer
authorization, Microsoft AI uses `api-key`, and Anthropic uses `x-api-key`.

The Iteration 7 migration creates four built-in routes and backfills existing assignments and Responses affinity.
Legacy Azure OpenAI and OpenAI-compatible assignments map to the Foundry OpenAI route; Native OpenAI and Anthropic
map to their matching built-ins. Downgrade is blocked if a model uses the same endpoint through multiple routes,
because the legacy schema cannot represent that state.

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
