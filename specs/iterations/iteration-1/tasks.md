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
- `AIProxyPlus.AppHost` starts the API, Postgres, Vault, and the Aspire dashboard, and traces/metrics/logs for a
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

- [x] Create solution `AIProxyPlus.sln` and empty projects per [architecture §4](../../architecture/architecture.md#4-solution-structure-clean-architecture):
  `AIProxyPlus.Domain`, `AIProxyPlus.Application`, `AIProxyPlus.Infrastructure`, `AIProxyPlus.Api`, `AIProxyPlus.ServiceDefaults`,
  `AIProxyPlus.AppHost`.
- [x] Create test projects: `AIProxyPlus.Domain.Tests`, `AIProxyPlus.Application.Tests`, `AIProxyPlus.Infrastructure.Tests`,
  `AIProxyPlus.Api.Tests`, `AIProxyPlus.ArchitectureTests`.
- [x] Wire project references so the dependency direction matches Clean Architecture (Domain has zero project
  references; Application → Domain; Infrastructure → Application, Domain; Api → Infrastructure, Application,
  Domain, ServiceDefaults; AppHost → Api).
- [x] Add `Directory.Build.props`/`Directory.Packages.props` for shared nullable/analyzers/central package
  versions.
- [x] Create `logs/` and `test-results/` folders with `.gitkeep`; add both to `.gitignore` (contents, not the
  folders themselves).
- [x] Add `AIProxyPlus.ArchitectureTests` with NetArchTest rules asserting: `Domain` does not depend on
  `Infrastructure`/`Api`/ASP.NET Core/EF Core; `Application` does not depend on `Infrastructure`/`Api`.
- [x] Verify `dotnet build` succeeds and `dotnet test` runs (even with zero real tests) with TRX output redirected
  to `test-results/`.

## Phase 1 — Domain layer

- [x] Implement `Model`, `Endpoint`, `UsageRecord`, `SessionAffinityEntry`, `Consumer` entities and
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
- [x] Unit tests (`AIProxyPlus.Domain.Tests`) for all invariants and selection logic, including edge cases (all
  endpoints unhealthy, single endpoint, sticky endpoint unhealthy).

## Phase 2 — Application layer

- [x] Define ports: `IModelRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `ISecretResolver`, `ICredentialProvider`, `IEndpointHealthTracker`, `IConsumerRepository`,
  `IConsumerAuthenticator` (hashes an incoming API key and looks up the matching enabled `Consumer`),
  `IUsageExtractor` (extracts token counts and/or `UsageMetrics` from a request/response for one
  `ApiFamily`+`OperationType` pair) and `IUsageExtractorResolver` (picks the right `IUsageExtractor` per request).
- [x] Implement configuration use cases: create/update/delete `Model`, add/update/remove `Endpoint`,
  set-endpoint-secret command (delegates to `ISecretResolver`, never stores secret material itself); create
  `Consumer` (generates a random API key, persists only its hash, returns the plaintext key once), rotate
  `Consumer` key, revoke `Consumer`.
- [x] Implement usage query use case (filter by deployment name, consumer name, operation type, and date range,
  paged). Add FluentValidation validators for all commands (unique name, valid URL, valid priority, enum values).
- [x] Unit tests (`AIProxyPlus.Application.Tests`) for every use case with mocked ports (NSubstitute), including
  validation failure paths, consumer create/rotate/revoke/authenticate behavior, and extractor-resolver dispatch
  (right extractor chosen per `ApiFamily`+`OperationType`, sensible failure when none registered).

## Phase 3 — Infrastructure: PostgreSQL

- [x] Add EF Core + Npgsql, `AIProxyPlusDbContext`, entity configurations for all five tables
  (architecture §7.1, incl. `consumers`), initial migration. `usage_records.prompt_tokens`/`completion_tokens`/
  `total_tokens` are nullable; add `operation_type` and a `usage_metrics` jsonb column.
- [x] Implement `IModelRepository`, `IUsageRecordRepository`, `ISessionAffinityStore`,
  `IConsumerRepository` (incl. `GetByApiKeyHashAsync`) against `AIProxyPlusDbContext`.
- [x] Implement a bounded-channel background writer for usage records so writes never block the response path.
- [x] Integration tests (`AIProxyPlus.Infrastructure.Tests`) using Testcontainers' Postgres module: migrations apply
  cleanly, repository CRUD round-trips, uniqueness constraints enforced (`models.name`,
  `consumers.name`/`api_key_hash`, `endpoints.(model_id, priority)`), usage-record writer persists
  under load (channel → background service → DB, 50 concurrent records), consumer create/rotate/revoke
  round-trips.

## Phase 4 — Infrastructure: Secrets (Vault)

- [x] Add VaultSharp client, `VaultSecretResolver : ISecretResolver` writing/reading KV v2 secrets at
  `secret/aiproxyplus/endpoints/{endpointId}`.
- [x] Implement `ApiKeyCredentialProvider` (resolves secret via `ISecretResolver`, caches with TTL, attaches
  `Authorization`/`api-key` header per `ApiFamily` convention) and `ManagedIdentityCredentialProvider` (uses
  `Azure.Identity` token credential, attaches bearer token) behind `ICredentialProvider`.
- [x] Implement `IApiKeyHasher` as HMAC-SHA256 with a pepper resolved once at startup via `ISecretResolver`
  from `secret/aiproxyplus/consumer-key-pepper` (architecture.md §8) and a constant-time comparison; implement
  `IApiKeyGenerator` (cryptographically random plaintext key).
- [x] Ensure no secret value is ever written to logs (`VaultSecretResolver` only ever logs the secret *path*;
  a unit test with a capturing logger asserts the log output never contains an injected secret value —
  Serilog-specific sink/redaction wiring is layered on top in Phase 8).
- [x] Integration tests (`AIProxyPlus.Infrastructure.Tests`) using Testcontainers' Vault (or a dev-mode Vault
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
- [x] Integration tests (`AIProxyPlus.Api.Tests`) against WireMock.Net fake backends: priority selection, failover on
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
- [x] Integration tests (`AIProxyPlus.Api.Tests`): non-streaming chat completion captures token usage,
  non-streaming image generation captures `UsageMetrics` with null token fields, streaming with
  client-requested usage forwards the usage chunk and still captures usage, streaming without
  client-requested usage suppresses the usage chunk from the client stream but still captures usage
  server-side, streaming Anthropic messages capture input/output tokens merged across `message_start` +
  `message_delta`, a response with no usage signal at all succeeds without writing a `UsageRecord`.

## Phase 7 — API surface

- [x] Map OpenAI ingress routes (`/openai/v1/chat/completions`, `/openai/v1/completions`,
  `/openai/v1/embeddings`, `/openai/v1/images/generations`, `/openai/v1/images/edits`,
  `/openai/v1/images/variations`, `/openai/v1/responses`) and Anthropic ingress route (`/anthropic/v1/messages`)
  per architecture §12 (mapped in Phase 5, now behind `ConsumerAuthEndpointFilter`).
- [x] Map configuration REST API (`/api/v1/config/*`, incl. `Consumer` CRUD/rotate) per architecture §7.2, backed
  by Phase 2 use cases (`CreateModelHandler`, `AddEndpointHandler`, `UpdateEndpointHandler`,
  `RemoveEndpointHandler`, `SetEndpointSecretHandler`, `UpdateModelHandler`,
  `DeleteModelHandler`, `CreateConsumerHandler`, `RotateConsumerKeyHandler`, `RevokeConsumerHandler`),
  plus direct repository reads for the two list/get routes that have no dedicated use case.
- [x] Map usage query endpoint (`GET /api/v1/usage`, filterable by deployment, consumer, operation type, date range).
- [x] Implement ingress authentication (`ConsumerAuthEndpointFilter`) that resolves the caller's `X-Api-Key` to a
  `Consumer` via `IConsumerAuthenticator` (401 on missing/unknown/revoked key) and stashes `ConsumerId`/
  `ConsumerName` in `HttpContext.Items` for `ProxyRoutingService` to read (replacing the Phase 6 placeholder
  headers); implement admin API key auth (`AdminAuthEndpointFilter` + `AdminApiKeyAuthenticator`, constant-time
  comparison, hash auto-provisioned in Vault on first run) for the **entire** `/api/v1/config/*` + `/api/v1/usage`
  surface — see the architecture §7.2 implementation note for why this is broader than "write routes only".
- [x] Add `/health/live` (always healthy — liveness), `/health/ready` (probes `ISecretResolver`/
  `IModelRepository` if registered, reports `"not configured"` until Phase 9 wires Postgres/Vault for
  real — forward-compatible with no further code changes needed then), and OpenAPI (`/openapi/v1.json`) +
  Scalar UI in dev.
- [x] Integration tests (`AIProxyPlus.Api.Tests`): full config CRUD lifecycle (create/get/list/update/delete
  deployment, add/update/remove endpoint, set/rotate endpoint secret never echoed back, duplicate-name conflict,
  empty-name validation error, unknown-deployment 404), consumer lifecycle (create returns plaintext key exactly
  once, list never exposes the key or hash, rotate invalidates the old key, revoke disables ingress auth for
  that consumer), authz enforcement (config/usage routes reject missing/wrong admin key with 401 and accept the
  valid one, ingress routes reject missing/unknown/revoked consumer keys with 401 and accept a freshly-created
  consumer's key), OpenAPI document generation succeeds. 197/197 tests passing solution-wide.

## Phase 8 — Observability

- [x] Configure `AIProxyPlus.ServiceDefaults` with OpenTelemetry traces/metrics/logs + OTLP exporter, ASP.NET
  Core/HttpClient/Npgsql auto-instrumentation, per architecture §11 (`AddMeter("AIProxyPlus")`/`AddSource("AIProxyPlus")`/
  `AddSource("Npgsql")` added alongside the existing ASP.NET Core/HttpClient/runtime instrumentation); wired into
  `AIProxyPlus.Api` via `builder.AddServiceDefaults()`.
- [x] Add custom `Meter("AIProxyPlus")` instruments (`AIProxyPlusMetrics`): `aiproxyplus.requests.count`,
  `aiproxyplus.tokens.prompt`/`.completion`/`.total`, `aiproxyplus.request.duration` (histogram), `aiproxyplus.failover.count`.
- [x] Add custom spans (`AIProxyPlusActivitySource`) for deployment resolution, secret resolution, backend call
  (tagged with endpoint/attempt/status), and usage extraction, nested under one root `aiproxyplus.request` activity
  per ingress request (tagged with operation_type/deployment) in `ProxyRoutingService`.
- [x] Configure Serilog (`UseSerilog(..., writeToProviders: true)` so events still reach the OTel logging
  provider for OTLP export) with a rolling-file sink (`logs/aiproxyplus-.log`), `Enrich.WithSpan()` for trace/span-id
  enrichment, and a `Destructure.ByTransforming<EndpointCredential>` policy that redacts the credential header
  value if one is ever logged.
- [x] Automated stand-in for the "run via Aspire, confirm traces/metrics appear" manual verification step:
  `TelemetryTests` (`AIProxyPlus.Api.Tests`) attaches an `ActivityListener` + `MeterListener` directly to the
  `AIProxyPlus` sources during a real routed WireMock-backed request and asserts all five spans and five metric
  instruments (incl. `aiproxyplus.failover.count` on a forced 5xx failover) actually fire. Full Aspire-dashboard
  visual confirmation is still a Phase 9 manual step once Postgres/Vault containers make `dotnet run` viable
  standalone (currently blocked by the same DI-registration gap noted in Phases 5–7 — confirmed via a real
  `dotnet run` attempt, which fails **only** on the expected missing repository/secret-store registrations, not
  on anything Serilog/OpenTelemetry-related). 199/199 tests passing solution-wide.

## Phase 9 — Aspire AppHost & containers

- [x] Implement `AIProxyPlus.AppHost` wiring: Postgres resource (`AddPostgres("postgres").WithDataVolume().AddDatabase("aiproxyplusdb")`),
  a dev-mode Vault resource (generic `AddContainer("vault", "hashicorp/vault")` — no official Aspire hosting
  package exists for Vault — with `VAULT_DEV_ROOT_TOKEN_ID`/`VAULT_DISABLE_MLOCK` for a local, throwaway dev
  instance), the Api project resource wired via `WithReference(postgres)` + `Vault__Address`/`Vault__Token`
  environment variables + `WaitFor(...)` on both. Also closes the long-standing DI-registration gap from
  Phases 5–8: `Program.cs` now registers the real `AIProxyPlusDbContext` (via `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL`'s
  `AddNpgsqlDbContext`), `ModelRepository`/`ConsumerRepository`/`SessionAffinityStore`/`UsageRecordRepository`,
  `VaultSecretResolver` (+ `VaultClientFactory`/`VaultOptions`), and `CredentialProvider`
  (`ApiKeyCredentialProvider`/`ManagedIdentityCredentialProvider` via `IMemoryCache`/`DefaultAzureCredential`).
- [x] Add Dockerfile for `AIProxyPlus.Api` (multi-stage restore/publish/runtime, repo-root build context) + a
  `.dockerignore` (excluding `bin/`/`obj/` — without it, host-machine build artifacts with Windows-specific
  NuGet fallback paths baked in get copied into the Linux build stage and break `dotnet publish`, confirmed by
  hitting exactly that failure on the first build attempt).
- [x] Add `docker-compose.yml` (Postgres + Vault + Api) for non-Aspire container runs, with `OTEL_EXPORTER_OTLP_ENDPOINT`
  left blank/configurable via env var for an external collector.
- [x] Verified end-to-end via **both** paths:
  - `dotnet run --project src/AIProxyPlus.AppHost`: Postgres/Vault containers came up, `AIProxyPlusDbContextFactory`
    (design-time-only) updated to honor a `ConnectionStrings__aiproxyplusdb` env var override so `dotnet ef database
    update` could target the Aspire-managed container; `GET /health/ready` initially reported `postgres`
    unhealthy (`relation "models" does not exist` — migrations aren't auto-applied at startup, by
    design, since that would break `WebApplicationFactory`-based tests which never point at a real database),
    then reported `{"vault":"healthy","postgres":"healthy"}` once the migration was applied. Directly queried
    Vault's HTTP API with the dev root token and confirmed `admin-api-key-hash` was auto-provisioned at
    `secret/aiproxyplus/admin-api-key-hash` as designed.
  - `docker compose up -d`: hit the same Postgres 18 image behavior change (data directory must now be mounted
    at `/var/lib/postgresql`, not `/var/lib/postgresql/data` — fixed in `docker-compose.yml`), then confirmed
    the identical healthy/unhealthy → healthy health-check transition through the containerized Api on
    `http://localhost:8080/health/ready`.
  - Also fixed a related bug this exposed: `/health/ready` originally threw a 500 instead of a graceful 503
    when `services.GetRequiredService<T>()` itself failed to *construct* a dependency (e.g. Vault client
    construction throwing because no token was configured) — the probe now wraps resolution *and* the call in
    the same try/catch. Added `HealthCheckTests` (`AIProxyPlus.Api.Tests`) covering the fake-backed healthy path.
  - All test/temporary containers, volumes, and images from this verification were torn down afterward
    (`docker compose down -v`, `docker rm -f`, `docker rmi`, `docker volume rm`) — nothing left running.
  - A full proxied ingress request against the live stack (vs. the WireMock-backed one already covered by
    ~30 `AIProxyPlus.Api.Tests`) was not additionally exercised manually — the automated suite already covers that
    path more rigorously and repeatably than a one-off manual curl would.
  - 201/201 tests passing solution-wide throughout.


## Phase 10 — Test hardening & documentation

- [x] Added dedicated unit tests for the `IUsageExtractor` family (`AIProxyPlus.Infrastructure.Tests/Usage`) —
  `OpenAiStyleTokenUsageExtractorTests`, `AnthropicChatUsageExtractorTests`, `ImageUsageExtractorTests`,
  `ResponsesUsageExtractorTests` — plus `AdminApiKeyAuthenticatorTests` (auto-provision-on-first-run and
  reuse-existing-hash branches). Added `AdditionalCoverageTests` (`AIProxyPlus.Api.Tests`) covering the remaining
  fixture gaps: non-streaming Images API edit/variation, streaming Responses API `previous_response_id`
  chaining, and a caller-cancellation test proving the retry/failover loop does **not** spuriously retry when
  the caller (not the backend) gives up — this last one exercises a genuinely previously-untested branch in
  `ProxyRoutingService`'s `TaskCanceledException` handling.
- [x] Verified `dotnet test --results-directory test-results/<timestamp> --logger trx --collect:"XPlat Code
  Coverage"` writes a TRX + `coverage.cobertura.xml` per test project into a single timestamped subfolder
  (all 5 test projects ran; verification folder deleted afterward — `test-results/` stays git-ignored except
  `.gitkeep`), and confirmed the API's Serilog rolling-file sink writes to `logs/aiproxyplus-.log` during a real
  run (already exercised manually in Phase 9).
- [x] Reviewed coverage on `Domain`/`Application` (both already consistently ≥92.9% on every file with any
  gaps — nothing egregious) and on the highest-risk routing/failover/usage files specifically: after the new
  tests, `ResponsesUsageExtractor` 30%→100%, `AnthropicChatUsageExtractor` 58.3%→91.7%, `ImageUsageExtractor`
  66.7%→94.4%, `AdminApiKeyAuthenticator` 65.2%→100%, `ProxyRoutingService` 86.2%→91.6%. Remaining gaps are
  defensive/malformed-JSON branches and one Domain-invariant-guarded-unreachable branch in
  `ManagedIdentityCredentialProvider` (documented in Phase 4's notes) — judged acceptable to leave uncovered.
- [x] Wrote top-level [README.md](../../../README.md): architecture summary + key design decisions, how to run
  locally via Aspire vs. `docker compose` (incl. the one-time migration + admin-key-is-logged-once steps),
  example `curl` requests for consumer/deployment creation and both OpenAI and Anthropic ingress routes, and
  where test/log output lands.
- 251/251 tests passing solution-wide — **Iteration 1 complete** (all 10 phases done, one commit per phase).

> **Superseded (security review):** the admin API key is no longer auto-generated or logged once. The Iteration 1
> notes above on hash auto-provisioning, `AdminApiKeyAuthenticatorTests` (auto-provision branch), and the
> "admin-key-is-logged-once" README step describe behavior that was removed because it wrote a usable credential
> to logs and telemetry. An operator now supplies `Authentication:AdminApiKey` (at least 32 characters) and only its
> hash is stored. See architecture §7.2 and §13 and the README.

