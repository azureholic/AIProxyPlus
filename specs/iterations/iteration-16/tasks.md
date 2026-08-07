# Iteration 16 — Dual Endpoint Credentials & Bounded Rotation Fallback

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). Iteration 16 runtime behavior
is implemented; unchecked items below remain explicit validation or follow-up gaps.

## Goal

Add two independently managed outbound API-key slots to each `ApiKey` endpoint so administrators can rotate a
backend credential without interrupting traffic when that provider supports overlapping keys. Slot 1 remains
required; Slot 2 is optional because not every backend supports multiple active credentials.

During an explicitly enabled, time-bounded cutover window, AI Proxy may retry one request with the previous slot
when the newly active credential receives an unambiguous upstream authentication rejection. This is a rotation
safety mechanism, not a permanent "try every key" policy.

## Controlling decisions

### Endpoint aggregate and persistence

- Add `EndpointCredentialSlot { Primary, Secondary }` to the Domain vocabulary.
- Replace the single mutable endpoint secret reference with `PrimarySecretRef` (required for `ApiKey` endpoints),
  `SecondarySecretRef?`, `ActiveCredentialSlot`, `FallbackCredentialSlot?`, and `FallbackExpiresAt?`.
- Managed-identity endpoints have no API-key slots, active slot, or fallback window.
- The active slot must reference a configured secret. A fallback slot must be configured, differ from the active
  slot, and have a future expiry. Expired fallback is treated as disabled even before background cleanup persists
  that state.
- Slot 1 remains the backward-compatible active slot for migrated endpoints. Existing `SecretRef` values migrate
  to `PrimarySecretRef`; Slot 2 and fallback fields remain null.
- Do not publish a domain event for cache invalidation. Credential-slot invariants belong to the `Endpoint`
  aggregate; replica notification is an Infrastructure concern.

### Vault storage and cache coherence

- Vault remains the only store for plaintext outbound credentials. PostgreSQL stores references and slot state,
  never key material.
- Every slot replacement writes to a new immutable Vault reference, for example
  `endpoints/{endpointId}/primary/{credentialVersionId}`. It never overwrites the path currently referenced by
  PostgreSQL.
- The write sequence is: write the new Vault secret, atomically update the endpoint slot reference in PostgreSQL,
  publish an invalidation notification after commit, then retire the unreferenced Vault version after a grace
  period. If the database update fails, the new unreferenced Vault value is cleanup work but cannot become active.
- Cache keys include the immutable secret reference. Once PostgreSQL points to a new reference, every replica
  naturally misses its old local entry; correctness does not depend on sharing plaintext through a distributed
  cache.
- PostgreSQL `LISTEN/NOTIFY` broadcasts only endpoint id and affected secret reference/version, never plaintext.
  Every replica evicts matching local entries and any future endpoint-configuration cache. The existing five-minute
  TTL remains a bounded fallback for missed notifications and orphaned entries.
- Prevent stale refill races: an in-flight Vault read may populate only the exact immutable reference it requested;
  invalidation tracks a local generation/tombstone so a read that began before invalidation cannot reinsert that
  same retired reference afterward.
- Do not use Redis or another distributed cache for plaintext endpoint keys.

### Rotation and fallback semantics

- Setting/replacing a slot does not activate it. Activation is a separate explicit command.
- Activating a slot may optionally retain the previously active slot as fallback until a supplied UTC expiry.
  Omitting the expiry performs an immediate cutover with no fallback.
- The fallback window has a configurable maximum (`Endpoints:MaxCredentialFallbackWindow`, proposed default one
  hour, hard maximum 24 hours). The application rejects past or over-limit expiry values.
- Routing always sends the active key first. It tries the fallback key at most once, without backoff, only while
  the persisted window is active and only after an unambiguous credential rejection received before any response
  is committed.
- Default credential rejection is HTTP `401`. A `403` is eligible only when a provider-specific classifier parses
  a structured error code that is explicitly allow-listed as invalid credentials. Generic `403`, connection
  failures, timeouts, `429`, and `5xx` never trigger key fallback.
- The fallback attempt reuses the already-buffered request bytes. This is safe only because the classified first
  response proves authentication failed before the provider accepted the operation. No fallback occurs after SSE,
  binary, or ordinary response content has begun relaying to the caller.
- Credential fallback is separate from endpoint transport retry/failover: it does not alternate keys repeatedly,
  consume a round-robin slot, mark the endpoint unhealthy, or silently promote the fallback slot.
- If fallback succeeds, the request is recorded once using the same endpoint and emits dedicated fallback telemetry.
  If fallback also fails, return the original active-key authentication response and record both attempt outcomes.
- Realtime WebSocket fallback is allowed only when .NET exposes an unambiguous `401` handshake response before the
  client WebSocket is accepted. Otherwise Realtime fails normally; it must not guess from a generic socket error.
- Expiry, disabling fallback, deleting a slot, and active-slot changes are based on database state shared by all
  replicas. Requests already in flight at the commit boundary may complete under the prior state; subsequent
  endpoint resolution observes the new state.

### Configuration API

Proposed routes (all existing admin authorization and antiforgery requirements remain):

| Method | Route | Purpose |
|---|---|---|
| `PUT` | `/api/v1/config/endpoints/{id}/secrets/{slot}` | Set/replace Slot `1` or `2`; writes a new immutable Vault value but does not activate it |
| `DELETE` | `/api/v1/config/endpoints/{id}/secrets/{slot}` | Remove an inactive, non-fallback slot; Slot 1 cannot be removed |
| `PUT` | `/api/v1/config/endpoints/{id}/credential-activation` | Atomically choose `activeSlot` and optional `fallbackExpiresAt` |
| `DELETE` | `/api/v1/config/endpoints/{id}/credential-fallback` | End the fallback window immediately without changing the active slot |

`EndpointResponse` exposes `hasPrimarySecret`, `hasSecondarySecret`, `activeCredentialSlot`,
`fallbackCredentialSlot`, and `fallbackExpiresAt`; it never exposes either plaintext value. The existing
`PUT /endpoints/{id}/secret` route remains as a deprecated compatibility alias for setting Slot 1 during one
release, then is removed deliberately in a later versioned change.

## Rotation workflow

1. Create/enable the standby credential at the external provider.
2. Store it in the inactive AI Proxy slot.
3. Activate that slot with a short fallback expiry.
4. Observe fallback-attempt telemetry. Successful fallback indicates the new provider credential is not accepted
   consistently and requires investigation; AI Proxy does not auto-promote or auto-revert either slot.
5. Once the new slot succeeds across replicas, end fallback and revoke the old key at the provider.
6. Keep or replace the inactive slot for the next rotation. Providers supporting only one key use Slot 1 and an
   immediate cutover, retaining today's bounded interruption risk.

## Phase 1 — Architecture specification

- [x] Update architecture.md domain/schema, Secrets Management, routing/retry, multi-replica, API, observability,
  security, and future-work sections with the controlling decisions above.
- [x] Update `02-domain-model.drawio`, `03-sequence-non-streaming-failover.drawio`, and affected exported images.
- [x] Resolve the current statement that the proxy is stateless across requests: credential caches are
  replica-local, while immutable references plus shared endpoint state preserve correctness.
- [x] Confirm the exact structured provider error codes eligible for `403` fallback; default to none when evidence
  is unavailable.
- [x] Confirm Vault retirement grace period and cleanup ownership before implementation.

## Phase 2 — Domain model

- [x] Add `EndpointCredentialSlot` and the dual-slot/fallback fields and methods to `Endpoint`.
- [x] Add methods equivalent to `SetSecretReference`, `ActivateCredential`, `DisableCredentialFallback`, and
  `RemoveSecondarySecret`, enforcing all slot/auth-mode/expiry invariants inside the aggregate.
- [x] Add Domain tests named `MethodName_Condition_ExpectedResult()` for migrated defaults, optional Slot 2,
  activation, bounded fallback, expiry, removal guards, and managed-identity rejection.

## Phase 3 — Persistence migration

- [x] Map the new endpoint fields, UTC expiry, slot conversions, and database check constraints.
- [x] Migrate existing `secret_ref` values to `primary_secret_ref` with Primary active; preserve every current
  endpoint id and Vault reference.
- [x] Add optimistic concurrency for endpoint credential-state updates so simultaneous rotations cannot silently
  overwrite each other.
- [ ] Add PostgreSQL integration tests for migration/backward compatibility, constraints, and concurrent activation.

## Phase 4 — Vault version lifecycle

- [x] Change slot writes to create immutable versioned secret references.
- [x] Implement safe retirement of unreferenced Vault versions after the agreed grace period, with retryable cleanup
  and telemetry; never delete a reference still active or eligible as fallback.
- [x] Make failed Vault-write/database-update sequences observable and cleanup-safe without logging secret values.
- [ ] Add Vault integration tests covering write-before-pointer-switch, failed database update, retirement, and
  plaintext redaction.

## Phase 5 — Multi-replica invalidation

- [x] Add a PostgreSQL notification publisher and hosted listener through Application abstractions implemented in
  Infrastructure; payloads contain identifiers/references only.
- [x] Invalidate credential cache entries on every replica after committed slot mutation; no endpoint-configuration cache exists today.
- [x] Add generation/tombstone protection against an in-flight stale read repopulating a retired cache entry.
- [x] Keep TTL expiry as missed-notification fallback and expose notification/reconnection/failure metrics.
- [ ] Add a two-instance integration test proving rotation on replica A causes replica B to use the new reference
  without waiting for TTL, plus tests for listener reconnect and missed-notification recovery.

## Phase 6 — Application commands and API

- [x] Add slot-aware set/remove, activate, and disable-fallback commands, validators, handlers, DTOs, and routes.
- [x] Keep the legacy Slot 1 route as a documented compatibility alias and add deprecation metadata to OpenAPI.
- [x] Ensure a successful mutation returns only metadata; plaintext endpoint credentials remain write-only.
- [ ] Add Application and API tests for valid workflows, absent Slot 2, active/fallback deletion conflicts,
  expiry limits, managed-identity conflicts, optimistic concurrency, authorization, and secret non-disclosure.

## Phase 7 — HTTP credential fallback

- [ ] Introduce a provider-aware structured credential-rejection classifier; do not use substring matching.
- [x] Extend credential resolution to return active and currently eligible fallback candidates without loading
  unused slots unnecessarily.
- [x] Add exactly one pre-commit fallback attempt to buffered HTTP forwarding while preserving existing `429`/`5xx`
  retry, endpoint failover, affinity, usage extraction, and first-failure replay semantics.
- [x] Ensure credential rejection does not mark endpoint connectivity unhealthy or consume round-robin state.
- [ ] Add tests for active success, fallback success, expired/disabled fallback, generic `403`, `429`, `5xx`, timeout,
  streaming pre-commit behavior, binary requests, stateful Responses calls, and both-key rejection.

## Phase 8 — Realtime credential fallback

- [x] Verify the target .NET `ClientWebSocket` API exposes handshake status reliably on all supported platforms.
- [ ] Retry once with fallback only for an explicit eligible handshake response before accepting the client socket.
- [ ] Add tests proving generic WebSocket failures never trigger fallback and successful fallback establishes only
  one client-facing WebSocket.
- [x] If reliable status classification is unavailable, document Realtime as immediate-cutover-only and leave its
  fallback unimplemented rather than weakening the rejection rules.

## Phase 9 — Observability and security

- [ ] Add counters for fallback attempted/succeeded/failed/expired and notification publish/receive/reconnect/failure,
  tagged by provider and endpoint id/name but never secret reference or key material.
- [ ] Add traces for active/fallback slot choice using slot labels only; redact authorization headers and provider
  error bodies under existing rules.
- [ ] Add alerts/runbook guidance: any successful fallback is actionable rotation drift, not normal steady state.
- [ ] Security-review revocation behavior, fallback-window limits, Vault policies for versioned paths, race handling,
  and log/telemetry redaction.

## Phase 10 — Frontend

- [x] Show Slot 1/Slot 2 configured state, active slot, and fallback expiry without revealing values or references.
- [x] Add separate set/replace actions, an explicit activation dialog with bounded expiry, immediate fallback stop,
  and guarded inactive Slot 2 deletion.
- [x] Present single-key providers as a supported Slot 1-only workflow, not an error state.
- [x] Add frontend tests for rotation workflow, validation, expired fallback display, and secret non-disclosure.

## Phase 11 — Validation and rollout

- [x] Run affected Domain, Application, Infrastructure, API, architecture, and frontend tests, then the complete
  backend suite plus frontend test/build checks.
- [ ] Live-test OpenAI-compatible and Anthropic endpoints with two provider keys: active success, bounded fallback,
  explicit cutover, fallback disable, and old-key revocation.
- [ ] Run at least two AI Proxy replicas against shared PostgreSQL/Vault and prove cache convergence without TTL wait.
- [x] Verify a Slot 1-only backend retains current behavior.
- [x] Update README, architecture diagrams, API documentation, operator rotation runbook, and this task file only
  after implementation and validation are complete.
