# Iteration 5 — Bug Hunting

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration captures
bugs found through live use, their root cause, regression coverage, and validation evidence.

## Bug 1 — Local OIDC username collision after Keycloak reset

### Observed symptom

Chat, Images, or Realtime ticket provisioning returns `409 Conflict`:

```text
The OIDC username 'proxy-admin' is already assigned to another consumer.
```

### Root cause

- [x] Confirm the resolver first looks up the immutable `(issuer, subject)` identity and rejects a username owned
  by a different identity.
- [x] Confirm persistent Postgres contained `proxy-admin` with the subject from the previous Keycloak realm.
- [x] Confirm recreating only the Keycloak data volume generated a new opaque user ID and therefore a new `sub`.
- [x] Preserve the collision rejection; merging identities by username would permit account takeover.

### Fix

- [x] Pin an explicit non-secret UUID for the seeded `proxy-admin` user in the local realm import.
- [x] Add an architecture contract test that prevents accidental removal or mutation of the seeded subject.
- [x] Document that Keycloak-only resets preserve the local OIDC identity while leaving Postgres and Vault intact.
- [x] Reconcile the existing local Consumer row to the pinned subject and verify Chat no longer returns the
  collision. Browser acceptance reached upstream forwarding; the configured deployment returned a separate
  `404 Not Found`.
- [x] Run focused architecture tests, the full solution tests, frontend tests, and frontend production build.

## Bug 2 — Structured provider errors render as objects

### Symptom

Chat and Images display `Error [object Object]` when an OpenAI-compatible provider returns a structured error.

### Root cause and fix

- [x] Capture the live response and confirm the provider returned
  `{"error":{"code":"404","message":"Resource not found"}}`.
- [x] Extend the shared API error parser to support string errors, OpenAI-compatible structured errors, and
  Problem Details messages.
- [x] Route Chat and Images through the shared parser so status, message, headers, and retry metadata are
  preserved consistently.
- [x] Add regression coverage and rebuild the frontend static assets.
- [x] Verify the same live response renders as `Error 404 Resource not found`.

## Bug 3 — Existing endpoint configuration cannot be corrected

### Symptom

The model detail page permits adding, enabling, rotating, and removing endpoints, but offers no way to
correct an existing base URL or managed-identity scope. A malformed Azure OpenAI base URL therefore requires
deleting and recreating the endpoint.

### Root cause and fix

- [x] Confirm the limitation exists throughout the update path: the domain aggregate, application command, API
  body, frontend client, and detail page support only priority, enabled state, timeout, and retries.
- [x] Add validated aggregate mutations and extend the existing update use case with base URL and managed-identity
  scope. Preserve immutable API-family and authentication-mode boundaries.
- [x] Add an endpoint edit dialog while keeping API-key rotation in its separate write-only flow.
- [x] Add domain, application, and frontend API-contract regression coverage.
- [x] Run the full .NET solution in Release configuration, all frontend tests, and the frontend production build.

## Bug 4 — Managed-identity token uses the wrong tenant

### Symptom

Azure rejects an upstream request with `400 Token tenant ... does not match resource tenant` when the local
developer credential's default tenant differs from the Azure AI resource tenant.

### Root cause and fix

- [x] Confirm managed-identity endpoint configuration contains a resource scope but no tenant selector, so token
  acquisition inherits the credential chain's default tenant.
- [x] Add an optional validated `ManagedIdentityTenantId` to the endpoint aggregate, persistence model, create and
  update contracts, and configuration UI.
- [x] Pass the configured tenant ID through `TokenRequestContext` while preserving default behavior when omitted.
- [x] Add domain, application, infrastructure, and frontend API-contract regression coverage.
- [x] Run the full .NET solution, architecture and persistence tests, frontend tests and production build; apply
  the migration locally and verify the edit dialog exposes tenant ID while keeping API family and auth mode
  immutable, then cancel without changing the endpoint.
