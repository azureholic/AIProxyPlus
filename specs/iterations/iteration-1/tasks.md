# Iteration 1 — Tasks

Reference architecture: [specs/architecture/architecture.md](../../architecture/architecture.md)

Goal: a working, tested .NET 10 AI proxy — OpenAI + Anthropic ingress, Postgres-backed configuration and usage
logging, Vault-backed secrets, priority/failover + sticky-session routing, forced streaming usage capture, full
OpenTelemetry instrumentation, running locally via a .NET Aspire AppHost, built with Clean Architecture and backed
by unit + integration tests. All debug logs and test output are written under `logs/` and `test-results/` in this
workspace.

Definition of Done for this iteration:
- All tasks below checked off.
- `dotnet test` passes for all test projects, results written to `test-results/`.
- Architecture tests pass (no illegal dependencies between layers).
- `AiProxy.AppHost` starts the API, Postgres, Vault, and the Aspire dashboard, and traces/metrics/logs for a
  sample request are visible in the dashboard.
- A README documents how to run locally (Aspire) and via `docker compose`.

---

## Phase 0 — Solution scaffolding

- [ ] Create solution `AiProxy.sln` and empty projects per [architecture §4](../../architecture/architecture.md#4-solution-structure-clean-architecture):
  `AiProxy.Domain`, `AiProxy.Application`, `AiProxy.Infrastructure`, `AiProxy.Api`, `AiProxy.ServiceDefaults`,
  `AiProxy.AppHost`.
- [ ] Create test projects: `AiProxy.Domain.Tests`, `AiProxy.Application.Tests`, `AiProxy.Infrastructure.Tests`,
  `AiProxy.Api.Tests`, `AiProxy.ArchitectureTests`.
- [ ] Wire project references so the dependency direction matches Clean Architecture (Domain has zero project
  references; Application → Domain; Infrastructure → Application, Domain; Api → Infrastructure, Application,
  Domain, ServiceDefaults; AppHost → Api).
- [ ] Add `Directory.Build.props`/`Directory.Packages.props` for shared nullable/analyzers/central package
  versions.
- [ ] Create `logs/` and `test-results/` folders with `.gitkeep`; add both to `.gitignore` (contents, not the
  folders themselves).
- [ ] Add `AiProxy.ArchitectureTests` with NetArchTest rules asserting: `Domain` does not depend on
  `Infrastructure`/`Api`/ASP.NET Core/EF Core; `Application` does not depend on `Infrastructure`/`Api`.
- [ ] Verify `dotnet build` succeeds and `dotnet test` runs (even with zero real tests) with TRX output redirected
  to `test-results/`.

## Phase 1 — Domain layer

- [ ] Implement `ModelDeployment`, `Endpoint`, `UsageRecord`, `SessionAffinityEntry`, `Consumer` entities and
  `ApiFamily`, `AuthMode`, `RoutingStrategy`, `EndpointHealthState`, `OperationType` (ChatCompletion, Completion,
  Embedding, ImageGeneration, ImageEdit, ImageVariation, ResponsesApi, extensible) value objects/enums
  (architecture §5).
- [ ] Implement domain invariants: a deployment must have ≥1 endpoint to be usable; priorities must be unique per
  deployment; an `ApiKey`-mode endpoint must have a non-empty `SecretRef`; a `ManagedIdentity`-mode endpoint must
  have a non-empty scope; a `Consumer` must have a unique `Name` and a non-empty `ApiKeyHash`; a `UsageRecord`
  must always carry a valid `ConsumerId`/`ConsumerName` and at least one usage signal (token fields or
  `UsageMetrics`).
- [ ] Implement pure endpoint-selection logic (given a set of endpoints + health states + sticky affinity, return
  the endpoint to use) as a testable domain service — no I/O.
- [ ] Unit tests (`AiProxy.Domain.Tests`) for all invariants and selection logic, including edge cases (all
  endpoints unhealthy, single endpoint, sticky endpoint unhealthy).

## Phase 2 — Application layer

- [ ] Define ports: `IModelDeploymentRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `ISecretResolver`, `ICredentialProvider`, `IEndpointHealthTracker`, `IConsumerRepository`,
  `IConsumerAuthenticator` (hashes an incoming API key and looks up the matching enabled `Consumer`),
  `IUsageExtractor` (extracts token counts and/or `UsageMetrics` from a request/response for one
  `ApiFamily`+`OperationType` pair) and `IUsageExtractorResolver` (picks the right `IUsageExtractor` per request).
- [ ] Implement configuration use cases: create/update/delete `ModelDeployment`, add/update/remove `Endpoint`,
  set-endpoint-secret command (delegates to `ISecretResolver`, never stores secret material itself); create
  `Consumer` (generates a random API key, persists only its hash, returns the plaintext key once), rotate
  `Consumer` key, revoke `Consumer`.
- [ ] Implement usage query use case (filter by deployment name, consumer name, operation type, and date range,
  paged). Add FluentValidation validators for all commands (unique name, valid URL, valid priority, enum values).
- [ ] Unit tests (`AiProxy.Application.Tests`) for every use case with mocked ports (NSubstitute), including
  validation failure paths, consumer create/rotate/revoke/authenticate behavior, and extractor-resolver dispatch
  (right extractor chosen per `ApiFamily`+`OperationType`, sensible failure when none registered).

## Phase 3 — Infrastructure: PostgreSQL

- [ ] Add EF Core + Npgsql, `AiProxyDbContext`, entity configurations for all five tables
  (architecture §7.1, incl. `consumers`), initial migration. `usage_records.prompt_tokens`/`completion_tokens`/
  `total_tokens` are nullable; add `operation_type` and a `usage_metrics` jsonb column.
- [ ] Implement `IModelDeploymentRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `IConsumerRepository` against `AiProxyDbContext`.
- [ ] Implement `IConsumerAuthenticator` using a salted hash (e.g. HMAC-SHA256 with a server-side pepper) and a
  constant-time comparison against `consumers.api_key_hash`.
- [ ] Implement a bounded-channel background writer for usage records so writes never block the response path.
- [ ] Integration tests (`AiProxy.Infrastructure.Tests`) using Testcontainers' Postgres module: migrations apply
  cleanly, repository CRUD round-trips, uniqueness constraints enforced, usage-record writer persists under load,
  consumer create/rotate/revoke round-trips and authenticator accepts/rejects correctly.

## Phase 4 — Infrastructure: Secrets (Vault)

- [ ] Add VaultSharp client, `VaultSecretResolver : ISecretResolver` writing/reading KV v2 secrets at
  `secret/ai-proxy/endpoints/{endpointId}`.
- [ ] Implement `ApiKeyCredentialProvider` (resolves secret via `ISecretResolver`, caches with TTL, attaches
  `Authorization`/`api-key` header per `ApiFamily` convention) and `ManagedIdentityCredentialProvider` (uses
  `Azure.Identity` token credential, attaches bearer token) behind `ICredentialProvider`.
- [ ] Ensure no secret value is ever written to logs (Serilog destructuring/redaction policy + a unit test
  asserting log output doesn't contain injected secret markers).
- [ ] Integration tests (`AiProxy.Infrastructure.Tests`) using Testcontainers' Vault (or a dev-mode Vault
  container) module: write/read/rotate a secret, cache TTL expiry behavior, resolver failure handling
  (missing path).

## Phase 5 — Infrastructure: Routing (YARP)

- [ ] Implement a database-backed `IProxyConfigProvider` that builds YARP clusters/destinations from
  `ModelDeployment`/`Endpoint` data, refreshed on config change (in-process change token).
- [ ] Implement deployment resolution: peek `model` field from the request body (buffered) before YARP
  forwarding, for both OpenAI-shaped and Anthropic-shaped payloads.
- [ ] Implement priority-based load balancing + passive/active health checks (mark endpoint unhealthy on
  transport error/5xx/timeout for a cooldown window, then retry).
- [ ] Implement session affinity: extract session id (body field or `X-Session-Id` header), persist/read via
  `ISessionAffinityStore`, apply YARP session affinity when `StickySessionEnabled`; define and implement the
  "sticky endpoint unhealthy → fail fast" policy from architecture §9.
- [ ] Implement Responses API affinity chaining (architecture §6.4/§9): on request, if `previous_response_id`
  is present, look it up in `ISessionAffinityStore` and pin routing to that endpoint; on response completion,
  extract the response's own `id` and write a new affinity row (skip entirely when the request set
  `store: false`).
- [ ] Implement bounded per-endpoint retry with jittered backoff (Microsoft.Extensions.Http.Resilience) before
  failover to next priority.
- [ ] Integration tests (`AiProxy.Api.Tests`) against WireMock.Net fake backends: priority selection, failover on
  5xx/timeout, sticky session pins to the same fake backend across calls, sticky session with unhealthy endpoint
  returns the documented error, Responses API `previous_response_id` chaining pins to the originating backend
  across turns.

## Phase 6 — Streaming & usage capture

- [ ] Implement `IUsageExtractor` implementations per architecture §4/§10: `ChatCompletionUsageExtractor` /
  `CompletionUsageExtractor` / `EmbeddingUsageExtractor` (token-based, from JSON `usage` or the streaming SSE
  usage frame), `ImageUsageExtractor` (non-streaming; reads whatever billing signal the Images API response
  contains — e.g. image count/size/quality — into `UsageMetrics`), and `ResponsesUsageExtractor` (reads
  `input_tokens`/`output_tokens`/`total_tokens` from the `response.completed` SSE event or the JSON body); wire
  them up via `IUsageExtractorResolver`.
- [ ] Implement outbound request mutation: force `stream_options.include_usage = true` for OpenAI-family
  streaming (chat completions) requests.
- [ ] Implement a streaming response interceptor that parses SSE `data:` frames incrementally (no full-body
  buffering), detects the usage-bearing frame (OpenAI final chunk / Anthropic `message_start` +
  `message_delta`), and records usage via the resolved `IUsageExtractor`.
- [ ] Implement suppression of the injected usage-only chunk when the original client request did not request
  usage, for both API families.
- [ ] Implement non-streaming usage extraction from the JSON response body (chat completions without
  `stream: true`, completions, embeddings, and all Images API operations).
- [ ] Wire usage capture to `IUsageRecordRepository` (async, non-blocking), attaching the `ConsumerId`/
  `ConsumerName` resolved during ingress authentication (Phase 7) and the resolved `OperationType` to every
  `UsageRecord`.
- [ ] Integration tests (`AiProxy.Api.Tests`): streaming with client-requested usage (chunk forwarded),
  streaming without client-requested usage (chunk suppressed, stream otherwise identical), non-streaming usage
  capture for chat completions/embeddings (token fields populated) and images (`UsageMetrics` populated, token
  fields null), verify `usage_records` row written in all cases with the correct `ConsumerId` and
  `OperationType`, verify time-to-first-byte isn't materially impacted (no full-stream buffering).

## Phase 7 — API surface

- [ ] Map OpenAI ingress routes (`/openai/v1/chat/completions`, `/openai/v1/completions`,
  `/openai/v1/embeddings`, `/openai/v1/images/generations`, `/openai/v1/images/edits`,
  `/openai/v1/images/variations`, `/openai/v1/responses`) and Anthropic ingress route (`/anthropic/v1/messages`)
  per architecture §12.
- [ ] Map configuration REST API (`/api/v1/config/*`, incl. `Consumer` CRUD/rotate) per architecture §7.2, backed
  by Phase 2 use cases.
- [ ] Map usage query endpoint (`GET /api/v1/usage`, filterable by deployment and consumer).
- [ ] Implement ingress authentication middleware that resolves the caller's API key to a `Consumer` via
  `IConsumerAuthenticator` (401 on missing/unknown/revoked key) and makes `ConsumerId`/`ConsumerName` available
  to downstream routing/usage-capture code; implement admin API key auth for config routes (constant-time
  comparison, key hash stored in Vault).
- [ ] Add `/health/live`, `/health/ready` (Postgres + Vault reachability) and OpenAPI/Scalar UI in dev.
- [ ] Integration tests (`AiProxy.Api.Tests`) for full config CRUD lifecycle incl. consumers, authz enforcement
  (missing/invalid/revoked consumer keys rejected with 401, missing/invalid admin key rejected on config routes),
  OpenAPI document generation.

## Phase 8 — Observability

- [ ] Configure `AiProxy.ServiceDefaults` with OpenTelemetry traces/metrics/logs + OTLP exporter, ASP.NET
  Core/HttpClient/Npgsql auto-instrumentation, per architecture §11.
- [ ] Add custom `Meter("AiProxy")` instruments: request count, prompt/completion/total token counters, request
  duration histogram, failover counter.
- [ ] Add custom spans for deployment resolution, secret resolution, backend call, usage extraction.
- [ ] Configure Serilog with the OTLP + rolling-file (`logs/ai-proxy-.log`) sinks, trace/span-id enrichment, and
  secret redaction.
- [ ] Manual verification: run via Aspire, issue a sample request, confirm traces/metrics/logs appear in the
  Aspire dashboard with the expected custom span/metric names.

## Phase 9 — Aspire AppHost & containers

- [ ] Implement `AiProxy.AppHost` wiring: Postgres resource (with volume), Vault resource, the Api project
  resource, service discovery + OTLP endpoint injection.
- [ ] Add Dockerfile for `AiProxy.Api`.
- [ ] Add `docker-compose.yml` (Postgres + Vault + Api) for non-Aspire container runs, with an external OTLP
  collector endpoint configurable via env var.
- [ ] Verify `dotnet run --project src/AiProxy.AppHost` brings up the full stack and the API responds to a health
  check and a sample proxied request against a local fake/stub backend.

## Phase 10 — Test hardening & documentation

- [ ] Add WireMock.Net-based fake OpenAI and Anthropic backend fixtures (success, 5xx, timeout, streaming
  with/without usage, non-streaming Images API generations/edits/variations responses, and Responses API
  streaming/non-streaming responses incl. a `previous_response_id` chain across two fake backends) reused across
  `AiProxy.Api.Tests`.
- [ ] Ensure `dotnet test` across the whole solution writes TRX + coverage (Cobertura) into `test-results/`
  (one subfolder per run), and the running API/tests write logs into `logs/`.
- [ ] Review coverage on `Domain`/`Application`; add tests for any uncovered branches in routing/failover/usage
  logic specifically (highest-risk areas).
- [ ] Write top-level `README.md`: architecture summary, how to run locally (Aspire), how to run via
  `docker compose`, how to run tests and where output lands (`logs/`, `test-results/`), and example `curl`
  requests for OpenAI and Anthropic ingress routes.
