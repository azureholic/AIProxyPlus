# Iteration 5 — Bug Hunting

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration captures
bugs found through live use, their root cause, regression coverage, and validation evidence.

## Bug 1 — Local OIDC username collision after Keycloak reset

### Symptom

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