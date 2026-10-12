# Iteration 12 — Mitigate Architecture Review Risks

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). Iteration 11 included a
review of the architecture document against the actual implementation (commit `d900993`) that corrected several
documentation-only drifts and flagged four concrete risks where documented intent and running code disagree.
This iteration mitigates each of those four risks with actual code changes, not just further documentation edits.

## Background — the four risks, with exact anchors

1. **Session affinity TTL mismatch** (architecture.md §17): the doc described an expiry "mirroring OpenAI's
   default 30-day response retention (configurable)". `ProxyRoutingService`'s `sessionAffinityTtl` constructor
   parameter defaults to `TimeSpan.FromMinutes(30)`, and `Program.cs` registers `ProxyRoutingService` via plain
   `AddScoped<ProxyRoutingService>()` — no caller ever supplies a non-default value, and there is no
   configuration key anywhere. Effective behavior today: always 30 minutes, never configurable.
2. **Endpoint health tracking is inert** (architecture.md §9): `IEndpointHealthTracker.MarkHealthy`/
   `MarkUnhealthy` are called from `ProxyRoutingService`/`RealtimeProxyService`, but `GetHealth` has zero call
   sites anywhere. Separately, `EndpointSelector` filters on the *persisted* `ModelEndpoint.Health` column, but
   that entity's own `MarkUnhealthy()`/`MarkHealthy()` domain methods are only ever invoked from unit tests — no
   production code path updates it. Net effect: the Models/Endpoints "Health" badge is decorative, and there is
   no cross-request circuit breaker — a request immediately after a same-tier endpoint failed will still try
   that same endpoint again from scratch, every time.
3. **No dependency hygiene tooling** (architecture.md §13): no `.github/dependabot.yml`, no CI workflows
   (`.github/workflows/` doesn't exist), no scheduled `dotnet outdated`/`npm audit` job.
4. **Postgres least privilege not implemented** (architecture.md §13): `AIProxyPlusDbContext` — one connection
   string, one role — runs both the startup `Database.MigrateAsync()` (DDL) and every runtime query. There is no
   narrower, DDL-less role for the running app.

## Proposed design

### 1. Session affinity TTL — make it configurable, keep today's default

- Bind a new `Routing:SessionAffinityTtl` config value (a `TimeSpan`-parseable string, e.g. `"00:30:00"`) in
  `Program.cs` and pass it explicitly into `ProxyRoutingService`'s `sessionAffinityTtl` parameter instead of
  relying on the constructor default.
- **Keep the shipped default at 30 minutes**, not 30 days: `session_affinity` rows reference a specific
  `ModelEndpoint` assignment, which can be reconfigured or deleted independently of an OpenAI-side response that
  might still be resolvable for up to 30 days — a 30-day-old affinity row is far more likely to point at a
  stale/removed assignment than a 30-minute-old one. Rather than guessing at a "correct" universal value, this
  iteration closes the *configurability* gap and documents the trade-off explicitly, leaving the specific value
  to operators who understand their own deployment's reconfiguration frequency (the doc's own §17 note — "needs
  revisiting once real usage patterns are known" — still applies to picking a *different* default, just no
  longer to whether it's configurable at all).
- An unrecognized/expired affinity key already falls back to normal (non-sticky) routing rather than erroring
  (§9) — that existing fallback semantics is unchanged; this work only makes the expiry window itself tunable.

### 2. Cross-request passive circuit breaker via the existing in-memory tracker

- `IEndpointHealthTracker`/`EndpointHealthTracker` gains a bounded **cooldown window**: `MarkUnhealthy` records
  *when* the mark happened, and `GetHealth` reports `Unhealthy` only while `now - markedAt < cooldown`, then
  automatically reports `Unknown` again (self-healing) without waiting for an explicit `MarkHealthy` call. A
  hardcoded "unhealthy forever until someone calls MarkHealthy" would be a regression, not a fix — nothing
  currently calls `MarkHealthy` from a background process, so a permanent mark would eventually exclude every
  endpoint that ever failed once.
- `ProxyRoutingService.RouteAsync` (and `RealtimeProxyService`'s handshake path) seed the request's
  `excludedEndpointIds` with any endpoint the tracker currently reports as `Unhealthy` (cooldown still active),
  *before* the existing same-request retry loop begins — this is additive to, not a replacement for, the
  existing request-scoped exclusion behavior. A brand-new incoming request therefore skips an endpoint that
  failed moments ago instead of retrying it from a cold start every single time, while still eventually retrying
  it once the cooldown elapses (passive health recovery, no active health-check polling needed).
- The **persisted** `ModelEndpoint.Health` column and its domain mutators are explicitly **out of scope** for
  automatic updates in this iteration — wiring a real background health-check job that updates it is a larger
  feature, not a risk-mitigation fix. Architecture.md is updated to state plainly that persisted health remains
  an administrator/future-health-check-owned display field, while the in-memory tracker is what actually now
  provides cross-request circuit breaking. This keeps the two mechanisms' responsibilities distinct instead of
  quietly merging them.
- Default cooldown: a short, conservative value (e.g. 30 seconds) — long enough to avoid hammering a
  just-failed endpoint on every subsequent request, short enough that a transient blip doesn't take an endpoint
  out of rotation for a meaningfully long time. Made configurable alongside the session affinity TTL for the
  same reason (§1 above).

### 3. Dependency hygiene — add Dependabot

- Add `.github/dependabot.yml` covering both ecosystems actually present: `nuget` (root — one `AIProxyPlus.sln`
  covers every `.csproj`) and `npm` (`/src/AIProxyPlus.Web`). Weekly schedule, a sane open-PR cap per ecosystem.
  This is the lowest-effort option already named in the architecture doc's own risk callout.

### 4. Postgres least privilege — separate migration and runtime connections

- Add an optional `ConnectionStrings:aiproxyplusdb-migrations` config key. When set, the startup
  `Database.MigrateAsync()` step uses a dedicated `AIProxyPlusDbContext` built from that connection string; the
  regular DI-registered `AIProxyPlusDbContext` (used by every repository/runtime query) keeps using
  `ConnectionStrings:aiproxyplusdb` unchanged. When the migrations key is **not** set, behavior is byte-for-byte
  identical to today (same single connection string for both) — this is additive, not a breaking change to the
  existing single-role local Aspire/docker-compose setup.
- Document (README + architecture.md, not a forced docker-compose.yml change) a runbook for creating the two
  Postgres roles in a real deployment: a privileged/owner role for migrations, and a narrower application role
  granted only `SELECT`/`INSERT`/`UPDATE`/`DELETE` on the app's own tables (no `CREATE`/`ALTER`/`DROP`, no
  superuser) — pointing `ConnectionStrings:aiproxyplusdb` at the narrower role and
  `ConnectionStrings:aiproxyplusdb-migrations` at the privileged one. Local Aspire/docker-compose dev intentionally
  keeps the single-role simplification (called out explicitly, the same way dev-mode Vault already is) since
  forcing two roles into the disposable local dev topology isn't worth the added complexity there.

## Phase 1 — Architecture specification

- [x] Update architecture.md §17 to replace the session-affinity-TTL "Risk" callout with the resolved design:
  configurable via `Routing:SessionAffinityTtl`, default unchanged at 30 minutes, with the rationale for not
  defaulting to 30 days.
- [x] Update architecture.md §9 to replace the endpoint-health "Risk" callout with the resolved design: describe
  the cooldown-based in-memory circuit breaker precisely, and state explicitly that persisted `ModelEndpoint.Health`
  remains a separate, non-automatic, admin/future-health-check-owned field.
- [x] Update architecture.md §13 to remove the dependency-hygiene "Risk" callout once `.github/dependabot.yml`
  exists, and replace the Postgres least-privilege "Risk" callout with the migration-vs-runtime connection
  string split and a pointer to the new role-creation runbook.
- [x] Finalize the exact configuration key names (`Routing:SessionAffinityTtl`, `Routing:UnhealthyCooldown`,
  `ConnectionStrings:aiproxyplusdb-migrations`) here first so later phases don't invent divergent names.

## Phase 2 — Session affinity TTL configuration

- [x] Bind `Routing:SessionAffinityTtl` in `Program.cs` and pass it explicitly to `ProxyRoutingService`.
- [x] Add a test proving a configured non-default TTL is actually threaded through (e.g. an `AIProxyPlus.Api.Tests`
  case constructing the test host with a short configured TTL and observing the affinity row's `ExpiresAt`).
- [x] Update `README.md`'s configuration reference if it documents other `Routing:*`/connection-string keys.

## Phase 3 — Cross-request passive circuit breaker

- [x] Extend `IEndpointHealthTracker`/`EndpointHealthTracker` with a cooldown window (recording mark time,
  auto-expiring back to `Unknown`); bind `Routing:UnhealthyCooldown` with a default of 30 seconds.
- [x] Seed `ProxyRoutingService.RouteAsync`'s `excludedEndpointIds` from the tracker's currently-unhealthy
  endpoints before the existing retry loop begins; apply the same treatment in `RealtimeProxyService`'s
  handshake-failover path.
- [x] Add unit tests for the tracker's cooldown expiry (unhealthy immediately after marking, healthy/unknown
  again after the cooldown elapses) and an `AIProxyPlus.Api.Tests` case proving a *separate, subsequent* request
  skips a same-tier endpoint that failed on a prior request within the cooldown window, then succeeds against it
  again once the cooldown has elapsed.
- [x] Do **not** wire automatic updates to persisted `ModelEndpoint.Health` in this iteration — leave its
  existing (test-only-invoked) mutators as-is; this is intentionally scoped to the in-memory tracker only.

## Phase 4 — Dependency hygiene tooling

- [x] Add `.github/dependabot.yml` with `nuget` (root directory) and `npm` (`/src/AIProxyPlus.Web`) update
  configurations, weekly schedule, reasonable open-PR limits.
- [x] Validate the file is well-formed YAML matching Dependabot's schema (structural check only — actual PR
  generation can't be validated locally).

## Phase 5 — Postgres least privilege

- [x] Add optional `ConnectionStrings:aiproxyplusdb-migrations` support in `Program.cs`: use it for the startup
  `Database.MigrateAsync()` step when present, falling back to `ConnectionStrings:aiproxyplusdb` when absent so
  today's single-connection-string setups (Aspire local dev, docker-compose) are unaffected.
- [x] Write the Postgres role-creation runbook (README and/or architecture.md): owner/migration role vs.
  `SELECT`/`INSERT`/`UPDATE`/`DELETE`-only application role, and which connection string key each maps to.
- [x] Add an integration test proving migrations still apply correctly when a distinct migrations connection
  string is supplied (can reuse the existing Testcontainers Postgres fixture with two connection strings to the
  same container/database, one per role, if feasible — otherwise a config-binding-level test is acceptable).

## Phase 6 — Validation

- [x] Run the complete backend test suite (`dotnet test AIProxyPlus.sln`) — 358/358 passing. (One new test,
  `EndpointHealthCircuitBreakerTests.FailedEndpoint_IsSkippedOnSubsequentRequest_ThenEligibleAgainAfterCooldown`,
  was flaky under full-suite parallel load because its two endpoints shared the same priority tier, making
  round-robin's "which one goes first" effectively random per run — fixed by giving them different priorities
  (1 and 2) so failover order is deterministic; re-ran 3x plus the full suite to confirm stability.)
- [x] Live-validate the passive circuit breaker: force a same-tier endpoint to fail (e.g. via WireMock or a
  temporarily-wrong credential), confirm a subsequent, separate request skips it during the cooldown window and
  succeeds against it again after the cooldown elapses. — Validated by
  `EndpointHealthCircuitBreakerTests`, which hosts the real `Program.cs` composition (via `ApiTestFactory`) with
  a configured 300ms cooldown, two real WireMock upstreams, and asserts on actual request logs across three
  separate, independent HTTP calls (fail → skip-during-cooldown → eligible-again-after-cooldown).
- [x] Live-validate session affinity TTL configurability: set a short `Routing:SessionAffinityTtl`, confirm the
  `session_affinity` row's `ExpiresAt` reflects it and that expiry correctly falls back to non-sticky routing. —
  Validated by `ResponsesApiAffinityTests.ConfiguredSessionAffinityTtl_IsAppliedToTheAffinityRow`, which hosts
  the real `Program.cs` composition with a configured short TTL and asserts the persisted affinity row's
  `ExpiresAt` reflects it; the pre-existing expiry-fallback tests in the same file cover the non-sticky fallback
  path.
- [x] Confirm the frontend/README/architecture docs are consistent with the final config key names actually
  shipped — `Routing:SessionAffinityTtl`, `Routing:UnhealthyCooldown`, and `ConnectionStrings:aiproxyplusdb-migrations`
  all appear consistently across `README.md` and `specs/architecture/architecture.md`.
