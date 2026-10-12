# AIProxyPlus

AIProxyPlus is a self-hosted reverse proxy that sits in front of one or more LLM inference backends and gives a
team one stable, internally-governed endpoint to call instead of many provider-specific ones. It centralizes
authentication and secrets, tracks token usage per caller for chargeback, and adds resiliency — priority-tiered
routing with round-robin distribution and automatic failover — without getting in the way of streaming or
stateful conversations.

It is a clean-slate, vendor-neutral rewrite of [azureholic/ai-proxy](https://github.com/azureholic/ai-proxy).
The original proxied only Azure OpenAI, logged usage to Log Analytics, and relied on Azure Managed Identity. This
version speaks the OpenAI-compatible and Anthropic wire protocols, can route to a backend hosted anywhere — local,
OpenAI, Anthropic, Azure AI Foundry, self-hosted vLLM/Ollama — and stores its own configuration, usage, and
secrets rather than depending on any single cloud. Azure Managed Identity is still supported, but only as one
optional, per-endpoint authentication mode.

## What it does

- **Unifies ingress**: `/openai/v1/...` and `/anthropic/v1/...` passthrough for chat/completions, embeddings,
  images, the stateful Responses API, and the Realtime (voice) WebSocket API, plus request/response speech
  (transcription and text-to-speech).
- **Routes with resiliency**: each model can have several endpoints across priority tiers; requests round-robin
  within a tier and fail over to the next tier on `429`/`5xx`, without ever double-committing a response to the
  caller.
- **Preserves protocol-inherent affinity**: Responses API calls stay pinned to the endpoint that owns their
  `previous_response_id`; Realtime calls stay pinned to their WebSocket connection. There is no generic
  session-affinity configuration to get wrong.
- **Captures usage without buffering**: streaming responses are relayed to the caller incrementally while token
  usage is extracted server-side per provider/operation, even when the client itself never asked for usage data.
- **Keeps secrets out of the database**: outbound endpoint credentials, the admin API key, and the Consumer key
  pepper live in HashiCorp Vault; Consumer API keys themselves are one-way hashed in PostgreSQL and only ever
  shown once.
- **Rotates credentials safely**: an endpoint can hold a Primary and an optional Secondary API key. Activating a
  new key can retain the old one as a time-bounded, single-retry fallback so a bad rotation doesn't cause an
  outage.
- **Supports more than API keys**: administrators sign in through any OIDC provider (Keycloak, Microsoft Entra
  ID, Okta, Auth0, ...); machine callers can authenticate with Consumer API keys, a generic OIDC access token, or
  a Microsoft Entra managed identity.
- **Is observable by default**: OpenTelemetry traces, metrics, and logs for every request, wired through a
  shared `AIProxyPlus.ServiceDefaults` project.
- **Ships a small ops UI**: a React/FluentUI frontend for managing models, endpoints, routes, and consumers, plus
  a chat/image/voice/speech playground and a usage dashboard — served from the same container as the API.

The full design rationale lives in [specs/architecture/architecture.md](specs/architecture/architecture.md), and
the phase-by-phase build history for every iteration is under [specs/iterations/](specs/iterations/).

## Pull request governance

The `main` branch is protected by a GitHub repository ruleset. Changes must arrive through a pull request and pass
the CodeQL checks for C# and TypeScript plus dependency review. GitHub Copilot is requested automatically for every
pull request and reviews new pushes using the repository's Copilot instructions. The
repository-wide [`security-review.instructions.md`](.github/instructions/security-review.instructions.md) applies
to every changed file, so high-confidence security analysis is part of each automatic Copilot pull-request review.

Security scanning has four layers: CodeQL, dependency review and Dependabot, secret scanning, and the advisory
agentic review above. Secret scanning also runs locally: after `winget install Gitleaks.Gitleaks`, run
`git config core.hooksPath .githooks` once per clone so gitleaks blocks commits and pushes that contain secrets.
For security-sensitive changes, also run the Copilot CLI `/security-review` command. See
[docs/security-scanning.md](docs/security-scanning.md) for each layer's scope, setup, and limitations.

Human approval is optional while the project has one maintainer. After reading Copilot's review and confirming the
required checks passed, the maintainer can merge normally without approving their own pull request. The ruleset has
no bypass actors, so nobody, including the repository owner, can push directly to `main`, force-push it, or delete
it; always commit to a branch and merge through a pull request. Local git hooks refuse commits and pushes on `main`
as an early warning (see [docs/security-scanning.md](docs/security-scanning.md)).

Dependabot pull requests receive an additional compile-and-test gate. The workflow compiles the complete .NET
solution, runs Domain, Application, and architecture tests, then runs the frontend unit tests and production build.
A separate trusted workflow verifies the pull request author and source repository before approving the update and
enabling squash auto-merge. GitHub merges it only after the normal CodeQL and dependency-review requirements also
pass.

Application dependencies resolve exclusively through Microsoft's anonymously readable CFS proxies. NuGet uses the
repository-level `NuGet.config`; npm uses `src/AIProxyPlus.Web/.npmrc`. Docker builds, GitHub Actions, and Dependabot
use the same proxy configuration. There are no credentials or fallbacks to the public NuGet or npm registries, so
package resolution fails closed if either CFS proxy is unavailable.

## Architecture at a glance

- **Clean Architecture**: `AIProxyPlus.Domain` (zero dependencies) → `AIProxyPlus.Application` (ports + use cases) →
  `AIProxyPlus.Infrastructure` (EF Core/Npgsql, Vault, routing, usage capture) → `AIProxyPlus.Api` (composition root) →
  `AIProxyPlus.AppHost` (.NET Aspire orchestration).
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
- **Observability**: OpenTelemetry traces/metrics (custom `Meter("AIProxyPlus")` + `ActivitySource("AIProxyPlus")`,
  auto-instrumented ASP.NET Core/HttpClient/Npgsql) and Serilog (rolling file + OTLP), all wired through
  `AIProxyPlus.ServiceDefaults`.

## Running locally

### Option A — .NET Aspire (recommended for local dev)

```powershell
dotnet run --project src/AIProxyPlus.AppHost
```

This starts persistent Postgres, local file-backed Vault, and Keycloak containers, then the API, and prints an Aspire
dashboard URL (traces/metrics/logs for every service). Before the first run, set the local OIDC secrets:

```powershell
dotnet user-secrets set "Parameters:oidc-client-secret" "<random-development-secret>" --project src/AIProxyPlus.AppHost
dotnet user-secrets set "Parameters:keycloak-admin-password" "<local-keycloak-admin-password>" --project src/AIProxyPlus.AppHost
dotnet user-secrets set "Parameters:proxy-admin-password" "<local-proxy-admin-password>" --project src/AIProxyPlus.AppHost
```

Open the API or Vite URL and sign in to the `aiproxyplus-local` realm as the development-only user `proxy-admin`.
Keycloak imports the realm only into a fresh `keycloak-data` volume; later starts reuse it without overwriting
changed credentials. To reset only local identity data, stop the AppHost, remove its Keycloak container, run
`docker volume rm keycloak-data`, and restart the AppHost. Postgres and Vault are unaffected. Never reuse these
development credentials outside local testing. The seeded `proxy-admin` subject is pinned so resetting Keycloak
does not create a different OIDC identity or collide with its Consumer row in persistent Postgres.

On first run:

- The database schema is **not** auto-migrated — apply it once with:
  ```powershell
  dotnet ef database update --project src/AIProxyPlus.Infrastructure --startup-project src/AIProxyPlus.Infrastructure
  ```
  (point `ConnectionStrings__aiproxyplusdb` at whatever port Aspire assigned Postgres, visible in the dashboard).
- The static admin API key is **not** generated or logged by the application. It is a disabled-by-default
  break-glass path (see below); browser sessions are the normal way to administer the proxy.
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

Brings up Postgres, local file-backed Vault, and the API (built from `src/AIProxyPlus.Api/Dockerfile`) on `localhost:8080`.
Apply migrations the same way as above, pointing at `localhost:5432`. Set `OTEL_EXPORTER_OTLP_ENDPOINT` in
`docker-compose.yml` if you have an external OTLP collector; leave it blank to disable OTLP export.

### Routing configuration

Two values are configurable via `appsettings`/environment variables — local dev needs neither, both fall back to
their existing defaults when unset:

| Key | Default | Purpose |
|---|---|---|
| `Routing:SessionAffinityTtl` | `00:30:00` (30 minutes) | How long a Responses API `previous_response_id` stays pinned to its originating endpoint. |
| `Routing:UnhealthyCooldown` | `00:00:30` (30 seconds) | How long a same-tier endpoint that just failed is skipped on *subsequent* requests before becoming eligible again (a cross-request passive circuit breaker). |

### Endpoint credential rotation

API-key endpoints have a required Primary and an optional Secondary slot. Setting a slot writes a new immutable
Vault reference but does not activate it. HTTP requests may use the previous slot once, only after an explicit
upstream `401` and only inside an administrator-supplied fallback window. Realtime WebSockets use immediate
cutover instead, because portable handshake errors cannot be classified reliably. Endpoint responses expose slot
state but never plaintext or Vault references.

| Key | Default | Purpose |
|---|---|---|
| `Endpoints:MaxCredentialFallbackWindow` | `01:00:00` | Maximum requested fallback window; must be greater than zero and no more than 24 hours. |
| `Vault:Retirement:GracePeriod` | `01:00:00` | Delay before deleting a replaced or removed immutable Vault value; zero to 24 hours. |
| `Vault:Retirement:RetryDelay` | `00:00:05` | Initial cleanup retry delay. |
| `Vault:Retirement:MaxRetryDelay` | `00:05:00` | Maximum bounded cleanup retry delay. |

Rotation runbook:

1. Enable a standby key at the provider and set it in the inactive slot.
2. Activate that slot with a short fallback expiry, or omit the expiry for immediate cutover.
3. Treat any successful credential fallback metric as rotation drift requiring investigation.
4. Stop fallback, revoke the old provider key, then optionally remove the inactive Secondary slot.

The mutating replica queues old Vault values for grace-period deletion and retries failures. This queue is
process-local and best-effort; a restart can leave an unreachable old Vault version that requires operator cleanup,
but immutable references prevent it from becoming active again.

### Least-privilege Postgres role (production)

Local Aspire/docker-compose dev intentionally uses one Postgres role for both migrations and runtime queries —
the same trade-off already made for dev-mode Vault. A real deployment should instead create two roles and set
`ConnectionStrings:aiproxyplusdb-migrations` to the privileged one:

```sql
-- Privileged role: runs EF Core migrations (DDL) at startup/deploy time only.
CREATE ROLE aiproxyplus_migrator WITH LOGIN PASSWORD '...';
GRANT ALL PRIVILEGES ON DATABASE aiproxyplusdb TO aiproxyplus_migrator;

-- Narrow role: what the running API actually connects as.
CREATE ROLE aiproxyplus_app WITH LOGIN PASSWORD '...';
GRANT CONNECT ON DATABASE aiproxyplusdb TO aiproxyplus_app;
GRANT USAGE ON SCHEMA public TO aiproxyplus_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO aiproxyplus_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO aiproxyplus_app;
```

Point `ConnectionStrings:aiproxyplusdb-migrations` at `aiproxyplus_migrator` and `ConnectionStrings:aiproxyplusdb` at the
narrower `aiproxyplus_app` — the API applies migrations through the first connection at startup only, then serves
every runtime request through the second. Omitting `ConnectionStrings:aiproxyplusdb-migrations` keeps the
single-connection-string behavior unchanged.

## Frontend (`src/AIProxyPlus.Web`)

A React + TypeScript + FluentUI admin/ops UI, served by the **same** container as the API — there is no separate
frontend host or deployment.

- **Pages**: Config → Models, Endpoints, API Routes, Consumers; Playground → Chat
  (streaming/non-streaming), Images, Realtime Voice (push-to-talk microphone input, streaming speaker output, and
  raw Realtime event diagnostics), and Speech (file/microphone transcription plus text-to-speech playback and
  download); Dashboard → Usage (filters, table, token/request charts).
- **Production / container path**: `npm run build` (or just `docker build`/`docker compose up`, which run it for
  you) writes the compiled static files straight into `src/AIProxyPlus.Api/wwwroot`, which `AIProxyPlus.Api` then serves
  at `/` (with a client-side-routing fallback to `index.html`) alongside its existing `/api/*`, `/openai/*`,
  `/anthropic/*` routes. `wwwroot`'s contents are git-ignored — they're a build artifact, not source.
- **Local frontend development** (hot reload, no rebuild loop):
  ```powershell
  # Terminal 1 — the API (Aspire or plain `dotnet run`)
  dotnet run --project src/AIProxyPlus.Api
  # Terminal 2 — Vite dev server; proxies /api, /openai, /anthropic, /health to the API above
  cd src/AIProxyPlus.Web
  npm install
  npm run dev
  ```
  Open the printed Vite URL (not the API's own port) — `vite.config.ts`'s dev proxy forwards backend calls so
  the frontend never needs its own copy of the auth/CORS story.
- On first load, sign in through the configured OIDC provider. React receives only minimal session metadata;
  provider tokens and the confidential-client credential remain on the server. Config, Usage, Chat, and Images
  use the encrypted same-origin session. Voice exchanges that session for a 30-second, single-use Realtime ticket.
- Voice requires a browser with Web Audio, AudioWorklet, and microphone support. Connect to a configured OpenAI
  Realtime model, hold **Hold to talk** while speaking, and release to commit the turn. Microphone access
  requires HTTPS or a loopback development origin such as `localhost`.
- Every model has one or more administrator-managed capabilities: `Chat`, `ImageGeneration`, `Realtime`,
  `SpeechToText`, and `TextToSpeech`. Playground selectors show only models with the matching capability. These
  values are discovery metadata; provider and operation compatibility are still enforced by routing.
- Speech is separate from Realtime Voice. Speech-to-text sends a selected audio file or an ephemeral microphone
  recording to `/openai/v1/audio/transcriptions`; text-to-speech sends text and a voice to
  `/openai/v1/audio/speech`, then offers browser playback and download. Audio is not persisted by AIProxyPlus.
- Frontend tests: `cd src/AIProxyPlus.Web && npm test` (Vitest + React Testing Library).

## OIDC provider setup

AIProxyPlus uses generic OIDC discovery and a server-side Authorization Code + PKCE flow. Register one confidential
web client per deployment with exact callback and post-logout URIs. The provider must supply stable `iss` + `sub`,
the configured client ID in `aud`, and the configured username claim in the ID token or UserInfo response. Restrict
who may sign in to this client at the provider: every admitted browser user is an AIProxyPlus administrator.

Configure the provider only in `src/AIProxyPlus.AppHost/appsettings.json`. AppHost resolves
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
OIDC Consumers have no Consumer API key and cannot rotate one.

## Workload OIDC and managed identity

Machine callers can use a generic OIDC access token or a Microsoft Entra managed-identity token. Configure the
independent `Authentication:Workload:Oidc` or `Authentication:Workload:ManagedIdentity` section in
`src/AIProxyPlus.Api/appsettings.json` (prefer environment variables for deployment values). Set an exact HTTPS
authority and audience, then enable the scheme. OIDC tokens require `aiproxyplus.invoke` in `scope`; managed identities
require the `AIProxyPlus.Invoke` application role by default. Both values are configurable.

A valid token is not sufficient by itself. Pre-enroll the OIDC `iss` + `sub` pair or managed identity `tid` + `oid`
pair on the Consumers page or through the configuration API. Local disable/revoke, model allow-lists, and usage
attribution then work exactly as they do for API-key Consumers. Workload identities are never auto-provisioned.

The static Admin API Key is a disabled-by-default migration/break-glass path. Set
`Authentication__AdminApiKeyFallbackEnabled=true` only during a controlled migration, and supply the key yourself
through `Authentication__AdminApiKey` (use a secret store or secret-aware parameter, never a committed file). It must
be at least 32 characters and the proxy refuses to start with a shorter value; generate one with a CSPRNG, for
example `openssl rand -hex 32` or
`[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()` in PowerShell. The
application stores only its SHA-256 hash in Vault, never generates a key, and never logs one. Changing
`Authentication__AdminApiKey` replaces the stored hash, which is how you rotate it; if an earlier version of the
proxy logged a generated key, rotate it this way and treat the old value as compromised. With no key configured and
none stored, the break-glass key is unavailable. Browser sessions require
the antiforgery header on unsafe requests; API-key clients do not.

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

# Stage a Secondary key without activating it.
curl -X PUT http://localhost:8080/api/v1/config/endpoints/<endpoint-id>/secrets/Secondary `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"apiKeyPlaintext":"sk-new-..."}'

# Activate Secondary and retain Primary as fallback until the supplied UTC expiry.
curl -X PUT http://localhost:8080/api/v1/config/endpoints/<endpoint-id>/credential-activation `
  -H "X-Admin-Api-Key: <admin-key>" -H "Content-Type: application/json" `
  -d '{"activeSlot":"Secondary","fallbackExpiresAt":"2030-01-01T12:10:00Z"}'

# End fallback as soon as the new provider key is proven healthy.
curl -X DELETE http://localhost:8080/api/v1/config/endpoints/<endpoint-id>/credential-fallback `
  -H "X-Admin-Api-Key: <admin-key>"

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
- The running API writes rolling logs to `logs/aiproxyplus-.log` (also git-ignored beyond `.gitkeep`).
- `AIProxyPlus.Domain.Tests`/`AIProxyPlus.Application.Tests` are pure unit tests. `AIProxyPlus.Infrastructure.Tests` uses
  Testcontainers for Postgres/Vault (requires Docker running). `AIProxyPlus.Api.Tests` uses `WebApplicationFactory`
  + WireMock.Net fake backends (no Docker required) to exercise the full HTTP pipeline — priority/round-robin
  routing and failover, Responses API affinity, the Realtime WebSocket proxy, streaming usage capture, the
  config REST API, and authz enforcement.
- `AIProxyPlus.ArchitectureTests` (NetArchTest) enforces the Clean Architecture dependency direction.
