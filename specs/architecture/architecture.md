# AI Proxy — Architecture (Iteration 1)

## 1. Purpose

This project is a clean-slate rewrite of [azureholic/ai-proxy](https://github.com/azureholic/ai-proxy), keeping the same
core principle — **a self-hosted reverse proxy that sits in front of one or more LLM inference endpoints, centralizes
authentication/secrets, tracks token usage for chargeback, and adds resiliency (retry/failover)** — but generalized so
it is not tied to Azure:

- The original proxied only Azure OpenAI, logged usage to Log Analytics, and relied on Azure Managed Identity.
- This version proxies **OpenAI-compatible** and **Anthropic** APIs, can call model endpoints hosted **anywhere**
  (local, OpenAI, Anthropic, Azure AI Foundry, self-hosted vLLM/Ollama, ...), stores configuration and usage in
  **PostgreSQL**, stores secrets in a **self-hosted secret store**, and emits **OpenTelemetry** traces/metrics/logs.
- Azure Managed Identity remains as *one of several* supported authentication modes for an endpoint (useful when the
  proxy itself runs in Azure and the target is Azure AI Foundry / Azure OpenAI), but it is optional, per-endpoint
  configuration — the solution itself has no hard Azure dependency.

## 2. Scope of Iteration 1

### In scope (maps to the numbered requirements)

| # | Requirement | Covered by |
|---|---|---|
| 1 | No Azure dependencies | PostgreSQL + self-hosted Vault + container runtime; Azure SDK only loaded as an optional, per-endpoint credential provider |
| 2 | OpenAI API support | `/openai/v1/...` ingress: chat completions, completions, embeddings, images (generations/edits/variations), the **Responses API** (`/responses`, stateful via `previous_response_id`); OpenAI-compatible request/response passthrough, extensible to further operation types via a pluggable usage extractor (§4, §10) |
| 3 | Anthropic API support | `/anthropic/v1/messages` ingress, Anthropic Messages API passthrough (multimodal image input inside a message already works unmodified — passthrough doesn't care about content shape) |
| 4 | Container + PostgreSQL datastore | Docker/Aspire container topology; EF Core + Npgsql |
| 4.1 | ... for configuration | `ModelDeployments` / `Endpoints` / `Consumers` tables, config REST API |
| 4.2 | ... for logging (token usage) | `UsageRecords` table, written per request/stream, attributed to a `Consumer` resolved from the caller's API key |
| 5 | OpenTelemetry (traces, logs, metrics) | OTLP exporter → Aspire dashboard locally, OTLP collector in prod |
| 6 | Configuration | see §7 |
| 6.1 | Model-deployment name | `ModelDeployment.Name` (routing key) |
| 6.2 | 1+ endpoints, priority + failover | `Endpoint.Priority`, YARP cluster destinations, active/passive health checks |
| 6.3 | Sticky sessions for stateful calls | YARP session affinity keyed by session id, persisted in Postgres |
| 6.4 | API key vs Managed Identity per endpoint | `Endpoint.AuthMode` + `ICredentialProvider` strategy |
| 6.5 | Configuration REST API | `/api/v1/config/*` CRUD endpoints, incl. `Consumer` (API key) management |
| 7 | Secure API key storage, container-based | HashiCorp Vault (OSS) container, KV v2 engine |
| 8 | Streaming + forced usage accounting | Force `stream_options.include_usage` / Anthropic usage event; capture server-side regardless of client request |

### Explicit non-goals for Iteration 1 (future work)

- Cross-protocol translation (e.g. client speaks OpenAI schema, backend is Anthropic-only). Iteration 1 requires the
  ingress protocol family to match the backend protocol family for a given deployment.
- Multi-tenant RBAC / OAuth2 for the configuration API (a single admin API key is used as a placeholder).
- Rate limiting / quota enforcement per client.
- Response caching / semantic caching.
- Admin UI (only REST API + OpenAPI).
- Automatic secret rotation workflows (Vault supports it; wiring it up is future work).

## 3. High-Level Architecture

![High-level architecture](diagrams/01-high-level-architecture.png)

*Source: [diagrams/01-high-level-architecture.drawio](diagrams/01-high-level-architecture.drawio)*

## 4. Solution Structure (Clean Architecture)

Dependencies point **inward**. Nothing in `Domain` or `Application` references ASP.NET Core, EF Core, YARP, Vault, or
OpenTelemetry — those are all `Infrastructure` concerns wired up in `Api` (composition root) and `AppHost`.

```
ai-proxy/
├── src/
│   ├── AiProxy.Domain/            # Entities, value objects, domain events, repository interfaces (ports)
│   ├── AiProxy.Application/       # Use cases (CQRS-ish handlers), DTOs, validation, application-level interfaces
│   ├── AiProxy.Infrastructure/    # EF Core + Npgsql, Vault client, YARP integration, credential providers,
│   │                              # HTTP forwarding, OpenTelemetry instrumentation, background jobs
│   ├── AiProxy.Api/                # ASP.NET Core minimal API host, endpoint mapping, middleware, composition root
│   ├── AiProxy.ServiceDefaults/    # Aspire shared service defaults (OTel, health checks, resilience defaults)
│   └── AiProxy.AppHost/            # .NET Aspire AppHost — orchestrates Api, Postgres, Vault, Aspire Dashboard
├── tests/
│   ├── AiProxy.Domain.Tests/               # Unit tests, no I/O
│   ├── AiProxy.Application.Tests/          # Unit tests w/ mocked ports
│   ├── AiProxy.Infrastructure.Tests/       # Integration tests — Testcontainers (Postgres, Vault)
│   ├── AiProxy.Api.Tests/                  # Integration tests — WebApplicationFactory + WireMock.Net fake backends
│   └── AiProxy.ArchitectureTests/          # NetArchTest rules enforcing the dependency graph above
├── logs/                # Runtime debug logs (Serilog file sink) — gitignored, kept via .gitkeep
├── test-results/         # dotnet test TRX/coverage output — gitignored, kept via .gitkeep
├── docker-compose.yml    # Postgres + Vault + Api for non-Aspire container runs
└── specs/
    ├── architecture/
    └── iterations/
```

### Layer responsibilities

- **Domain** — `ModelDeployment`, `Endpoint`, `UsageRecord`, `SessionAffinityEntry`, `Consumer`, value objects
  (`ApiFamily`, `AuthMode`, `RoutingPriority`, `OperationType` — ChatCompletion, Completion, Embedding,
  ImageGeneration, ImageEdit, ImageVariation, ResponsesApi, extensible to more), domain invariants (e.g. a
  deployment must have ≥1 enabled endpoint; a `UsageRecord` must always reference a valid `Consumer`; token
  fields are optional but at least one usage signal — tokens or `UsageMetrics` — must be present).
- **Application** — `IModelDeploymentRepository`, `IUsageRecorder`, `ISecretResolver`, `IEndpointSelector`,
  `IConsumerRepository`, `IConsumerAuthenticator`, `IUsageExtractor` (one implementation per `ApiFamily` +
  `OperationType` combination, resolved via `IUsageExtractorResolver`) ports; command/query handlers for
  configuration CRUD and usage queries; validation (FluentValidation).
- **Infrastructure** — implements all ports: `AiProxyDbContext` (EF Core/Npgsql), `VaultSecretResolver`
  (VaultSharp), `ApiKeyCredentialProvider` / `ManagedIdentityCredentialProvider`, YARP `IProxyConfigProvider`,
  custom `ITransformProvider` for auth injection, per-operation `IUsageExtractor` implementations (e.g.
  `ChatCompletionUsageExtractor`, `ImageUsageExtractor`), OpenTelemetry resource/exporter setup.
- **Api** — maps ingress routes (`/openai/v1/*`, `/anthropic/v1/messages`), configuration routes
  (`/api/v1/config/*`), health endpoints (`/health/live`, `/health/ready`), OpenAPI, DI composition.
- **ServiceDefaults / AppHost** — Aspire wiring for local development: starts Postgres, Vault, the Api, and the
  Aspire dashboard, and injects OTLP endpoint + connection strings via service discovery.

## 5. Domain Model

![Domain model](diagrams/02-domain-model.png)

*Source: [diagrams/02-domain-model.drawio](diagrams/02-domain-model.drawio)*

## 6. Request Flow

### 6.1 Non-streaming chat completion, with failover

![Sequence: non-streaming chat completion with failover](diagrams/03-sequence-non-streaming-failover.png)

*Source: [diagrams/03-sequence-non-streaming-failover.drawio](diagrams/03-sequence-non-streaming-failover.drawio)*

### 6.2 Streaming chat completion, sticky session, forced usage capture

![Sequence: streaming chat completion with sticky session and usage capture](diagrams/04-sequence-streaming-sticky-usage.png)

*Source: [diagrams/04-sequence-streaming-sticky-usage.drawio](diagrams/04-sequence-streaming-sticky-usage.drawio)*

### 6.3 Non-token operations (e.g. image generation)

Embeddings and Images requests flow through the exact same ingress → routing → forwarder pipeline as chat
completions — deployment resolution still peeks the `model` field (Images API requests carry one too), and
failover/sticky-session behavior is identical. The only thing that changes per operation type is **which**
`IUsageExtractor` is invoked: chat completions and embeddings extract `prompt_tokens`/`completion_tokens`/
`total_tokens`; image generation/edit/variation extract whatever billing signal the response actually contains
(e.g. image count, size, quality) into `UsageRecord.UsageMetrics`, since most Images API responses don't report
token usage. Image requests are treated as non-streaming request/response for Iteration 1 (see §10).

### 6.4 Responses API (stateful chaining via `previous_response_id`)

The Responses API (`/openai/v1/responses`) flows through the same pipeline too, but it's the one OpenAI operation
that is stateful *by OpenAI's own design*: unless a caller passes `store: false`, OpenAI persists each response
server-side, and a follow-up turn references it via `previous_response_id` instead of resending prior messages.
That id only resolves on the endpoint that originally created it, so it's treated as an implicit sticky-session
key (see §9) — no client-supplied `session`/`X-Session-Id` is required for this operation type. Usage is
extracted the same way as any other operation, just via the Responses-specific `IUsageExtractor` (§10).

## 7. Configuration & Persistence

### 7.1 PostgreSQL schema (initial)

| Table | Purpose | Key columns |
|---|---|---|
| `model_deployments` | Routing key + policy | `id`, `name` (unique), `sticky_session_enabled`, `strategy` |
| `endpoints` | Backend targets per deployment | `id`, `model_deployment_id`, `base_url`, `api_family`, `priority`, `auth_mode`, `secret_ref`, `enabled` |
| `consumers` | Callers allowed to invoke the proxy | `id`, `name` (unique), `api_key_hash`, `enabled`, `created_at`, `revoked_at` |
| `usage_records` | Usage log (append-only) | `id`, `timestamp`, `model_deployment_name`, `consumer_id`, `consumer_name`, `endpoint_id`, `operation_type`, `prompt_tokens?`, `completion_tokens?`, `total_tokens?`, `usage_metrics` (jsonb), `streaming`, `status_code`, `duration_ms` |
| `session_affinity` | Sticky session map | `session_id`, `model_deployment_id`, `endpoint_id`, `expires_at` |
| `__ef_migrations_history` | EF Core migrations | standard |

Access via EF Core + Npgsql, code-first migrations. `usage_records` is append-only and indexed on
`(model_deployment_name, timestamp)` and `(consumer_id, timestamp)` for chargeback queries per deployment and per
consumer. `consumers.api_key_hash` stores a salted one-way hash (e.g. HMAC-SHA256 with a server-side pepper) of the
consumer's inbound API key — never the key itself — so a Postgres read compromise does not leak usable credentials.
`prompt_tokens`/`completion_tokens`/`total_tokens` are nullable — only token-based operations (chat completions,
completions, embeddings) populate them; other operation types (e.g. image generation) populate the flexible
`usage_metrics` jsonb column instead, so new modalities can report whatever billing signal they have without a
schema migration. Configuration reads are cached in-memory with a short TTL + invalidated on config API writes
(via an in-process change token; a Postgres `LISTEN/NOTIFY` channel is a possible future enhancement for
multi-instance cache invalidation).

### 7.2 Configuration REST API (`/api/v1/config`)

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/config/model-deployments` | List deployments (with endpoints) |
| GET | `/api/v1/config/model-deployments/{name}` | Get one deployment |
| POST | `/api/v1/config/model-deployments` | Create deployment |
| PUT | `/api/v1/config/model-deployments/{name}` | Update deployment (policy fields) |
| DELETE | `/api/v1/config/model-deployments/{name}` | Delete deployment (cascades endpoints) |
| POST | `/api/v1/config/model-deployments/{name}/endpoints` | Add endpoint |
| PUT | `/api/v1/config/model-deployments/{name}/endpoints/{id}` | Update endpoint (priority, enabled, timeout...) |
| DELETE | `/api/v1/config/model-deployments/{name}/endpoints/{id}` | Remove endpoint |
| PUT | `/api/v1/config/model-deployments/{name}/endpoints/{id}/secret` | Set/replace the API key (writes to Vault, never returned by GET) |
| GET | `/api/v1/config/consumers` | List consumers (name, enabled, created/revoked — never the key or its hash) |
| POST | `/api/v1/config/consumers` | Create a consumer; response includes the **plaintext API key exactly once** — only its hash is persisted |
| PUT | `/api/v1/config/consumers/{name}/rotate` | Rotate a consumer's API key; returns the new plaintext key once, invalidates the old one |
| DELETE | `/api/v1/config/consumers/{name}` | Revoke a consumer (soft delete via `revoked_at`; historical `usage_records` are preserved) |
| GET | `/api/v1/usage` | Query usage records (filter by deployment, consumer, date range) |

All write routes require an admin API key (`X-Admin-Api-Key` header, validated against a hash stored in Vault) for
Iteration 1. Requests/responses are validated with FluentValidation; secrets are **write-only** — no GET ever returns
key material, only the `secret_ref` path (endpoints) or consumer metadata without the key/hash (consumers).

## 8. Secrets Management

**HashiCorp Vault (OSS)** runs as a container alongside the proxy (dev mode locally via Aspire; `file` or
`raft` storage backend in real deployments). API keys are stored under a **KV v2** engine at
`secret/ai-proxy/endpoints/{endpointId}`. The proxy authenticates to Vault using **AppRole** (role id + wrapped
secret id supplied via environment/orchestrator secret, never committed) — for local Aspire runs, dev-mode Vault
with a well-known root token confined to the dev environment is acceptable.

- `Endpoint.SecretRef` in Postgres stores only the Vault **path**, never the secret value.
- `ManagedIdentity`-mode endpoints have no Vault entry; the proxy instead acquires an Entra ID access token via
  `Azure.Identity` (`DefaultAzureCredential`/`ManagedIdentityCredential`) scoped to the target resource
  (e.g. Azure AI Foundry / Cognitive Services scope) and attaches it as a bearer token. This is the **only**
  place the solution touches an Azure SDK, and it's opt-in per endpoint.
- Resolved secrets are cached in-memory with a short TTL (e.g. 5 minutes) to avoid a Vault round trip per request,
  and are never logged (Serilog destructuring policies + explicit redaction for auth headers).
- **Scope note**: Vault holds secrets the proxy must *present outbound* (endpoint credentials, the admin key hash).
  Inbound **Consumer** API keys (see the domain model in §5 and authentication in §13) are a different concern —
  the proxy only ever needs to *verify* them, never retrieve or replay them — so they are stored as a salted
  one-way hash directly in `consumers.api_key_hash` (Postgres), avoiding a Vault round trip on every single
  ingress request. The **pepper** (a server-wide secret mixed into that hash to resist offline cracking if the
  `consumers` table ever leaks) is itself a real secret the app must retrieve, so it lives in Vault at
  `secret/ai-proxy/consumer-key-pepper`, read once at startup and cached in memory — not the individual consumer
  keys, just this single shared value.

## 9. Routing & Failover

- Built on **YARP** (`Yarp.ReverseProxy`), the same library family the original proxy used, but with a
  **dynamic, database-backed** `IProxyConfigProvider` instead of static JSON — clusters/destinations are rebuilt
  from `model_deployments`/`endpoints` whenever configuration changes.
- **Deployment resolution**: the ingress endpoint peeks the request body (`model` field for OpenAI schema,
  `model` field for Anthropic schema) to determine the target `ModelDeployment` before YARP forwarding — this
  is the one place the proxy must buffer the body, and it's covered by a dedicated integration test for both
  large payloads and streaming requests. The same `model`-field peek covers every OpenAI-compatible operation
  type (chat completions, completions, embeddings, images) since they all carry a `model` field — no
  per-operation-type routing logic is needed.
- **Priority + failover**: destinations are ordered by `Endpoint.Priority`. A custom load-balancing policy tries
  the lowest-priority-number healthy destination first; YARP active health checks (periodic) and passive health
  checks (on transport errors/5xx) mark an endpoint unhealthy for a cooldown window, at which point the next
  priority is tried — mirroring the original `ThrottlingHealthPolicy` idea but generalized beyond 429s.
- **Sticky sessions (6.3)**: YARP **session affinity** is enabled per deployment when `StickySessionEnabled` is
  true. The affinity key is resolved per operation type: for chat completions it's a client-supplied session
  identifier (a `session`/`conversation-id` field in the request body or an `X-Session-Id` header —
  configurable); for the **Responses API** it's the caller's `previous_response_id`, if present. The affinity map
  itself is persisted in `session_affinity` (Postgres) rather than only in-memory, so the "one endpoint per
  stateful session" guarantee survives proxy restarts/multiple replicas. If the affinity-selected endpoint
  becomes unhealthy, a documented failure policy applies (Iteration 1: fail the request with a clear error
  rather than silently breaking session state — silently rerouting a stateful session is unsafe).
- **Responses API chaining**: the Responses API is stateful by default (OpenAI persists each response server-side
  unless the caller sets `store: false`), and a follow-up call references the prior turn via
  `previous_response_id` — that id only resolves on the endpoint that created it. So whenever a `/responses`
  call completes, the proxy reads the response's own `id` field and writes a `session_affinity` row
  (`session_id` = that response id → the endpoint used); the *next* call that sends `previous_response_id`
  looks itself up the same way a client session id would. No new storage concept is needed —
  `session_affinity.session_id` is just an opaque affinity key, whether it came from a client or from an
  OpenAI-issued response id. If the client sets `store: false`, there is no chain to preserve and no affinity
  row is written.
- **Retries**: bounded per-endpoint retry (`Endpoint.MaxRetries`) with jittered backoff (Polly, via
  `Microsoft.Extensions.Http.Resilience`) before failing over to the next priority.

## 10. Streaming & Usage Capture (8)

- Usage extraction is **pluggable per operation type** via `IUsageExtractor`/`IUsageExtractorResolver` (§4), so
  adding a new modality later (e.g. audio) only means adding a new extractor implementation, not changing the
  ingress/routing/forwarding pipeline.
- For OpenAI-family **chat completions** requests, the proxy always sets `"stream_options": {"include_usage": true}`
  on the outbound request when `stream: true`, regardless of whether the client set it. The **Responses API**
  always includes a `usage` object on the completed response (in the terminal `response.completed` SSE event
  when streaming, or directly in the JSON body otherwise) — no request mutation is needed to force it there.
- For Anthropic-family requests, usage is included in the `message_start` and terminal `message_delta` events by
  default; the proxy simply parses these — no request mutation needed there.
- A streaming `ITransformProvider`/response-body interceptor reads the SSE byte stream as it flows through,
  splitting on `\n\n`, parsing `data: {...}` JSON payloads to detect the usage-bearing event (the specific event
  name/shape depends on the operation type — chat completions' final chunk, or the Responses API's
  `response.completed` event with an `input_tokens`/`output_tokens`/`total_tokens` usage object), recording it,
  and otherwise passing bytes through untouched (no buffering of the whole stream — must preserve
  time-to-first-byte).
- If the **client's original request did not** ask for usage (`include_usage` absent for OpenAI, or an Anthropic
  client that doesn't expect a usage-only trailing event), the proxy removes the *injected* usage-only chunk from
  what is sent downstream, so the client-visible stream is unchanged relative to calling the backend directly.
  When the client **did** ask for it, the chunk is passed through unmodified.
- Non-streaming responses (embeddings, images, and any chat/completions call without `stream: true`) are simpler:
  the JSON body already contains the relevant usage signal — a token-style `usage` object, or an operation-specific
  field set for images; the matching `IUsageExtractor` parses it and the original body is forwarded unmodified.
- Usage is written to `usage_records` asynchronously (fire-and-forget with a bounded channel + background
  writer) so it never adds latency to the response path; failures to persist usage are logged, not surfaced to
  the client.

## 11. Observability (OpenTelemetry)

- **Traces**: one root `Activity` per ingress request (`ai_proxy.request`), child spans for deployment
  resolution, secret resolution, backend call, and usage extraction. Standard `http.client`/`http.server`
  auto-instrumentation via `OpenTelemetry.Instrumentation.AspNetCore`/`.Http`.
- **Metrics**: custom `Meter("AiProxy")` with instruments:
  - `ai_proxy.requests.count` (tags: deployment, api_family, endpoint, status)
  - `ai_proxy.tokens.prompt` / `ai_proxy.tokens.completion` / `ai_proxy.tokens.total` (tags: deployment, endpoint)
  - `ai_proxy.request.duration` (histogram, tags as above)
  - `ai_proxy.failover.count` (tags: deployment, from_endpoint, to_endpoint)
  - Standard ASP.NET Core / HttpClient / Npgsql instrumentation metrics.
- **Logs**: Serilog configured as the ILogger provider, enriched with trace/span ids, exported via OTLP **and**
  written to `logs/ai-proxy-.log` (rolling file) for local debugging — see §14.
- **Local dev**: everything exports via **OTLP** to the **.NET Aspire dashboard**, started by
  `AiProxy.AppHost`. The `AiProxy.ServiceDefaults` project centralizes this wiring (the standard Aspire pattern)
  so the same code exports to any OTLP collector in non-Aspire/production deployments (configured purely via
  `OTEL_EXPORTER_OTLP_ENDPOINT`).

## 12. API Surface Summary

| Group | Routes | Notes |
|---|---|---|
| OpenAI ingress (text) | `POST /openai/v1/chat/completions`, `POST /openai/v1/completions`, `POST /openai/v1/embeddings` | Passthrough proxy, streaming supported on chat/completions |
| OpenAI ingress (images) | `POST /openai/v1/images/generations`, `POST /openai/v1/images/edits`, `POST /openai/v1/images/variations` | Passthrough proxy, non-streaming, usage captured via the image `IUsageExtractor` |
| OpenAI ingress (responses) | `POST /openai/v1/responses` | Passthrough proxy, streaming and non-streaming; stateful via `previous_response_id` — see §9 |
| Anthropic ingress | `POST /anthropic/v1/messages` | Passthrough proxy, streaming supported |
| Configuration | `/api/v1/config/*` | See §7.2, incl. `consumers` CRUD |
| Usage query | `GET /api/v1/usage` | Chargeback / reporting, filterable by deployment and consumer |
| Health | `GET /health/live`, `GET /health/ready` | Standard ASP.NET Core health checks (Postgres, Vault reachability) |
| OpenAPI | `/openapi/v1.json`, Scalar/Swagger UI in dev | Generated from minimal API metadata |

## 13. Security Considerations (OWASP-aligned)

- **Secrets**: never stored in Postgres or logs; Vault is the single source of truth; in-memory cache is
  process-local and short-lived; Serilog enrichers redact `Authorization`/`api-key` headers.
- **SSRF**: `Endpoint.BaseUrl` is admin-supplied (trusted input via the config API), but is still validated
  (must be `https://` except explicit `http://localhost`/loopback allowance for local dev) to avoid accidental
  misconfiguration pointing at internal metadata endpoints.
- **AuthN/AuthZ**: the configuration API requires an admin key (hashed at rest in Vault, compared with a
  constant-time comparison). Ingress inference routes require every caller to present an API key that resolves to
  an enabled `Consumer` (`consumers.api_key_hash` lookup, constant-time comparison) — this is what lets every
  `UsageRecord` attribute tokens back to the calling application/team for chargeback, and stops the proxy from
  being an open relay. Requests with no key, an unknown key, or a revoked consumer are rejected with `401` before
  any routing/forwarding work happens.
- **Input validation**: FluentValidation on all configuration DTOs; request bodies for ingress routes are only
  partially parsed (enough to read `model`/session id) and otherwise streamed through unmodified — the proxy
  does not attempt to fully validate arbitrary upstream API schemas.
- **Transport security**: TLS to all backends; HSTS + TLS termination in front of the proxy in real deployments.
- **Dependency hygiene**: Dependabot-equivalent (e.g. `dotnet outdated` / GitHub Dependabot) retained from the
  original repo's practice.
- **Least privilege**: Vault AppRole policy scoped only to `secret/data/ai-proxy/*`; Postgres role for the app
  has no superuser/DDL rights outside of the migration step.

## 14. Deployment Topology & Local Development

- **Local development**: `AiProxy.AppHost` (.NET Aspire) starts:
  - `ai-proxy-api` (the .NET 10 API)
  - `postgres` (official `postgres` image, with a named volume)
  - `vault` (official `hashicorp/vault` image, dev mode for local, config-file mode for anything shared)
  - the **Aspire dashboard**, which receives OTLP traces/metrics/logs from the API via service-discovery-injected
    `OTEL_EXPORTER_OTLP_ENDPOINT`.
  - `AiProxy.ServiceDefaults` wires health checks, resilience, and OpenTelemetry consistently for the API project.
- **Container-only (no Aspire)**: `docker-compose.yml` at the repo root stands up the same three containers for
  environments where Aspire isn't used (e.g. CI, or a simple `docker compose up` for manual testing), with an
  external OTLP collector endpoint configurable via env var.
- Model endpoints themselves are **never** part of this topology — they are just URLs in configuration, which may
  point at `http://localhost:11434` (Ollama), `https://api.openai.com`, `https://api.anthropic.com`, or an Azure
  AI Foundry endpoint.

![Deployment topology](diagrams/05-deployment-topology.png)

*Source: [diagrams/05-deployment-topology.drawio](diagrams/05-deployment-topology.drawio)*

## 15. Testing Strategy

Clean Architecture is enforced and verified, not just aspirational:

| Test project | Type | Tooling | What it covers |
|---|---|---|---|
| `AiProxy.Domain.Tests` | Unit | xUnit, FluentAssertions | Entity invariants, value objects, routing/failover selection logic |
| `AiProxy.Application.Tests` | Unit | xUnit, FluentAssertions, NSubstitute | Use case handlers with mocked ports, validation rules |
| `AiProxy.Infrastructure.Tests` | Integration | xUnit, Testcontainers (Postgres + Vault), VaultSharp, EF Core | Repository behavior, migrations, VaultSecretResolver against a real Vault container, YARP config provider rebuild logic |
| `AiProxy.Api.Tests` | Integration | xUnit, `WebApplicationFactory`, WireMock.Net (fake OpenAI/Anthropic backends) | End-to-end ingress routing, failover, sticky sessions, streaming usage capture/suppression, config CRUD API, authz |
| `AiProxy.ArchitectureTests` | Architecture | NetArchTest.Rules | Domain has no dependency on Infrastructure/Api/ASP.NET Core/EF Core; Application has no dependency on Infrastructure |

- Fake upstream OpenAI/Anthropic servers (WireMock.Net or minimal test-only ASP.NET apps) simulate success,
  latency, 5xx, timeouts, and both streaming/non-streaming responses with and without usage, including Images
  API fixtures (generations/edits/variations) to exercise the image `IUsageExtractor` — this is what makes
  failover, sticky sessions, and usage-capture testable without hitting real provider APIs or incurring cost.
- Target coverage: meaningful line/branch coverage on `Domain`/`Application` (business logic) is prioritized over
  a raw percentage; `coverlet.collector` + a coverage summary is produced per run.
- **All debug and test output is written under the workspace**, not to a temp directory or the console only:
  - `logs/` — Serilog rolling file sink for the running API (`logs/ai-proxy-.log`), both when run locally and
    when run by integration tests that boot the API in-process.
  - `test-results/` — `dotnet test --logger "trx" --results-directory test-results` output (TRX + coverage
    Cobertura files), one subfolder per test run/project.
  - Both folders are git-ignored except for a `.gitkeep`, and are created by the test/CI tasks if missing.

## 16. Technology Choices

| Concern | Choice | Rationale |
|---|---|---|
| Runtime | .NET 10 | Latest LTS-track SDK, required by the user |
| API style | ASP.NET Core Minimal APIs | Lightweight, fast, matches Clean Architecture composition root pattern |
| Reverse proxy engine | YARP (`Yarp.ReverseProxy`) | Same family as the original proxy; supports dynamic config, session affinity, health checks, custom transforms |
| Local orchestration | .NET Aspire (AppHost + ServiceDefaults) | Required by the user; first-class OTLP wiring + container orchestration for local dev |
| Database | PostgreSQL + EF Core/Npgsql | Explicitly requested; portable, container-friendly |
| Secret store | HashiCorp Vault (OSS), container | Runs in a container as required; industry-standard KV secret engine, supports AppRole and future rotation |
| Resilience | `Microsoft.Extensions.Http.Resilience` (Polly v8) | Standard .NET-idiomatic retry/circuit-breaker, integrates with YARP/HttpClient |
| Observability | OpenTelemetry SDK + OTLP exporter | Explicitly requested; vendor-neutral |
| Optional Azure auth | `Azure.Identity` | Only loaded/used for endpoints configured with `AuthMode = ManagedIdentity` |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers, WireMock.Net, NetArchTest | Standard, well-supported .NET test stack covering unit/integration/architecture tests |

## 17. Assumptions & Open Questions

- The config API's admin authentication is a placeholder (static API key) for Iteration 1; a follow-up iteration
  should evaluate OAuth2/Entra ID/mTLS depending on where the proxy is deployed.
- Session/conversation id extraction defaults to a `session` field in the request body with an `X-Session-Id`
  header override — needs confirmation against real client usage patterns.
- Cross-protocol translation (OpenAI ↔ Anthropic) is explicitly deferred; if needed sooner, it changes the
  ingress/routing design meaningfully and should be scoped as its own iteration.
- Vault is chosen over alternatives (Docker Secrets, SOPS, age-encrypted files) because it offers dynamic
  secrets, an HTTP API, audit logging, and a mature .NET client (VaultSharp) while still running fully in a
  container — this can be revisited if operational overhead proves too high for the target environment.
- Consumer API keys are presented by callers as a bearer/`api-key`-style header (exact header name to be finalized
  during implementation, e.g. `X-Api-Key`); the plaintext key is shown to the admin **exactly once** at
  creation/rotation time and only its salted hash is ever persisted — losing the plaintext means rotating, not
  recovering, the key.
- Not all providers return a token-style `usage` object for image generation (behavior varies by model/provider
  and is still evolving); `UsageRecord.UsageMetrics` is intentionally a flexible jsonb bag rather than fixed
  columns so Iteration 1 can capture whatever signal is actually available (image count/size/quality) without
  guessing at a universal schema.
- Responses API affinity rows (keyed by OpenAI response id) are written with an expiry mirroring OpenAI's default
  30-day response retention (configurable); this needs revisiting once real usage patterns/retention settings
  are known.

## 18. Non-Goals / Future Iterations

- Rate limiting & per-client quotas.
- Cross-protocol request/response translation.
- Admin UI.
- Multi-region / multi-instance cache invalidation via Postgres `LISTEN/NOTIFY` (Iteration 1 uses TTL-based cache
  expiry, which is acceptable for a single-instance or short-TTL multi-instance deployment).
- Automatic Vault secret rotation workflows.
- Audio (speech-to-text, text-to-speech) and moderation endpoints — supported in principle by the same pluggable
  ingress + usage-extractor design, but not implemented in Iteration 1.
- Additional providers beyond OpenAI-compatible and Anthropic (e.g. Google Gemini, Cohere).
