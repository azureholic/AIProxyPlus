# Iteration 6 — Reusable Endpoints

Companion to [specs/architecture/architecture.md](../../architecture/architecture.md). This iteration separates
reusable backend endpoint configuration from model-deployment routing assignments.

## Phase 1 — Domain and persistence

- [x] Make `Endpoint` an independently named aggregate containing target, authentication, credential, timeout,
  and retry configuration.
- [x] Add `ModelDeploymentEndpoint` as the deployment-owned assignment containing priority, enabled state, and
  runtime health.
- [x] Migrate existing endpoint rows without losing deployment assignments or Vault secret references.
- [x] Add repository and persistence coverage for reusable endpoint CRUD, assignment loading, uniqueness, and
  delete protection.

## Phase 2 — Application and API

- [x] Add validated reusable endpoint CRUD and write-only API-key rotation handlers.
- [x] Change deployment endpoint commands to assign, update, and remove existing endpoints.
- [x] Add top-level `/api/v1/config/endpoints` routes and preserve deployment routing responses.
- [x] Add application and API regression coverage for reuse across multiple model deployments.

## Phase 3 — Frontend

- [x] Add Config > Endpoints navigation and reusable endpoint list/create/edit/delete flows.
- [x] Replace inline endpoint creation on model deployments with existing-endpoint selection.
- [x] Keep assignment priority and enabled controls on the deployment detail page.
- [x] Add frontend API-contract and interaction coverage.

## Phase 4 — Documentation and validation

- [x] Update the domain diagram and exported image.
- [x] Update README configuration/API guidance.
- [x] Run focused tests, the full .NET solution, frontend tests/build, migration validation, and live browser
  acceptance.
