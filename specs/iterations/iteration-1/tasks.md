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
- A git commit is made after each phase (Phase 0 through Phase 10) once its tasks are checked off and
  `dotnet build`/`dotnet test` pass, so history maps 1:1 to phases and each phase is independently revertible.
  This commit includes the corresponding checkbox updates in this `tasks.md` file — code and task status land
  together, not as a separate follow-up commit.
- Before every phase commit, tests are run via the VS Code Testing tool (Test Explorer) rather than a raw
  `dotnet test --logger trx` dump to `test-results/`, and a pass/fail summary is reported in chat — that folder
  is for real CI/manual `dotnet test` runs, not something populated on every phase commit.

---

## Phase 0 — Solution scaffolding

- [x] Create solution `AiProxy.sln` and empty projects per [architecture §4](../../architecture/architecture.md#4-solution-structure-clean-architecture):
  `AiProxy.Domain`, `AiProxy.Application`, `AiProxy.Infrastructure`, `AiProxy.Api`, `AiProxy.ServiceDefaults`,
  `AiProxy.AppHost`.
- [x] Create test projects: `AiProxy.Domain.Tests`, `AiProxy.Application.Tests`, `AiProxy.Infrastructure.Tests`,
  `AiProxy.Api.Tests`, `AiProxy.ArchitectureTests`.
- [x] Wire project references so the dependency direction matches Clean Architecture (Domain has zero project
  references; Application → Domain; Infrastructure → Application, Domain; Api → Infrastructure, Application,
  Domain, ServiceDefaults; AppHost → Api).
- [x] Add `Directory.Build.props`/`Directory.Packages.props` for shared nullable/analyzers/central package
  versions.
- [x] Create `logs/` and `test-results/` folders with `.gitkeep`; add both to `.gitignore` (contents, not the
  folders themselves).
- [x] Add `AiProxy.ArchitectureTests` with NetArchTest rules asserting: `Domain` does not depend on
  `Infrastructure`/`Api`/ASP.NET Core/EF Core; `Application` does not depend on `Infrastructure`/`Api`.
- [x] Verify `dotnet build` succeeds and `dotnet test` runs (even with zero real tests) with TRX output redirected
  to `test-results/`.

## Phase 1 — Domain layer

- [x] Implement `ModelDeployment`, `Endpoint`, `UsageRecord`, `SessionAffinityEntry`, `Consumer` entities and
  `ApiFamily`, `AuthMode`, `RoutingStrategy`, `EndpointHealthState`, `OperationType` (ChatCompletion, Completion,
  Embedding, ImageGeneration, ImageEdit, ImageVariation, ResponsesApi, extensible) value objects/enums
  (architecture §5).
- [x] Implement domain invariants: a deployment must have ≥1 endpoint to be usable; priorities must be unique per
  deployment; an `ApiKey`-mode endpoint must have a non-empty `SecretRef`; a `ManagedIdentity`-mode endpoint must
  have a non-empty scope; a `Consumer` must have a unique `Name` and a non-empty `ApiKeyHash`; a `UsageRecord`
  must always carry a valid `ConsumerId`/`ConsumerName` and at least one usage signal (token fields or
  `UsageMetrics`).
- [x] Implement pure endpoint-selection logic (given a set of endpoints + health states + sticky affinity, return
  the endpoint to use) as a testable domain service — no I/O.
- [x] Unit tests (`AiProxy.Domain.Tests`) for all invariants and selection logic, including edge cases (all
  endpoints unhealthy, single endpoint, sticky endpoint unhealthy).

## Phase 2 — Application layer

- [x] Define ports: `IModelDeploymentRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `ISecretResolver`, `ICredentialProvider`, `IEndpointHealthTracker`, `IConsumerRepository`,
  `IConsumerAuthenticator` (hashes an incoming API key and looks up the matching enabled `Consumer`),
  `IUsageExtractor` (extracts token counts and/or `UsageMetrics` from a request/response for one
  `ApiFamily`+`OperationType` pair) and `IUsageExtractorResolver` (picks the right `IUsageExtractor` per request).
- [x] Implement configuration use cases: create/update/delete `ModelDeployment`, add/update/remove `Endpoint`,
  set-endpoint-secret command (delegates to `ISecretResolver`, never stores secret material itself); create
  `Consumer` (generates a random API key, persists only its hash, returns the plaintext key once), rotate
  `Consumer` key, revoke `Consumer`.
- [x] Implement usage query use case (filter by deployment name, consumer name, operation type, and date range,
  paged). Add FluentValidation validators for all commands (unique name, valid URL, valid priority, enum values).
- [x] Unit tests (`AiProxy.Application.Tests`) for every use case with mocked ports (NSubstitute), including
  validation failure paths, consumer create/rotate/revoke/authenticate behavior, and extractor-resolver dispatch
  (right extractor chosen per `ApiFamily`+`OperationType`, sensible failure when none registered).

## Phase 3 — Infrastructure: PostgreSQL

- [x] Add EF Core + Npgsql, `AiProxyDbContext`, entity configurations for all five tables
  (architecture §7.1, incl. `consumers`), initial migration. `usage_records.prompt_tokens`/`completion_tokens`/
  `total_tokens` are nullable; add `operation_type` and a `usage_metrics` jsonb column.
- [x] Implement `IModelDeploymentRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `IConsumerRepository` (incl. `GetByApiKeyHashAsync`) against `AiProxyDbContext`.
- [x] Implement a bounded-channel background writer for usage records so writes never block the response path.
- [x] Integration tests (`AiProxy.Infrastructure.Tests`) using Testcontainers' Postgres module: migrations apply
  cleanly, repository CRUD round-trips, uniqueness constraints enforced (`model_deployments.name`,
  `consumers.name`/`api_key_hash`, `endpoints.(model_deployment_id, priority)`), usage-record writer persists
  under load (channel → background service → DB, 50 concurrent records), consumer create/rotate/revoke
  round-trips.

## Phase 4 — Infrastructure: Secrets (Vault)

- [x] Add VaultSharp client, `VaultSecretResolver : ISecretResolver` writing/reading KV v2 secrets at
  `secret/ai-proxy/endpoints/{endpointId}`.
- [x] Implement `ApiKeyCredentialProvider` (resolves secret via `ISecretResolver`, caches with TTL, attaches
  `Authorization`/`api-key` header per `ApiFamily` convention) and `ManagedIdentityCredentialProvider` (uses
  `Azure.Identity` token credential, attaches bearer token) behind `ICredentialProvider`.
- [x] Implement `IApiKeyHasher` as HMAC-SHA256 with a pepper resolved once at startup via `ISecretResolver`
  from `secret/ai-proxy/consumer-key-pepper` (architecture.md §8) and a constant-time comparison; implement
  `IApiKeyGenerator` (cryptographically random plaintext key).
- [x] Ensure no secret value is ever written to logs (`VaultSecretResolver` only ever logs the secret *path*;
  a unit test with a capturing logger asserts the log output never contains an injected secret value —
  Serilog-specific sink/redaction wiring is layered on top in Phase 8).
- [x] Integration tests (`AiProxy.Infrastructure.Tests`) using Testcontainers' Vault (or a dev-mode Vault
  container) module: write/read/rotate a secret, cache TTL expiry behavior, resolver failure handling
  (missing path), and `ConsumerAuthenticator` end-to-end against a real hasher+pepper (accepts a valid key,
  rejects an unknown or revoked one).

## Phase 5 — Infrastructure: Routing

> **Design note (deviation from the original plan):** destination selection depends on the request body
> (`model`, session id, `previous_response_id`) and our own domain rules (priority/failover/sticky), which
> doesn't map onto YARP's declarative `IProxyConfigProvider`/cluster model. We also found that YARP's
> `IHttpForwarder` commits status/headers to the client as soon as it receives them from upstream, which
> makes bounded per-endpoint retry (inspect-before-commit) impossible when reusing it across attempts on the
> same `HttpContext` (empirically confirmed via a failing integration test: "the response has already
> started"). Routing is instead implemented as: buffer + peek the request body, run our own
> priority/failover/sticky selection (`EndpointSelector`), then forward via a small `DirectHttpProxy` helper
> that issues each attempt with a raw `HttpMessageInvoker`, inspects the status code, and only copies the
> response onto `HttpContext.Response` once an attempt is accepted (success or retries exhausted). This keeps
> full control over retries/affinity while still supporting streaming pass-through later (Phase 6).

- [x] Implement deployment resolution: peek `model` field from the request body (buffered) before forwarding,
  for both OpenAI-shaped and Anthropic-shaped payloads.
- [x] Implement priority-based load balancing + passive health checks (mark endpoint unhealthy on transport
  error/5xx, in-memory `IEndpointHealthTracker`).
- [x] Implement session affinity: extract session id (body field or `X-Session-Id` header), persist/read via
  `ISessionAffinityStore`; implement the "sticky endpoint unhealthy → fail fast" policy from architecture §9
  (no fallback to another endpoint for a pinned session).
- [x] Implement Responses API affinity chaining (architecture §6.4/§9): on request, if `previous_response_id`
  is present, look it up in `ISessionAffinityStore` and pin routing to that endpoint (falls back to normal
  routing if the id is unrecognized); on response completion, extract the response's own `id` and write a new
  affinity row (skip entirely when the request set `store: false`).
- [x] Implement bounded per-endpoint retry with jittered backoff before failover to next priority
  (`DirectHttpProxy` + manual retry loop in `ProxyRoutingService`).
- [x] Integration tests (`AiProxy.Api.Tests`) against WireMock.Net fake backends: priority selection, failover on
  5xx/connection-refused, all-endpoints-unhealthy → 503, unknown deployment/missing model → 404, disabled
  endpoint never selected, sticky session pins to the same fake backend across calls, sticky session with
  unhealthy endpoint returns the documented error (no silent reroute), different sessions may land on different
  endpoints, Responses API `previous_response_id` chaining pins to the originating backend across turns, unknown
  `previous_response_id` falls back to normal routing, `store: false` skips affinity persistence.

## Phase 6 — Streaming & usage capture

> **Note:** `ConsumerId`/`ConsumerName` attribution is a placeholder until Phase 7 wires real ingress
> authentication — `Program.cs` currently reads them from `X-Consumer-Id`/`X-Consumer-Name` headers (stripped
> before forwarding upstream) so this phase's capture pipeline could be built and tested end-to-end; Phase 7
> replaces that with `IConsumerAuthenticator` resolution over the caller's API key. Until a real consumer is
> resolved, or a response carries no usage signal at all, `UsageRecord` capture is skipped rather than
> violating its `ConsumerId`/"at least one usage signal" domain invariants.

- [x] Implement `IUsageExtractor` implementations per architecture §4/§10: `OpenAiStyleTokenUsageExtractor`
  (chat completions/completions/embeddings, token-based, from JSON `usage` or the streaming SSE usage frame,
  shared across the OpenAI/AzureOpenAI/OpenAICompatible families), `AnthropicChatUsageExtractor`
  (`input_tokens`/`output_tokens` from the body or `message_start`/`message_delta` SSE events),
  `ImageUsageExtractor` (non-streaming; reads image count/size/quality into `UsageMetrics`), and
  `ResponsesUsageExtractor` (reads `input_tokens`/`output_tokens`/`total_tokens` from the `response.completed`
  SSE event or the JSON body); wired up via `IUsageExtractorResolver`.
- [x] Implement outbound request mutation (`OutboundBodyMutator`): force `stream_options.include_usage = true`
  for OpenAI-family streaming chat completions/completions requests.
- [x] Implement a streaming response interceptor (`SseStreamRelay`) that parses SSE `data:` frames
  incrementally (no full-body buffering — reads/writes line-by-line, block-by-block), detects the usage-bearing
  frame via the resolved `IUsageExtractor`, and merges usage across multiple frames (e.g. Anthropic's
  `message_start` + `message_delta`).
- [x] Implement suppression of the injected usage-only chunk (`IUsageExtractor.IsUsageOnlyStreamingEvent`) when
  the original client request did not request usage — OpenAI-family only, since Anthropic's usage-bearing
  frames are otherwise-necessary content and are never suppressed.
- [x] Implement non-streaming usage extraction from the JSON response body (chat completions without
  `stream: true`, completions, embeddings, and all Images API operations) via the already-buffered response
  peek used for Responses API affinity capture.
- [x] Wire usage capture to `IUsageRecordRepository` via the existing `UsageRecordChannel` (async,
  non-blocking), attaching `ConsumerId`/`ConsumerName` (see note above) and the resolved `OperationType` to
  every `UsageRecord`.
- [x] Integration tests (`AiProxy.Api.Tests`): non-streaming chat completion captures token usage,
  non-streaming image generation captures `UsageMetrics` with null token fields, streaming with
  client-requested usage forwards the usage chunk and still captures usage, streaming without
  client-requested usage suppresses the usage chunk from the client stream but still captures usage
  server-side, streaming Anthropic messages capture input/output tokens merged across `message_start` +
  `message_delta`, a response with no usage signal at all succeeds without writing a `UsageRecord`.

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
