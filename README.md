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

This starts persistent Postgres, local file-backed Vault, and Keycloak containers, then the API, and prints an Aspire
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

Aspire keeps the Postgres, Vault, and Keycloak containers running across AppHost restarts, and all three use named
storage volumes. Local Vault uses its file backend, so endpoint credentials survive AppHost, container, and Docker
restarts. To intentionally reset local Vault, stop the AppHost, remove its Vault container, delete `vault-data`,
and restart; this removes both Vault data and the local-only unseal key, and existing API-key endpoints must then
have their secrets set again.

### Option B — Docker Compose

```powershell
docker compose up -d
```

Brings up Postgres, local file-backed Vault, and the API (built from `src/AiProxy.Api/Dockerfile`) on `localhost:8080`.
Apply migrations the same way as above, pointing at `localhost:5432`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in
`docker-compose.yml` if you have an external OTLP collector; leave it blank to disable OTLP export.

### Routing configuration (Iteration 12)

Two previously-hardcoded values are now configurable via `appsettings`/environment variables — local dev needs
neither, both fall back to their existing defaults when unset:

| Key | Default | Purpose |
|---|---|---|
| `Routing:SessionAffinityTtl` | `00:30:00` (30 minutes) | How long a Responses API `previous_response_id` stays pinned to its originating endpoint (architecture.md §9/§17). |
| `Routing:UnhealthyCooldown` | `00:00:30` (30 seconds) | How long a same-tier endpoint that just failed is skipped on *subsequent* requests before becoming eligible again (the cross-request passive circuit breaker, architecture.md §9). |

### Least-privilege Postgres role (production)

Local Aspire/docker-compose dev intentionally uses one Postgres role for both migrations and runtime queries —
the same trade-off already made for dev-mode Vault. A real deployment should instead create two roles and set
`ConnectionStrings:aiproxydb-migrations` to the privileged one:

```sql
-- Privileged role: runs EF Core migrations (DDL) at startup/deploy time only.
CREATE ROLE aiproxy_migrator WITH LOGIN PASSWORD '...';
GRANT ALL PRIVILEGES ON DATABASE aiproxydb TO aiproxy_migrator;

-- Narrow role: what the running API actually connects as.
CREATE ROLE aiproxy_app WITH LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE aiproxydb TO aiproxy_app;
GRANT USAGE ON SCHEMA public TO aiproxy_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO aiproxy_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO aiproxy_app;
```

Point `ConnectionStrings:aiproxydb-migrations` at `aiproxy_migrator` and `ConnectionStrings:aiproxydb` at the
narrower `aiproxy_app` — the API applies migrations through the first connection at startup only, then serves
every runtime request through the second. Omitting `ConnectionStrings:aiproxydb-migrations` keeps today's
single-connection-string behavior unchanged.

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
API. Set `DisplayName` to the user-facing provider name, such as `Keycloak`, `Microsoft Entra ID`, `Okta`, or
`Auth0`; this non-secret label is the only provider setting exposed to the frontend. `Authority` must be HTTPS
except for loopback development, `Scopes` must contain `openid`, and `AllowedSigningAlgorithms` is an explicit
asymmetric allow-list.

OIDC users become Consumers on their first inference request. Their immutable identity is exact issuer + subject;
the username claim is mutable display and chargeback metadata. A username collision is rejected rather than merged.
Disabling a Consumer reversibly blocks local inference without changing its keys, OIDC identity, or model access.
Re-enabling restores access with the same configuration. Revocation is an irreversible security action; deletion is
available only after revocation and removes the Consumer record while preserving denormalized historical usage.
Disabling or revoking an OIDC Consumer does not disable the upstream identity. OIDC Consumers have no Consumer API
key and cannot rotate one. External clients may authenticate with Consumer API keys or the optional workload
bearer schemes below.

## Workload OIDC and managed identity

Machine callers can use a generic OIDC access token or a Microsoft Entra managed-identity token. Configure the
independent `Authentication:Workload:Oidc` or `Authentication:Workload:ManagedIdentity` section in
`src/AiProxy.Api/appsettings.json` (prefer environment variables for deployment values). Set an exact HTTPS
authority and audience, then enable the scheme. OIDC tokens require `aiproxy.invoke` in `scope`; managed identities
require the `AiProxy.Invoke` application role by default. Both values are configurable.

A valid token is not sufficient by itself. Pre-enroll the OIDC `iss` + `sub` pair or managed identity `tid` + `oid`
pair on the Consumers page or through the configuration API. Local disable/revoke, model allow-lists, and usage
attribution then work exactly as they do for API-key Consumers. Workload identities are never auto-provisioned.

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

Create a consumer (admin-only) and use either independently rotatable key for ingress calls:

```powershell
# Create a consumer — apiKey1 and apiKey2 are only ever shown in this response.
curl -X POST http://localhost:8080/api/v1/config/consumers `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"my-team"}'

# Pre-enroll a generic OIDC workload identity.
curl -X POST http://localhost:8080/api/v1/config/consumers/oidc `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"ci-agent","issuer":"https://identity.example.com","subject":"stable-subject"}'

# Pre-enroll a Microsoft Entra managed identity using token tid and oid claims.
curl -X POST http://localhost:8080/api/v1/config/consumers/managed-identity `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"name":"batch-worker","tenantId":"00000000-0000-0000-0000-000000000000","objectId":"11111111-1111-1111-1111-111111111111"}'

# Invoke with either configured workload scheme.
curl http://localhost:8080/openai/v1/models -H "Authorization: Bearer <access-token>"

# Rotate one slot without interrupting callers using the other slot.
curl -X PUT "http://localhost:8080/api/v1/config/consumers/my-team/rotate?slot=2" `
  -H "X-Admin-Api-Key: <admin-key>"

# Pause and restore access without changing either key or model permissions.
curl -X PUT http://localhost:8080/api/v1/config/consumers/my-team/disable `
  -H "X-Admin-Api-Key: <admin-key>"
curl -X PUT http://localhost:8080/api/v1/config/consumers/my-team/enable `
  -H "X-Admin-Api-Key: <admin-key>"

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

# Restrict the consumer to named models. With no entries, model access is unrestricted.
curl -X PUT http://localhost:8080/api/v1/config/consumers/my-team/allowed-models/gpt-4o `
  -H "X-Admin-Api-Key: <admin-key>"
```

`Endpoint` owns only the host, credentials, timeout, and retry settings. `ApiRoute` owns provider behavior and
the normalized upstream prefix. A model assignment combines both, so one Foundry endpoint can be reused with the
built-in Third Party OpenAI (`/openai/v1`) and Foundry Microsoft AI (`/mai/v1`) routes. OpenAI uses bearer
authorization, Microsoft AI uses `api-key`, and Anthropic uses `x-api-key`.

The Iteration 7 migration creates four built-in routes and backfills existing assignments and Responses affinity.
Legacy Azure OpenAI and OpenAI-compatible assignments map to the Third Party OpenAI route; Native OpenAI and Anthropic
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
