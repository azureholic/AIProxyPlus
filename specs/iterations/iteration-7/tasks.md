# Iteration 7 — Reusable API Routes

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration introduces
reusable API-route configuration so a model can combine a shared endpoint host and credential with the
provider-specific API surface exposed by that host.

## Proposed model

- `Endpoint` continues to own connection and credential configuration: base URL, authentication mode, secret or
  managed identity settings, timeout, and retries.
- `ApiRoute` owns API-surface configuration: display name, provider, and normalized path prefix.
- `ModelEndpoint` selects both an `Endpoint` and an `ApiRoute`, in addition to priority, enabled state,
  and runtime health.
- Ingress remains stable. Clients continue to call the proxy's existing `/openai/v1/*` and `/anthropic/v1/*`
  routes; only the selected upstream path is rewritten.
- Provider adapters own operation suffixes. Administrators select a provider and path prefix rather than entering
  a separate arbitrary path template for every operation.

## Built-in API routes

Seed these routes with stable identifiers in the database migration. Seeding must be deterministic and must not
replace an administrator's later edits when the application restarts.

| Name | Provider | Path prefix | Example image-generation target |
| --- | --- | --- | --- |
| Foundry OpenAI | `OpenAI` | `/openai/v1` | `/openai/v1/images/generations` |
| Microsoft AI | `MicrosoftAI` | `/mai/v1` | `/mai/v1/images/generations` |
| Anthropic | `Anthropic` | `/anthropic/v1` | Not supported |
| Native OpenAI | `OpenAI` | `/v1` | `/v1/images/generations` |

## Phase 1 — Architecture specification

- [x] Update the architecture specification to separate endpoint connectivity, API-route selection, credential
  placement, upstream path resolution, and usage extraction responsibilities currently grouped under
  `ApiFamily`.
- [x] Define the `ApiRoute` aggregate, provider vocabulary, normalized path-prefix rules, uniqueness rules, and
  delete behavior while assigned.
- [x] Decide whether provider is a closed enum or an extensible identifier, and document how unsupported
  provider/operation combinations fail before any upstream request is sent.
- [x] Define the `ModelEndpoint` identity and invariants after adding `ApiRouteId`, including whether
  the same endpoint may be assigned more than once to one model through different API routes.
- [x] Define upstream URI composition as `Endpoint.BaseUrl + ApiRoute.PathPrefix + operation suffix + query`,
  with exactly one slash at each boundary and no forwarding of the proxy ingress prefix.
- [x] Define provider adapters for operation suffixes, credential-header placement, request mutation, response
  passthrough, and usage extraction without cross-translating OpenAI and Anthropic payloads.
- [x] Define behavior for HTTP and Realtime routing, Responses API affinity, retry/failover, telemetry labels,
  and usage attribution when an assignment selects an API route.
- [x] Update the domain and relevant sequence diagrams before implementation begins.

## Phase 2 — Domain and persistence

- [x] Add the reusable `ApiRoute` aggregate and provider value type with validation for names and normalized path
  prefixes.
- [x] Add `ApiRouteId` and navigation to the model endpoint assignment according to the approved
  identity and uniqueness design.
- [x] Add `api_routes` persistence, repository abstraction/implementation, EF configuration, and assignment
  relationships with delete protection.
- [x] Add a migration that creates the route schema, seeds the four built-in routes above with stable IDs, and
  maps every existing assignment to the compatible seeded route without losing routing state.
- [x] Make database seeding migration-owned and idempotent across fresh databases and upgrades; do not perform
  mutable startup seeding.
- [x] Add migration tests for fresh creation, upgrade preservation, deterministic seed values, assignment
  backfill, uniqueness, and downgrade safety.
- [x] Add domain and repository tests for route validation, reuse, assignment loading, and assigned-route delete
  conflicts.

## Phase 3 — Application and configuration API

- [x] Add validated `ApiRoute` list/get/create/update/delete handlers and repository ports.
- [x] Add admin-protected `/api/v1/config/api-routes` CRUD endpoints with OpenAPI metadata and conflict responses
  for assigned routes.
- [x] Extend model assignment commands and responses to require and expose both `endpointId` and
  `apiRouteId`.
- [x] Preserve endpoint secret handling and ensure API-route reads never duplicate or expose credential material.
- [x] Add application and API tests for route CRUD, seeded-route visibility, endpoint/route reuse across model
  configurations, invalid combinations, and delete protection.

## Phase 4 — Upstream routing

- [x] Introduce an upstream route resolver keyed by provider and `OperationType`.
- [x] Change HTTP forwarding to use the resolved upstream URI rather than copying `HttpContext.Request.Path`
  verbatim.
- [x] Support the seeded OpenAI, Microsoft AI, and Anthropic path profiles while preserving protocol passthrough
  boundaries.
- [x] Move credential-header selection and usage-extractor resolution to the approved provider/route design so
  Microsoft AI API-key requests use the required `api-key` header.
- [x] Preserve retry/failover, health tracking, first-upstream-error replay, Responses API affinity, streaming,
  and usage attribution across route resolution.
- [x] Add focused routing tests proving at least `/openai/v1/images/generations` can target both
  `/openai/v1/images/generations` and `/mai/v1/images/generations` based on the selected assignment route.
- [x] Add regression coverage for chat, embeddings, Responses API, Anthropic messages, and Realtime behavior.

## Phase 5 — Frontend

- [x] Add Config > API Routes beside Models, Endpoints, and Consumers.
- [x] Add API-route list/create/edit/delete flows with provider selection, path-prefix validation, built-in-route
  visibility, assigned-route conflict handling, and no credential fields.
- [x] Update model assignment UI to select both an existing endpoint and an existing API route, then
  configure priority and enabled state.
- [x] Show endpoint and API-route names together on model details so the effective target is understandable
  without exposing secrets.
- [x] Add frontend API-contract and interaction coverage for route management and assignment selection.

## Phase 6 — Documentation and validation

- [x] Update README configuration, API, migration, and provider-route examples.
- [x] Update architecture diagrams and export their rendered images.
- [x] Run focused domain, application, infrastructure, API, architecture, and frontend tests.
- [x] Run the full .NET solution, frontend tests/build, migration validation against pre-Iteration-7 data, and
  live browser acceptance.
- [ ] Verify live calls through one shared Foundry endpoint using both the Foundry OpenAI and Microsoft AI seeded
  routes, including MAI image generation.
