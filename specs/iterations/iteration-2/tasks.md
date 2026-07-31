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

- [x] Domain: remove `ModelDeployment.StickySessionEnabled` (property, constructor parameter,
  `SetStickySessionEnabled`/equivalent mutator). Also removed the priority-uniqueness invariant while touching
  this file (folded into Phase 1 since it was the same `AddEndpoint`/`ChangeEndpointPriority` code path).
- [x] EF Core: migration (`RemoveStickySessionEnabled`) dropping the `sticky_session_enabled` column from
  `model_deployments`.
- [x] Application: `CreateModelDeploymentCommand`/`UpdateModelDeploymentCommand` (+ validators) drop
  `StickySessionEnabled`.
- [x] Api: config API request/response bodies for model-deployments drop `stickySessionEnabled`.
- [x] `ProxyRoutingService`: affinity lookup applies **only** to `OperationType.ResponsesApi` via
  `PreviousResponseId` — unconditionally, no deployment flag gate (fixes the latent bug described above). The
  generic body-`session`/`X-Session-Id` header lookup path (`ResolveSessionKey`) is deleted, along with the
  now-dead `RequestBodyFields.SessionId`/its extraction in `RequestBodyPeek`.
- [x] Tests: deleted `StickySessionTests` (the generic-session scenarios it covered no longer apply); its one
  still-relevant scenario ("a failed sticky endpoint fails fast, no silent reroute") was ported into
  `ResponsesApiAffinityTests` using `previous_response_id` instead of a generic session id.

## Phase 3 — Realtime (voice) API via WebSocket proxy

- [x] Domain: add `OperationType.RealtimeVoice`.
- [x] Infrastructure: `RealtimeProxyService` — accepts the inbound WebSocket
  (`HttpContext.WebSockets.AcceptWebSocketAsync`), resolves `model` from the **query string** (no body to peek
  on a WebSocket handshake), resolves the `ModelDeployment`, selects an endpoint via the same
  `EndpointSelector` (priority + round-robin — no affinity/stickiness needed, the connection itself is the
  affinity), resolves the credential via the existing `ICredentialProvider`, and opens an outbound
  `ClientWebSocket` to the backend (`Authorization: Bearer` header set via `ClientWebSocket.Options` before
  connecting). If the outbound connect fails, mark the endpoint unhealthy and try the next
  priority/round-robin candidate (bounded by the deployment's endpoint count) — never mid-session. Once
  connected, pump frames bidirectionally (two loops) until either side closes; propagate the close
  status/reason back to the other side.
- [x] Api: `app.UseWebSockets()`; new ingress route `GET /openai/v1/realtime` behind the same
  `ConsumerAuthEndpointFilter` as other ingress routes (the handshake is a normal HTTP GET with headers before
  the protocol upgrade, so the existing `X-Api-Key` filter applies unchanged).
- [x] Test infrastructure: a minimal in-process fake WebSocket backend (a second `WebApplication`/Kestrel test
  host with `UseWebSockets()` + an echo/scripted endpoint) since WireMock.Net doesn't support WebSocket —
  reused across the new realtime tests the same way `WireMockServer` is reused for HTTP fakes.
- [x] Integration tests (`AiProxy.Api.Tests`): successful connect + bidirectional echo round-trip; unknown
  deployment / missing `model` query param rejected before any upgrade attempt; handshake failure on the
  priority-1 endpoint fails over to priority-2; missing/invalid `X-Api-Key` rejected with 401 before upgrade.

## Phase 4 — Documentation

- [x] `architecture.md`: rewrote §6.2 (Responses API affinity streaming example, replacing the removed generic
  session mechanism), added §6.5 (Realtime API), rewrote §9's priority/round-robin/429-failover and narrowed
  affinity description; updated §2 scope table (6.2/6.3), §4 layer responsibilities (`OperationType.RealtimeVoice`,
  `IEndpointRoundRobinCounter`, `RealtimeProxyService`, removed the priority-uniqueness invariant mention), §7.1
  schema table (dropped `sticky_session_enabled`), §12 API surface summary (new Realtime row), §16 technology
  choices (`System.Net.WebSockets`), §17 assumptions (removed the stale generic-session-id assumption), §18
  non-goals (Realtime now implemented; Azure Realtime/generic WS backends still deferred).
- [x] Diagrams: 01 (high-level) ingress/routing box labels updated to mention the Realtime route and
  round-robin; 04 (streaming sequence) rewritten to depict the Responses API's `previous_response_id` affinity
  instead of the removed generic `session` field — both re-exported to PNG.
- [x] `README.md`: intro paragraph and testing-strategy bullet updated to describe priority + round-robin +
  429 failover, automatic Responses/Realtime affinity (no more generic "sticky sessions"), and the Realtime
  WebSocket proxy.
- [x] `specs/iterations/iteration-1/tasks.md`: no change needed (historical record of Iteration 1, left as-is).

## Phase 5 — Design challenge follow-ups: multi-replica affinity, exhausted-failover error propagation

Two questions raised after Phase 4 review:

1. **"With multiple AI Proxy instances behind an ingress controller, do sessions still stick to the correct
   instance?"** Answer: yes, and no ingress-level sticky routing to a specific replica is needed — analyzed and
   written up in architecture.md §9 ("Multiple AI Proxy replicas..."). Responses API affinity lives in Postgres
   (shared), and Realtime affinity is the WebSocket TCP connection itself (inherently pinned to one replica by
   the transport, not the application). The one caveat: `IEndpointRoundRobinCounter`/`IEndpointHealthTracker`
   are per-replica in-memory state, which is an accepted trade-off (round-robin only needs to be fair in
   aggregate; the health tracker isn't actually consulted by `EndpointSelector` today regardless of replica
   count) — no code change required, this was a documentation gap, not a bug.
- [x] `architecture.md` §9: added the multi-replica analysis above.

2. **"If the priority-1 endpoint fails and secondary endpoints fail with a different error, return the first
   (priority-1) error to the client — and if it's a 429, include the backoff headers."** This *was* a real gap:
   `ProxyRoutingService` discarded the actual upstream failure entirely once every endpoint was exhausted,
   always synthesizing a generic 503 regardless of what any backend actually returned.
- [x] `DirectHttpProxy.CaptureFailureSnapshotAsync`: buffers a failed (429/5xx) response's status, non-hop-by-hop
  headers, and body before the attempt is discarded/retried.
- [x] New `UpstreamFailureSnapshot` record (`RoutingFailure.cs`) + `RoutingFailure.UpstreamFailure` optional
  property; `UpstreamFailureSnapshot.WriteToAsync` replays it onto the client response verbatim.
- [x] `ProxyRoutingService.RouteAsync`: captures the **first** endpoint's failure snapshot per request
  (`firstFailureSnapshot ??= outcome.Failure`) and attaches it to the `RoutingFailure` once failover is
  exhausted — deliberately **not** attached on the sticky-endpoint-failure path, which intentionally stays a
  generic fail-fast error (architecture.md §9).
- [x] `Program.cs`'s `WriteRoutingFailureAsync`: when `UpstreamFailure` is present, replays it (status/headers/
  body) instead of the generic JSON error envelope.
- [x] Tests updated/added (`AiProxy.Api.Tests`): `ChatCompletionRoutingTests.AllEndpointsUnhealthy_ReturnsFirstEndpointsOriginalError`
  (renamed/rewritten — now expects the priority-1 endpoint's real 500, not a synthetic 503, even though
  priority-2 failed differently with 503); `LoadBalancingTests.AllEndpointsReturn429_ReturnsFirstEndpointsOriginal429WithRetryAfterHeader`
  (renamed/rewritten — asserts the original 429 + `Retry-After` header round-trips to the client);
  `LoadBalancingTests.FirstEndpointFails429_SecondEndpointFailsWithADifferentError_ClientSeesFirstEndpoints429`
  (new — the exact "different error" scenario from the question, 429 vs 500).
- [x] `architecture.md` §9: added the "Exhausted failover — client sees the first endpoint's real error" bullet.
