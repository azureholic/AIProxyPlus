# Iteration 15 — Workload OIDC and Managed Identity Authentication

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md), especially §7, §13, §13.2,
and §19.3.

## Goal

Allow machine Consumers to invoke every ingress protocol with a standards-based OIDC access token or Microsoft
Entra managed-identity access token while preserving local enrollment, disable/revoke controls, model allow-lists,
and usage attribution. Browser OIDC sessions and workload tokens remain independent trust configurations.

## Controlling decisions

- A Consumer uses exactly one authentication type: API key, OIDC issuer + subject, or managed-identity tenant +
  object ID. Database constraints and unique indexes enforce exclusivity and external identity uniqueness.
- Workload schemes are independently configurable and disabled by default. Validation requires exact issuer,
  audience, lifetime, signature, and signing algorithm.
- Generic OIDC requires the configured scope (`aiproxy.invoke` by default). Managed identity requires the configured
  app role (`AiProxy.Invoke` by default).
- Every workload identity must be pre-enrolled by an administrator. A valid token never auto-provisions access.
- All accepted credentials resolve to the same local Consumer context before routing, model authorization, and usage
  capture. Realtime accepts bearer authentication through the header; its existing browser ticket remains separate.

## Completed work

- [x] Add mutually exclusive managed-identity fields and invariants to the Consumer aggregate.
- [x] Add PostgreSQL mappings, uniqueness/check constraints, migration, and repository lookup by tenant + object ID.
- [x] Add validated admin commands and routes to pre-enroll OIDC and managed-identity Consumers.
- [x] Add lookup-only federated Consumer resolution with enabled/revoked enforcement.
- [x] Configure independent named JWT bearer schemes for generic OIDC and managed identity.
- [x] Enforce required scope/app role and converge API key, bearer, browser cookie, and Realtime credentials.
- [x] Add bearer OpenAPI security metadata.
- [x] Extend the Consumers UI with authentication-type-specific enrollment and API-key-only rotation controls.
- [x] Document configuration, enrollment, and security boundaries in architecture and README.
- [x] Add Domain, Application, Infrastructure, API integration, and frontend coverage.

## Validation

- [x] Domain Consumer tests.
- [x] Federated enrollment Application tests.
- [x] Managed-identity PostgreSQL repository test.
- [x] Architecture dependency tests.
- [x] Real JWT authorization integration tests for both schemes.
- [x] Consumers frontend tests.
- [x] Complete backend solution test suite.
- [x] Complete frontend test and production build.
- [ ] Manual provider token validation against a configured deployment.
