# Iteration 13 — Dual Rotatable Consumer API Keys & Per-Consumer Model Allow-Lists

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration adds two
independent, user-requested capabilities to `Consumer` — it does not touch the Iteration 12 risk-mitigation
work. Spec-first, per repo convention: no implementation until Phase 1 is agreed.

## Background

Today (architecture.md §8/§13, `src/AiProxy.Domain/Entities/Consumer.cs`):

1. An API-key `Consumer` has exactly one `ApiKeyHash`. `RotateConsumerKeyHandler` overwrites it in place via
   `Consumer.RotateKey(string)`. Rotating a key is therefore a **breaking** operation for that consumer: the
   old plaintext key stops working the instant the new hash is persisted, and every caller using the old key
   must be updated with the new one atomically, or requests start failing with 401 mid-rotation.
2. `Consumer` has no notion of which `Model`s it may call — `ConsumerAuthEndpointFilter` only proves *identity*
   (architecture.md §13); `ProxyRoutingService.RouteAsync` resolves a `Model` purely from the request body's
   `model` field and never consults the caller's identity again. Any enabled consumer can invoke any model.

The user asked for two things, explicitly modeled on Azure's storage-account-key pattern:

- Two independently-rotatable API keys per consumer ("Key1"/"Key2"), either of which authenticates the caller,
  so rotating one key leaves the other valid — zero-downtime credential rotation (rotate the standby key,
  migrate callers, then rotate the now-standby former-active key on the next cycle).
- A per-consumer allow-list of which `Model`s that consumer may invoke.

## Proposed design

### 1. Dual API keys ("Key1"/"Key2", Azure-style)

- Add a second nullable hash column to `Consumer`: `ApiKeyHash2`, mirroring `ApiKeyHash`'s existing shape
  (nullable, `HasMaxLength(500)`, unique filtered index `WHERE "ApiKeyHash2" IS NOT NULL`). Rename nothing —
  `ApiKeyHash` stays the "slot 1" / "Key1" column to avoid an unnecessary rename migration; `ApiKeyHash2` is
  "slot 2" / "Key2". Both are only ever set for API-key consumers, never OIDC ones.
- **`CreateConsumer` generates both keys upfront** (not just Key1, with Key2 added later) — this matches the
  Azure pattern the user asked for: a brand-new consumer already has two live, independent keys from creation,
  not one key plus an optional future second one. Both plaintext keys are returned once in the create response,
  the same way the single key is today.
- New domain type `ApiKeySlot { Primary, Secondary }` (maps to `ApiKeyHash`/`ApiKeyHash2`). `Consumer` gains
  `RotateKey(ApiKeySlot slot, string newApiKeyHash)` replacing the current single-arg `RotateKey(string)` —
  rotates only the hash in the specified slot; the other slot's hash is untouched and remains valid for
  authentication throughout. `Consumer.CreateApiKey` takes both `apiKeyHash1`/`apiKeyHash2` (both required at
  creation, per the point above).
- `IConsumerRepository.GetByApiKeyHashAsync` (and its EF implementation) matches `WHERE "ApiKeyHash" = @hash OR
  "ApiKeyHash2" = @hash` — either slot authenticates identically; `ConsumerAuthenticator` needs no changes
  beyond the repository query itself since it already just calls that method with the hash of the caller's key.
- `CK_consumers_authentication_identity` check constraint gains a clause preventing an OIDC consumer or a
  keyless row from ever having `ApiKeyHash2` set: `("ApiKeyHash2" IS NULL OR "ApiKeyHash" IS NOT NULL)`.
- API surface: `PUT /api/v1/config/consumers/{name}/rotate?slot=1` (or `2`) — `slot` becomes a **required** query
  parameter (400 if missing/invalid) rather than defaulting silently, since guessing which of two live keys to
  invalidate is exactly the kind of mistake this feature exists to prevent. Response shape unchanged
  (`{ consumer, apiKey }`) — only the rotated slot's new plaintext key is returned.
- `ConsumerResponse` (GET/list) never exposes hashes (unchanged), but gains enough information for the frontend
  to render "Key1"/"Key2" rotate actions independently (e.g. booleans or a slot-presence indicator) — exact
  shape decided in Phase 1 alongside the DTO code.
- Existing consumers created before this migration only have `ApiKeyHash` populated; the migration leaves
  `ApiKeyHash2` `NULL` for them (no forced-regeneration backfill) — they simply have a live Key1 and an
  unconfigured Key2 slot until an admin first rotates slot 2. This is a deliberate, additive migration; it does
  not break any existing consumer's current key.

### 2. Per-consumer model allow-list

- New relation, not a fat column: `Consumer` gains a `_allowedModelIds` collection (mirrors the existing
  `Model._capabilities`/`ModelCapabilityAssignment` join-entity pattern already in the codebase) persisted via a
  new `consumer_allowed_models(ConsumerId, ModelId)` join table (composite PK, FKs to both `consumers` and
  `models`, `ON DELETE CASCADE` from both sides so deleting either side cleans up the join row).
- **Default = unrestricted**, for backward compatibility with every existing consumer and test: `Consumer.
  HasUnrestrictedModelAccess => _allowedModelIds.Count == 0`. An allow-list only starts restricting once an
  admin adds at least one entry. Removing the last entry returns the consumer to unrestricted access. A separate
  deny-all state is intentionally out of scope.
- `Consumer` domain methods: `AllowModel(Guid modelId)`, `RevokeModelAccess(Guid modelId)` (both idempotent —
  adding an already-present id or removing an absent one is a no-op, not an error, matching `ModelCapability`'s
  existing idempotent-add precedent), `CanUseModel(Guid modelId) => HasUnrestrictedModelAccess ||
  _allowedModelIds.Contains(modelId)`.
- **Enforcement point**: `ConsumerAuthEndpointFilter` already loads the full `Consumer` entity to authenticate
  the caller — it stashes `consumer.Id`/`consumer.Name` into `HttpContext.Items` today. Extend it to also stash
  the resolved allow-list (`null` when `HasUnrestrictedModelAccess`, else the `IReadOnlySet<Guid>`) under a new
  `ConsumerAuthEndpointFilter.AllowedModelIdsItemKey`. This avoids a second DB round-trip inside
  `ProxyRoutingService`/`RealtimeProxyService` to re-fetch the same `Consumer` row that authentication just
  loaded, and avoids adding `IConsumerRepository` as a new routing-layer dependency.
- `Program.cs`'s route handlers read that item the same way they already read `ConsumerIdItemKey`/
  `ConsumerNameItemKey`, and pass it into `ProxyRoutingService.RouteAsync`/`RealtimeProxyService`'s equivalent
  entry point as a new optional parameter (e.g. `IReadOnlySet<Guid>? allowedModelIds = null`).
- Inside `RouteAsync`, immediately after `Model? model` is resolved (the earliest point the model's `Guid` id is
  known) and before any endpoint/affinity/round-robin work begins: if `allowedModelIds is not null &&
  !allowedModelIds.Contains(model.Id)`, return a new failure reason without touching endpoints, affinity, or
  usage capture — this must be a pure authorization check, not something that consumes a round-robin slot or
  touches the health tracker.
- New `RoutingFailureReason.ModelAccessDenied`, mapped to **403 Forbidden** in `Program.cs`'s existing
  failure-reason → status-code switch (alongside the current `ModelNotFound` → 404, `NoHealthyEndpoint`/
  `StickyEndpointUnavailable` → 503). Deliberately distinct from 404: the model exists, the caller is
  authenticated, they're just not permitted to use *this* model — a 404 would leak nothing extra here since the
  model name came from the caller's own request body, so 403 is unambiguously the more correct and more
  informative status.
- Application-layer commands/queries for managing the list (`AllowConsumerModelHandler`/
  `RevokeConsumerModelHandler` or similar — exact naming decided in Phase 1) plus new config API routes, e.g.
  `PUT /api/v1/config/consumers/{name}/allowed-models/{modelName}` and
  `DELETE /api/v1/config/consumers/{name}/allowed-models/{modelName}`, and the existing `ConsumerResponse`
  gains the current allow-list (model ids, or ids+names via a join at the query side) so the frontend can render
  it without a second round-trip per consumer.

## Phase 1 — Architecture specification

- [x] Update architecture.md's domain model section (and the `02-domain-model.drawio` diagram) to show
  `Consumer.ApiKeyHash2` and the new `consumer_allowed_models` relation.
- [x] Update architecture.md §8/§13 (or wherever consumer authentication is currently described) to document
  the dual-key rotation model and its zero-downtime rationale.
- [x] Update architecture.md §9/§10 (routing) to document the new authorization check's exact position in
  `RouteAsync` (after model resolution, before endpoint selection) and its 403 semantics.
- [x] Finalize exact API route shapes, DTO field names (`ConsumerResponse` additions), and the
  `RotateConsumerKeyCommand`/handler signature change (`slot` parameter) here first, before any code changes.
- [x] Finalize the `consumer_allowed_models` table/migration shape and the exact `Consumer` domain method names.

## Phase 2 — Domain: dual API keys

- [x] Add `ApiKeySlot` enum (`Primary`, `Secondary`) under `AiProxy.Domain.Enums`.
- [x] Add `Consumer.ApiKeyHash2`; update `CreateApiKey` to require both hashes; replace `RotateKey(string)` with
  `RotateKey(ApiKeySlot slot, string newApiKeyHash)`.
- [x] Update `AiProxy.Domain.Tests/Entities/ConsumerTests.cs` for the new creation/rotation signatures (create
  with both hashes; rotate each slot independently; verify the untouched slot's hash is unchanged after
  rotating the other; validation errors for empty hashes in either slot; OIDC consumers still reject any key
  operation).

## Phase 3 — Persistence: dual API keys

- [x] `ConsumerConfiguration`: map `ApiKeyHash2` (nullable, `HasMaxLength(500)`, unique filtered index), extend
  the `CK_consumers_authentication_identity` check constraint.
- [x] EF Core migration: add the `ApiKeyHash2` column (nullable — no backfill, existing consumers keep a single
  live key until first rotated) and the updated check constraint.
- [x] `ConsumerRepository.GetByApiKeyHashAsync` (and any other query touching `ApiKeyHash`) matches either slot.
- [x] Update `ConsumerRepositoryTests` for: seeding a consumer with both hashes, looking a consumer up by either
  slot's hash, and the updated check constraint (a row with `ApiKeyHash2` set but `ApiKeyHash` null must be
  rejected by Postgres, not just by domain code).

## Phase 4 — Application & API: dual API keys

- [x] Update `CreateConsumerHandler`/`CreateConsumerCommand` to generate and return both plaintext keys.
- [x] Update `RotateConsumerKeyHandler`/`RotateConsumerKeyCommand` to take a required `ApiKeySlot`/`slot` value;
  reject OIDC consumers and invalid slot values with the existing exception types
  (`ConflictException`/validation).
- [x] Update the `PUT /consumers/{name}/rotate` route to require `?slot=1|2` (400 on missing/invalid), and the
  `POST /consumers` response to include both plaintext keys.
- [x] Update `ConsumerResponse` with whatever slot-presence indicator Phase 1 settled on.
- [ ] Update `AiProxy.Application.Tests` (`CreateConsumerTests`, `RotateConsumerKeyTests`) and
  `AiProxy.Api.Tests/Config/ConsumerConfigTests.cs` for the new command/response shapes, including a test proving
  a consumer authenticates successfully with the OLD key in the untouched slot immediately after rotating the
  OTHER slot (the actual zero-downtime guarantee, exercised end-to-end through `ConsumerAuthenticator`).

## Phase 5 — Frontend: dual API keys

- [x] Update the Consumers page to show both keys' rotate actions independently (two buttons/rows, "Key 1" /
  "Key 2"), and the create-consumer flow to display both freshly-generated plaintext keys once.
- [x] Update/add frontend tests for the new two-key UI.

## Phase 6 — Domain & persistence: per-consumer model allow-list

- [x] Add `Consumer._allowedModelIds`/`AllowedModelIds`, `HasUnrestrictedModelAccess`, `AllowModel`,
  `RevokeModelAccess`, `CanUseModel` (idempotent add/remove, per the design above).
- [x] Add the `consumer_allowed_models` join table (`ConsumerConfiguration`/`ModelConfiguration` mapping,
  composite PK, cascading deletes from both `consumers` and `models`) and the EF Core migration.
- [x] Update `ConsumerTests` for the new allow-list methods (idempotency, `HasUnrestrictedModelAccess` default,
  `CanUseModel` with an empty vs. populated list).
- [ ] Add persistence tests proving the join table round-trips correctly and that deleting a `Model` that a
  consumer is restricted to removes the join row without deleting the `Consumer` (and vice versa).

## Phase 7 — Application & API: per-consumer model allow-list

- [x] Add `AllowConsumerModelHandler`/`RevokeConsumerModelHandler` (or the names Phase 1 settles on) plus their
  commands/validators, following the existing `RotateConsumerKeyHandler`-style pattern (`NotFoundException` for
  an unknown consumer or model name/id, idempotent success otherwise).
- [x] Add the `PUT`/`DELETE /consumers/{name}/allowed-models/{modelName}` routes; extend `ConsumerResponse`
  with the current allow-list.
- [x] Add `AiProxy.Application.Tests` and `AiProxy.Api.Tests` coverage for the new handlers/routes, including the
  transition back to unrestricted access when the last allowed model is removed.

## Phase 8 — Routing enforcement

- [x] Extend `ConsumerAuthEndpointFilter` to stash the resolved allow-list (or `null` when unrestricted) into
  `HttpContext.Items` alongside the existing consumer id/name items.
- [x] Thread the allow-list through `Program.cs`'s ingress route handlers into
  `ProxyRoutingService.RouteAsync`/`RealtimeProxyService`'s equivalent entry point as a new optional parameter.
- [x] Add `RoutingFailureReason.ModelAccessDenied`; enforce it in `RouteAsync` immediately after model
  resolution, before any endpoint/affinity/round-robin/usage work; map it to 403 in `Program.cs`.
- [ ] Add `AiProxy.Api.Tests` coverage: a restricted consumer is denied (403) for a disallowed model but
  succeeds for an allowed one; an unrestricted consumer (the default/today's behavior) is unaffected; a denied
  request does not touch the round-robin counter, session affinity, or the endpoint health tracker, and does
  not produce a `UsageRecord` (it never reaches an endpoint).

## Phase 9 — Frontend: per-consumer model allow-list

- [x] Update the Consumers (and/or Model Deployments) page to let an admin add/remove allowed models per
  consumer, and clearly show "unrestricted" vs. a specific allow-list.
- [x] Update/add frontend tests for the new allow-list UI.

## Phase 10 — Validation

- [x] Run the complete backend test suite (`dotnet test AiProxy.sln`).
- [x] Run the frontend test suite and build (`npm test`, `npm run build` from `src/AiProxy.Web`).
- [ ] Live-validate zero-downtime key rotation: create a consumer, call the proxy successfully with Key1,
  rotate Key2, confirm Key1 still authenticates; rotate Key1, confirm the (still-unrotated-since-last-step) Key2
  authenticates and the old Key1 no longer does.
- [ ] Live-validate the model allow-list: restrict a consumer to one model, confirm calls to that model succeed
  and calls to any other model return 403, then remove the restriction and confirm access returns to
  unrestricted.
- [x] Confirm README/architecture.md/diagram consistency with whatever was actually shipped.

## Follow-up — Revoked consumer cleanup

- [x] Add guarded permanent deletion for already-revoked consumers while preserving denormalized usage history.
- [x] Enable the Consumers page delete action for revoked rows with an explicit permanent-deletion confirmation.
- [x] Add application, API, and frontend coverage for the revoked-only deletion lifecycle.

## Follow-up — Explicit consumer lifecycle

- [x] Model reversible disable/enable separately from irreversible revocation in the Consumer aggregate.
- [x] Add explicit disable/enable API commands and preserve authentication denial while disabled.
- [x] Use Pause/Play for disable/enable, a distinct revoke action, and Trash only for permanent deletion.
- [x] Add domain, application, API, and frontend coverage for all lifecycle transitions.
