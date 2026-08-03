# Iteration 4 — Tasks

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration is planning
only at present: every task below is intentionally unchecked, and no authentication behavior is changed yet.

## Motivation

Iteration 3's frontend asks operators to paste an Admin API Key and Consumer API Key into `sessionStorage`.
Iteration 4 replaces both frontend key stand-ins with an interactive OIDC login and a same-origin server session
for Config, Usage, and Playground inference calls. Every admitted frontend user is an administrator and may select
any configured model deployment. Inference-only clients outside the frontend continue to use Consumer API Keys.
The login design must work with any provider that satisfies the documented OIDC capability contract, without a
provider SDK or provider-specific branch in the application.

## Design challenge — resolve before implementation

Every user allowed to log into the frontend is an administrator in Iteration 4. This deliberately avoids portable
role/group mapping, which OIDC does not standardize, but moves the authorization boundary to **who the provider
allows to sign in to this OIDC client**. If the provider allows every user to access the client, every one of those
users becomes an AI Proxy administrator. Deployment documentation and tests must make that consequence explicit;
restricting login eligibility at the provider is not optional in a shared identity domain.

OIDC users still need to become Consumers because every inference usage record requires a real Consumer ID. The
visible Consumer name should be the user's configured username claim, but **username is not a safe identity key**:
it can change, may be absent, and may collide with another user or API-key Consumer. The durable identity must be
the token's normalized `(issuer, subject)` pair. On first inference, the API creates an OIDC-backed Consumer for
that identity; later requests reuse it and may refresh its display name. A name collision must never merge two
identities silently.

### Proposed scope boundary

- Use the Backend-for-Frontend pattern: ASP.NET Core performs OIDC Authorization Code flow as a confidential web
  client and keeps its client credential on the server. The React application never handles provider tokens.
- Validate the OIDC authorization response and ID token from discovery/JWKS metadata, including signature, exact
  issuer, client ID/audience, nonce, lifetime, and allowed algorithms. Do not call provider-specific APIs.
- Establish an encrypted, `Secure`, `HttpOnly`, appropriately `SameSite` same-origin session cookie after login.
  Protect every state-changing cookie-authenticated request with antiforgery validation and validate WebSocket
  origins. Do not persist provider tokens in the authentication cookie unless a later use case requires them.
- Support one configured OIDC authority/issuer per deployment in Iteration 4. Multi-issuer federation is deferred.
- Treat every valid OIDC server session from the configured issuer/client as an administrator. No application
  role/group mapping or per-user RBAC is introduced.
- Accept that session on Config, Usage, and HTTP inference routes. Inference-only clients continue to use Consumer
  API Keys; both mechanisms must resolve a Consumer before forwarding.
- Resolve OIDC Consumers by normalized `(issuer, subject)` and use a configurable token claim such as
  `preferred_username` as `Consumer.Name`. Never identify a user by display name, email, or username alone.
- Provision an OIDC Consumer just in time on the user's first inference request. OIDC Consumers do not receive or
  store an API key, and API-key Consumers keep their current behavior.
- Keep static Admin API Key authentication as an explicitly configurable migration/break-glass option during
  rollout; disable it by default after migration is proven. Consumer API Keys remain supported.
- For Realtime Playground inference, exchange the server session for a short-lived, single-use WebSocket ticket tied
  to the resolved OIDC Consumer. Never place the OIDC access token in a WebSocket query string.

### Target Playground flow

1. The administrator signs in through the configured OIDC provider using server-side Authorization Code flow.
2. The server validates the callback, establishes the same-origin session, and redirects to the frontend.
3. The administrator selects any enabled deployment in the existing deployment picker.
4. For Chat or Images, the browser sends the session cookie and antiforgery header to the inference route; no
  provider token or Consumer API Key is requested or stored by the frontend.
5. The API resolves `(issuer, subject)` from the authenticated session and atomically finds or creates the corresponding
  OIDC Consumer. `Consumer.Name` comes from the configured username claim.
6. Routing proceeds unchanged, and usage is recorded with that Consumer ID and username.
7. For Voice/Realtime, the frontend exchanges its server session for a single-use WebSocket ticket; the
  WebSocket handshake consumes that ticket and restores the same Consumer identity before proxying begins.

### Provider portability strategy

The portability boundary is the **OIDC authorization response and server session**, not a browser SDK or provider
access-token format. Keycloak is the executable local fixture, but it is not a privileged runtime provider profile.
Production providers use the same configuration and code path.

A provider is compatible when it supports:

- OIDC discovery with an exact issuer, authorization endpoint, token endpoint, and `jwks_uri`;
- confidential web clients using Authorization Code flow and exact redirect URIs;
- signed ID tokens with stable `iss` + `sub`, the configured client ID in `aud`, and a supported asymmetric signing
  algorithm;
- `state` and `nonce` round trips required by the OIDC handler; and
- either the configured username claim in the ID token or a standards-based UserInfo endpoint that returns it.

Optional discovery capabilities such as RP-Initiated Logout are detected rather than assumed. Local session logout
must always work; provider-session logout is enabled only when a discovered `end_session_endpoint` and its required
parameters are compatible. Additional authorization parameters may be supplied declaratively, but configuration
must reject overrides of protocol-critical values such as `client_id`, `redirect_uri`, `response_type`, `state`,
`nonce`, and PKCE fields. Client credentials are server-side secrets and never appear in frontend runtime config.

This is generic OIDC, not unrestricted identity-provider compatibility. A product that lacks OIDC discovery,
requires a proprietary browser SDK, cannot register a confidential web client, or does not provide stable `iss` +
`sub` identity cannot be supported without a separately designed adapter. OAuth-only/SAML providers and arbitrary
opaque access-token behavior are irrelevant to the BFF session and remain outside this iteration.

## Phase 0 — Architecture decision and provider compatibility spike

- [x] Write the authentication/authorization threat model: authorization-response theft, login CSRF/state, nonce
  and PKCE handling, session fixation/theft, request CSRF, open redirects, issuer confusion, signing-key rotation,
  replay, clock skew, logout behavior, provider-side login eligibility, and accidental logging of tokens or claims.
- [x] Decide and document the supported deployment model: one authority/issuer, confidential web client ID and
  secret reference, `openid` plus optional identity scopes, callback URI, post-logout URI, session lifetime, and a
  fixed allow-list of asymmetric signing algorithms.
- [x] Define a provider-neutral configuration contract using OIDC terms (`authority`, `issuer`, `clientId`,
  `clientSecretRef`, `scopes`, `usernameClaim`, `getClaimsFromUserInfo`, callback/logout paths, cookie settings, and
  validated additional authorization parameters). Do not add provider product concepts to Domain or Application.
- [x] Make `AiProxy.AppHost` the single owner of deploy-time configuration. API and frontend projects must not
  duplicate provider settings in their own `appsettings*.json` files; AppHost reads configuration, resolves secret
  parameters, and supplies each child resource only the values it needs through environment variables. Document
  the environment-variable names as the contract between AppHost and each resource.
- [x] Define the admin policy contract as a valid server session established by the configured OIDC issuer/client;
  do not introduce role or group claim mapping in this iteration.
- [x] Define provider-side admission requirements generically: the provider's client assignment, policy, or tenant
  controls must ensure only intended administrators can complete login.
- [x] Define the OIDC Consumer lifecycle: immutable normalized issuer + `sub`, configurable username claim,
  just-in-time creation on first inference, display-name refresh, local disable/re-enable behavior, name-collision
  handling, and what the Consumers admin page may modify. Never generate a placeholder API key for an OIDC user.
- [x] Decide the collision policy before implementation. Recommended: require the configured username claim to be
  present and unique, reject a conflicting identity with an actionable `409`/`403`, and never suffix or merge names
  silently. If duplicate display names must be supported, redesign Consumer addressing away from unique names and
  name-based config routes as a separate, explicitly accepted schema/API change.
- [ ] Spike ASP.NET Core's standards-based OIDC handler against local Keycloak and at least one independent OIDC
  conformance fixture. Prove discovery, Authorization Code + PKCE, callback validation, optional UserInfo claims,
  session establishment/expiry, local logout, optional provider logout, and signing-key rotation. Record protocol
  traces with secrets redacted and remove any assumption that requires a product-specific SDK.
- [ ] Build a reusable provider conformance check that accepts operator-supplied OIDC configuration and reports
  capability results without persisting credentials: exact issuer/discovery, endpoints, supported code flow and
  PKCE method, asymmetric ID-token algorithm overlap, callback, stable `iss` + `sub`, client-ID audience, configured
  username claim or UserInfo fallback, logout capability, and key rotation. Reject wrong issuer/client, state,
  nonce, signature, lifetime, callback, or algorithm; never repair issuer mismatches with partial comparisons.
- [x] Record unsupported capabilities explicitly: providers without OIDC discovery/JWKS, confidential Code flow,
  stable `iss` + `sub`, or a usable username source require a separately designed adapter and are out of scope.
- [x] Update `specs/architecture/architecture.md` with the accepted decisions and amend this plan if the spike
  disproves any assumption. Do not begin Phase 1 until the security and identity decisions are accepted.

## Phase 1 — API authentication and authorization foundation

- [ ] Add ASP.NET Core OpenID Connect and cookie authentication in `AiProxy.Api` using standards-based
  discovery/JWKS metadata; bind and validate options at startup, resolve the client secret through the existing
  secret boundary, fail fast on incomplete or unsafe production configuration, and keep all protocol wiring in Api.
- [ ] Add an `AdminAccess` authorization policy for Config and Usage routes. A valid token from the configured
  OIDC server session satisfies the policy because every admitted frontend user is an administrator.
- [ ] Add composite inference authentication: Consumer API Key for non-interactive clients or OIDC server session
  for frontend users. Both paths must produce one shared authenticated-caller result containing Consumer ID and
  name before routing begins; inference API keys must not satisfy `AdminAccess`.
- [ ] Introduce an Application-layer OIDC Consumer resolver keyed by normalized issuer + subject. Keep ASP.NET
  `ClaimsPrincipal` and claim parsing in the Api/Infrastructure boundary.
- [ ] Extend `Consumer` and persistence to distinguish API-key and OIDC identities: API-key hash is required only
  for API-key Consumers; OIDC issuer/subject are required and unique only for OIDC Consumers. Add an EF migration
  and preserve existing API-key Consumer rows without data rewriting.
- [ ] Implement race-safe just-in-time OIDC Consumer creation and lookup. Resolve concurrent first requests to one
  row, reject username collisions without identity merging, and preserve `Enabled` as a local kill switch.
- [ ] Update Consumer config responses and UI to show authentication type. Disable key rotation for OIDC Consumers;
  revocation/disable behavior must not pretend to revoke the upstream OIDC account.
- [ ] Replace `AdminAuthEndpointFilter` with policy-based composition where practical without changing route
  behavior beyond the configured cookie/Admin API Key migration schemes.
- [ ] Keep Admin API Key fallback behind an explicit configuration switch; use a separate authentication scheme
  and policy composition so cookie and API-key behavior can be tested independently.
- [ ] Add antiforgery token issuance and validation for cookie-authenticated unsafe HTTP methods, including
  Playground inference. API-key requests remain header-authenticated and must not be forced through browser CSRF
  mechanics. Validate allowed `Origin` values for login initiation, logout, and WebSocket ticket flows.
- [ ] Return standards-correct `401` challenges for missing/invalid tokens and `403` for authenticated callers
  whose OIDC Consumer is locally disabled. Browser navigation may redirect to login, but API requests must return
  status codes rather than HTML redirects. Return an explicit conflict for unsafe username collisions.
- [ ] Document cookie session, antiforgery, Admin API Key fallback, and Consumer API Key requirements in OpenAPI
  without implying that every scheme is accepted by every route.

## Phase 2 — Frontend login and session lifecycle

- [ ] Add same-origin login, callback, logout, session-status, and antiforgery endpoints. Keep OIDC configuration,
  callback processing, tokens, and client credentials on the server; the frontend receives only minimal user and
  session metadata.
- [ ] Add authenticated app state, loading/error/expired-session states, and restore the intended route after
  successful login using only validated same-origin return paths.
- [ ] Replace the Settings page's Admin API Key field with signed-in user/session information. Show the fallback
  key control only when migration mode is enabled; remove the Consumer API Key field from the normal frontend flow;
  do not expose or retain provider tokens in browser storage or JavaScript state.
- [ ] Update the API client to send same-origin credentials and the antiforgery header on unsafe methods, handle
  session expiry without retry loops, and redirect to login when reauthentication is required.
- [ ] Keep the deployment picker backed by the admin-protected model-deployment list. A logged-in administrator can
  select any enabled deployment and invoke Chat or Images with the same session, without entering a key.
- [ ] Show the resolved Consumer username in Playground results and usage views so the administrator can verify
  that inference was attributed to their own identity.
- [ ] Ensure frontend and server telemetry, error rendering, browser history, and console output never expose
  cookies, tokens, client credentials, authorization codes, PKCE verifier values, or raw claim sets.

## Phase 3 — Realtime browser authentication

- [ ] Add a session-authenticated, antiforgery-protected endpoint that resolves/provisions the OIDC Consumer and issues a cryptographically
  random, single-use, short-lived WebSocket ticket stored only as a hash with Consumer identity and expiry.
- [ ] Accept the ticket on `/openai/v1/realtime`, consume it atomically before upgrading, and place the resolved
  Consumer ID/name into the same routing context as HTTP inference. Reject reuse, expiry, wrong route, malformed
  tickets, API-key Consumers, and disabled Consumers.
- [ ] Update the Voice Playground to request a ticket immediately before opening the WebSocket and pass only that
  ticket in the query string.
- [ ] Keep the existing Consumer API Key query fallback for non-frontend compatibility, with header precedence;
  never permit session cookies or provider tokens to be copied into WebSocket query strings.

## Phase 4 — Tests and security verification

- [ ] Unit-test OIDC configuration validation, issuer/subject normalization, ID-token and UserInfo username
  extraction, session settings, Consumer type invariants, collision handling, concurrent provisioning, antiforgery,
  and WebSocket ticket single-use/expiry.
- [ ] Add API integration tests with local OIDC/JWKS and browser-session fixtures for valid login, wrong issuer,
  wrong client-ID audience, state/nonce failures, expired/not-yet-valid ID token, unsupported algorithm, key
  rotation, missing username, UserInfo fallback, duplicate username, disabled OIDC Consumer, session expiry,
  antiforgery failure, Admin API Key fallback enabled/disabled, and API-key regression coverage.
- [ ] Add parameterized provider-capability contract tests for discovery metadata, exact issuer, Code + PKCE support,
  endpoint and algorithm validation, ID-token claim mapping, optional UserInfo, optional end-session endpoint, and
  rejection of reserved additional authorization parameters. The tests must contain no product-name branches.
- [ ] Test every protected route family to prove Config/Usage cannot be reached with inference-only credentials and
  both API-key and session inference resolve the correct Consumer. Verify session inference usage is attributed to
  the OIDC username while the durable lookup remains issuer + subject.
- [ ] Add frontend tests for login redirect, callback success/failure, session status, antiforgery headers, expiry,
  logout, restored route, fallback-mode visibility, and credential redaction.
- [ ] Add Playwright coverage against local Keycloak for login, selecting each available deployment, session-based
  Chat/Images inference, Realtime ticket inference, username attribution, logout, and unauthenticated redirect.
- [ ] Publish and exercise a provider-onboarding conformance checklist that operators can run against their chosen
  provider without source changes. Keep CI deterministic with Keycloak; do not make CI depend on a cloud tenant.
- [ ] Run the focused test projects first, then `dotnet test AiProxy.sln`, `npm test`, and `npm run build`; include
  architecture tests because authentication responsibilities and dependencies cross Api/Application/Infrastructure.
- [ ] Perform a security review before enabling session authentication by default, including dependency audit and
  inspection of application, reverse-proxy, and browser logs for credential leakage, CSRF, cookie scope, session
  fixation, and unsafe redirect behavior.

## Phase 5 — Deployment and documentation

- [ ] Add a vendor-neutral provider example to `src/AiProxy.AppHost/appsettings.json` using this intended shape
  (illustrative values only; the task implementation may refine names once the options type is finalized):

  ```json
  {
    "Authentication": {
      "Oidc": {
        "Authority": "https://identity.example.com/realms/ai-proxy",
        "ClientId": "ai-proxy-web",
        "ClientSecretParameter": "oidc-client-secret",
        "Scopes": ["openid", "profile"],
        "UsernameClaim": "preferred_username",
        "GetClaimsFromUserInfo": false,
        "CallbackPath": "/signin-oidc",
        "SignedOutCallbackPath": "/signout-callback-oidc"
      }
    }
  }
  ```

  Keep the actual client secret in an Aspire secret parameter/user secret, never in JSON. Add capability-based
  recipes for ID-token username claims, UserInfo fallback, optional provider logout, and validated additional
  authorization parameters without introducing vendor-specific configuration sections.
- [ ] Bind and validate `Authentication:Oidc` in AppHost, then map it explicitly to the API resource using .NET
  configuration environment-variable names such as `Authentication__Oidc__Authority`,
  `Authentication__Oidc__ClientId`, `Authentication__Oidc__Scopes__0`, and
  `Authentication__Oidc__UsernameClaim`. Resolve `ClientSecretParameter` to the secret value and pass that value as
  `Authentication__Oidc__ClientSecret`; never forward the parameter name as though it were a credential.
- [ ] Keep environment propagation least-privileged: only the API receives OIDC authority, client ID, client
  secret, scopes, claim mapping, and callback settings. The frontend receives no provider configuration or secret;
  under the BFF design it calls same-origin login/session/antiforgery endpoints. If future frontend runtime settings
  are needed, AppHost must supply them through its resource environment and an explicit runtime-config mechanism,
  not through duplicated `AiProxy.Web` configuration files or Vite build-time constants.
- [ ] Add a pinned Keycloak container to `AiProxy.AppHost` for local OIDC testing. Run it in development/import
  mode, expose a stable HTTP endpoint, persist its data in a named volume, use
  `WithLifetime(ContainerLifetime.Persistent)`, and make the API wait for Keycloak readiness. Pass Keycloak's
  issuer/authority, client ID, and server-side client-secret reference to the API from the same source so
  AppHost-assigned endpoints do not drift from OIDC configuration.
- [ ] Add an importable `ai-proxy-local` development realm configuration containing:
  - a confidential `ai-proxy-web` client using Authorization Code flow + PKCE (`S256`), with direct-access grants,
    implicit flow, and service accounts disabled and client authentication enabled;
  - exact local callback and post-logout redirect URIs for both the Vite proxy development path and the SPA served
    by `AiProxy.Api`, with no wildcard origins or redirects;
  - ID-token claims `iss`, `aud`, `sub`, and `preferred_username` satisfying the generic validation contract; and
  - short development ID-token and application-session lifetimes suitable for exercising expiry behavior.
- [ ] Supply the local Keycloak client credential through an Aspire secret parameter/user secret or idempotent
  post-import bootstrap, never in committed realm JSON. Verify rotation by changing the secret source without
  rebuilding the frontend.
- [ ] Seed one enabled local user named `proxy-admin`. Supply its initial development-only password through a
  secret Aspire parameter/user secret or a post-import bootstrap command, not in the committed realm JSON. Require
  no password change on first login so the account can immediately access the frontend, make Config/Usage changes,
  select any model in the Playground, and run session-authenticated inference attributed to Consumer
  `proxy-admin`. Document the local username and how to set/reset the password; label the account as development
  only and never reuse its credentials outside local testing.
- [ ] Make Keycloak bootstrap idempotent: a fresh environment imports the realm and creates `proxy-admin`; an
  existing persistent Keycloak volume is reused without duplicate users/clients or overwriting a deliberately
  changed password. Document an explicit reset procedure that removes only the local Keycloak volume and then
  reimports the realm.
- [ ] Add a local smoke test/checklist proving `proxy-admin` can complete Code + PKCE login, establish a secure
  session, call Config and Usage APIs with antiforgery protection, list/select all enabled model deployments, run Chat/Images inference without
  a Consumer API Key, receive usage attribution as `proxy-admin`, obtain and consume a Realtime ticket, log out,
  and log back in after the AppHost has been stopped and restarted.
- [ ] Update `AiProxy.AppHost` so the Postgres and Vault container resources use Aspire's persistent container
  lifetime (`WithLifetime(ContainerLifetime.Persistent)`) and stable named volumes. Verify that stopping or closing
  the AppHost leaves both containers running, restarting the AppHost reuses them, and existing database rows and
  Vault secrets remain available. Postgres already uses `WithDataVolume`; do not mistake Vault's current
  `/vault/file` mount for durable secret storage while Vault runs in dev mode, because dev mode stores data in
  memory. Either document that Vault data survives only while the persistent container itself exists, or switch
  local Vault to a file-storage configuration if secrets must also survive container deletion/recreation and Docker
  restarts.
- [ ] Update container and reverse-proxy guidance for HTTPS, forwarded headers, exact redirect URIs, issuer URL
  reachability, discovery/JWKS caching, and key rotation.
- [ ] Update `README.md` with login setup, provider registration and user-admission checklists, username-claim
  selection, OIDC Consumer lifecycle/collision handling, migration from Admin API Key, API-key client coexistence,
  the provider conformance check, and troubleshooting for issuer/client/callback/claim/cookie mismatches.
- [ ] Update `specs/architecture/architecture.md`, the relevant draw.io sources/exports, API surface, security
  boundaries, data model, frontend auth section, assumptions, and non-goals to reflect implemented behavior.
- [ ] Mark this task plan complete only after the provider compatibility matrix and all required validation pass.

## Explicit non-goals for Iteration 4

- Multi-issuer or per-tenant federation in one running proxy instance.
- Opaque-token introspection and non-OIDC OAuth providers.
- Claiming compatibility with an OIDC provider that has not passed the provider conformance suite.
- Provider lifecycle synchronization (SCIM) or provider-specific directory/Graph API calls.
- Application role/group mapping, per-user RBAC, or permissions below the all-frontend-users-are-admin boundary.
- Replacing Consumer API Keys for non-frontend inference clients.
- Treating username, email, or display name as the durable identity instead of OIDC issuer + subject.
- Acting as an authorization server or token broker, exposing OIDC client credentials to the browser, or adding
  product-specific authentication adapters in the generic Iteration 4 path.
