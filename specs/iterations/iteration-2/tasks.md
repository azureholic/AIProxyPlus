# Iteration 2 — Tasks

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md), which is updated in
Phase 1/2/3 to describe these changes as they land (not left as a stale Iteration-1-only description).

## Motivation (from planning discussion)

- Sticky sessions should not need an explicit per-deployment config flag. The Responses API already proved a
  better pattern: affinity that's *inherent to the protocol* (`previous_response_id`), not an admin toggle. The
  current code actually has a latent bug from this half-transition — `ProxyRoutingService` persists Responses
  API affinity unconditionally but only *looks it up* when `ModelDeployment.StickySessionEnabled` is true,
  so a deployment with the flag off silently breaks its own `previous_response_id` chaining.
- The only remaining case that genuinely needs connection-level stickiness is the Realtime (voice) API, and
  that's inherent too — a WebSocket is a single long-lived connection to one backend for its lifetime; there is
  nothing to configure.
- Load balancing across endpoints of the same priority has never done anything but "first one in list order";
  round-robin across equal-priority endpoints is a real, currently-missing feature. Failover also currently
  triggers only on 5xx — 429 (rate limiting, the single most common reason to want failover in practice) is
  silently *not* treated as a failure today.

## Decisions locked in for this iteration

- Realtime/voice: **OpenAI Realtime API only** (`wss://.../v1/realtime?model=...`, `Authorization: Bearer` auth).
  Azure OpenAI Realtime and generic/self-hosted WebSocket backends are explicitly deferred.
- The generic `X-Session-Id`/body `session` sticky mechanism (for plain chat completions) is **removed
  entirely** — Responses API and Realtime each carry their own inherent affinity; nothing else needs it.
- Realtime failover applies **only to the WebSocket handshake** (picking a different endpoint if connecting to
  the backend fails before any client data has flowed) — never mid-session.
- Realtime usage/token capture is **deferred** to a later iteration; this iteration is connectivity + routing.
- `session_affinity` (table/entity/`ISessionAffinityStore`) is kept as-is, just narrowed in scope/docs to be
  "Responses API affinity" — no rename, to minimize churn for a concept that's still structurally identical
  (opaque key → endpoint, with expiry).

## Phase 1 — Priority + round-robin load balancing, 429 failover

- [x] `EndpointSelector` (Domain): rework non-sticky selection to group healthy/enabled candidates by
  `Priority`, pick the lowest surviving priority tier, and round-robin within that tier using a caller-supplied
  counter (keeps the selector a pure function — no I/O/state in Domain). Single-endpoint and
  single-priority-tier cases keep working unchanged; multi-endpoint-same-priority now rotates instead of
  always picking the same one. Also removed `ModelDeployment`'s priority-uniqueness invariant
  (`AddEndpoint`/`ChangeEndpointPriority` used to throw on a duplicate priority) — ties are now the whole
  point, not an error.
- [x] New `IEndpointRoundRobinCounter` port (Application) + in-memory `EndpointRoundRobinCounter` impl
  (Infrastructure, `ConcurrentDictionary<Guid,int>` + `Interlocked.Increment`, keyed by `ModelDeploymentId`) —
  same pattern as the existing `IEndpointHealthTracker`. `ProxyRoutingService` increments once per incoming
  request (not per retry) and passes the snapshot into `EndpointSelector` for that request's whole
  selection/failover loop.
- [x] `ProxyRoutingService`/`TryForwardWithRetriesAsync`: treat HTTP `429` the same as `>=500` — a retriable
  failure that marks the endpoint unhealthy (for a cooldown) and fails over to the next endpoint/tier.
- [x] Domain unit tests: round-robin distributes evenly across an equal-priority tier and is stable per
  counter value; priority still strictly orders across tiers; excluded/unhealthy endpoints are skipped
  correctly in both single- and multi-endpoint-per-priority cases.
- [x] Integration tests (`AiProxy.Api.Tests`): two same-priority endpoints split traffic across repeated
  calls (round-robin, not always-first); a 429 triggers failover to the next endpoint exactly like a 500 does.

## Phase 2 — Remove generic sticky-session config; Responses API affinity becomes unconditional

- [ ] Domain: remove `ModelDeployment.StickySessionEnabled` (property, constructor parameter,
  `SetStickySessionEnabled`/equivalent mutator).
- [ ] EF Core: migration dropping the `sticky_session_enabled` column from `model_deployments`.
- [ ] Application: `CreateModelDeploymentCommand`/`UpdateModelDeploymentCommand` (+ validators) drop
  `StickySessionEnabled`.
- [ ] Api: config API request/response bodies for model-deployments drop `stickySessionEnabled`.
- [ ] `ProxyRoutingService`: `ResolveSessionKey`/affinity lookup applies **only** to
  `OperationType.ResponsesApi` via `PreviousResponseId` — unconditionally, no deployment flag gate (fixes the
  latent bug described above). The generic body-`session`/`X-Session-Id` header lookup path is deleted, along
  with the now-dead parts of `RequestBodyFields`/`RequestBodyPeek` that only existed to support it.
- [ ] Tests: remove/rework `StickySessionTests` (the generic-session scenarios no longer apply once the
  mechanism is gone); `ResponsesApiAffinityTests` continues to pass unchanged since Responses API behavior is
  the same, just no longer gated by a flag — add a regression test proving that a deployment needs no special
  configuration for `previous_response_id` chaining to work.

## Phase 3 — Realtime (voice) API via WebSocket proxy

- [ ] Domain: add `OperationType.RealtimeVoice`.
- [ ] Infrastructure: `RealtimeProxyService` — accepts the inbound WebSocket
  (`HttpContext.WebSockets.AcceptWebSocketAsync`), resolves `model` from the **query string** (no body to peek
  on a WebSocket handshake), resolves the `ModelDeployment`, selects an endpoint via the same
  `EndpointSelector` (priority + round-robin — no affinity/stickiness needed, the connection itself is the
  affinity), resolves the credential via the existing `ICredentialProvider`, and opens an outbound
  `ClientWebSocket` to the backend (`Authorization: Bearer` header set via `ClientWebSocket.Options` before
  connecting). If the outbound connect fails, mark the endpoint unhealthy and try the next
  priority/round-robin candidate (bounded by the deployment's endpoint count) — never mid-session. Once
  connected, pump frames bidirectionally (two loops) until either side closes; propagate the close
  status/reason back to the other side.
- [ ] Api: `app.UseWebSockets()`; new ingress route `GET /openai/v1/realtime` behind the same
  `ConsumerAuthEndpointFilter` as other ingress routes (the handshake is a normal HTTP GET with headers before
  the protocol upgrade, so the existing `X-Api-Key` filter applies unchanged).
- [ ] Test infrastructure: a minimal in-process fake WebSocket backend (a second `WebApplication`/Kestrel test
  host with `UseWebSockets()` + an echo/scripted endpoint) since WireMock.Net doesn't support WebSocket —
  reused across the new realtime tests the same way `WireMockServer` is reused for HTTP fakes.
- [ ] Integration tests (`AiProxy.Api.Tests`): successful connect + bidirectional echo round-trip; unknown
  deployment / missing `model` query param rejected before any upgrade attempt; handshake failure on the
  priority-1 endpoint fails over to priority-2; missing/invalid `X-Api-Key` rejected with 401 before upgrade.

## Phase 4 — Documentation

- [ ] `architecture.md`: rewrite §6.2/§9 to describe the new load-balancing algorithm (priority tiers +
  round-robin + 429/5xx failover) and the narrowed, automatic-only affinity model (Responses API + Realtime,
  no config flag); add a new subsection for the Realtime API (ingress route, WebSocket proxy design, "why
  failover only applies to the handshake"); update §2 scope table, §7.1 schema table (drop
  `sticky_session_enabled`), §12 API surface summary, §16 technology choices if needed.
- [ ] New/updated diagrams if the high-level or sequence diagrams need it (at minimum, note the new ingress
  route on diagram 01 if it's still accurate enough not to need a full redraw).
- [ ] `README.md`: mention the realtime route and the removal of `stickySessionEnabled` from the config API
  example if relevant.
- [ ] `specs/iterations/iteration-1/tasks.md`: no change needed (historical record of Iteration 1, left as-is).
