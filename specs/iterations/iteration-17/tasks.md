# Iteration 17 — Azure Deployment Task Breakdown

Implementation companion to [specification.md](specification.md). Check an item only after its implementation and
listed validation are complete.

## Phase 0 — Approval and Azure context

- [x] **T17-001 — Approve the target design**
  Review all REQ-1701 through REQ-1709 decisions and resolve the five open decisions.
  **Depends on:** none.
  **Traceability:** all requirements.

- [x] **T17-002 — Select subscription and region**
  Confirm the Azure subscription and a region supporting Azure Container Apps, PostgreSQL Flexible Server, Key
  Vault, ACR, Log Analytics, and Application Insights. Record AZD environment values.
  **Depends on:** T17-001.
  **Traceability:** REQ-1702, REQ-1704, REQ-1707.

- [x] **T17-003 — Validate quota and provider registration**
  Check quotas/capacity for the resource inventory in `.azure/deployment-plan.md` and verify required resource
  providers are registered.
  **Depends on:** T17-002.
  **Traceability:** REQ-1702.

## Phase 1 — Architecture and contracts

- [x] **T17-101 — Update the architecture specification**
  Revise architecture sections 1, 3, 4, 8, 11, 13, 16, 17, and 18 so HashiCorp Vault is the local provider and
  Azure Key Vault is the Azure provider behind `ISecretResolver`. Document the Container Apps deployment topology
  and retain vendor neutrality in Domain/Application.
  **Depends on:** T17-001.
  **Traceability:** REQ-1701, REQ-1705, REQ-1709.

- [x] **T17-102 — Update architecture diagrams**
  Add or revise the draw.io deployment diagram and exported image with Container Apps, managed identity, ACR,
  private PostgreSQL, Key Vault, Entra, and observability relationships.
  **Depends on:** T17-101.
  **Traceability:** REQ-1709.

- [x] **T17-103 — Define provider-neutral secret configuration**
  Add typed options for `Secrets:Provider`, `Secrets:Retirement`, and `AzureKeyVault:VaultUri`; define strict startup
  validation and a compatibility rule for `Vault:Retirement`.
  **Depends on:** T17-101.
  **Traceability:** REQ-1705.

## Phase 2 — Azure Key Vault adapter

- [x] **T17-201 — Add Azure Key Vault SDK dependencies**
  Add centrally managed `Azure.Security.KeyVault.Secrets` and any required Azure client-registration package without
  leaking Azure dependencies into Domain or Application.
  **Depends on:** T17-103.
  **Traceability:** REQ-1705.

- [x] **T17-202 — Implement deterministic secret-name mapping**
  Map opaque slash-delimited secret references to reversible or collision-resistant Key Vault-compatible names.
  Reject invalid/oversized references explicitly and test collision cases.
  **Depends on:** T17-201.
  **Traceability:** REQ-1705.

- [x] **T17-203 — Implement `AzureKeyVaultSecretResolver`**
  Implement get, set, and delete through `SecretClient`; translate only Key Vault not-found responses to `null`;
  propagate authorization, throttling, network, and service errors; never log values.
  **Depends on:** T17-202.
  **Traceability:** REQ-1705, REQ-1708.

- [x] **T17-204 — Configure environment-specific credentials**
  Use `ManagedIdentityCredential` in Azure and `DefaultAzureCredential` only for explicit local Azure Key Vault
  development. Support a user-assigned client ID only if the approved design selects it.
  **Depends on:** T17-203.
  **Traceability:** REQ-1705, REQ-1708.

- [x] **T17-205 — Wire provider selection in API composition**
  Register exactly one `ISecretResolver`; fail startup for unsupported/missing configuration; rename the readiness
  component to `secretStore`; move retirement options to provider-neutral configuration.
  **Depends on:** T17-203, T17-204.
  **Traceability:** REQ-1705.

- [x] **T17-206 — Add secret adapter tests**
  Add unit tests named with the repository convention for mapping, missing secrets, exception propagation, provider
  selection, startup validation, and log redaction. Add an opt-in integration test against a real development Key
  Vault or emulator only if it does not weaken fidelity.
  **Depends on:** T17-205.
  **Traceability:** REQ-1705, REQ-1708.

## Phase 3 — Aspire local/publish topology

- [x] **T17-301 — Add Aspire Azure hosting integrations**
  Add centrally managed Aspire packages required for Azure PostgreSQL and Key Vault publishing, pinned to the
  solution's Aspire version.
  **Depends on:** T17-001.
  **Traceability:** REQ-1701.

- [x] **T17-302 — Isolate local-only resources**
  Refactor `AppHost.cs` so HashiCorp Vault and Keycloak retain current local behavior but are excluded from the
  publish manifest. Preserve persistent volumes and local OIDC secrets.
  **Depends on:** T17-301.
  **Traceability:** REQ-1701.

- [x] **T17-303 — Define publish resources**
  Represent PostgreSQL Flexible Server and Azure Key Vault in publish mode, inject the `Secrets:Provider` and OIDC
  cloud configuration, and preserve API dependency ordering.
  **Depends on:** T17-302, T17-205.
  **Traceability:** REQ-1701, REQ-1704, REQ-1705, REQ-1706.

- [x] **T17-304 — Verify the production image path**
  Ensure the Aspire/AZD packaging path uses the existing multi-stage Dockerfile or otherwise builds the React SPA
  into API `wwwroot`. Verify Linux non-root execution, port 8080, WebSockets, and health routes.
  **Depends on:** T17-303.
  **Traceability:** REQ-1703, REQ-1708.

- [x] **T17-305 — Test local topology regression**
  Start the AppHost, verify all local dependencies, sign in through Keycloak, check `/health/live` and
  `/health/ready`, and stop all resources cleanly.
  **Depends on:** T17-302, T17-304.
  **Traceability:** REQ-1701.

## Phase 4 — AZD and Bicep

- [x] **T17-401 — Initialize AZD from AppHost**
  Run `azd init --from-code -e <environment>` from the repository root. Verify `azure.yaml` has a non-empty AppHost
  service and do not replace it with a manual file.
  **Depends on:** T17-303, T17-304, T17-002.
  **Traceability:** REQ-1702.

- [x] **T17-402 — Set and verify AZD context**
  Set `AZURE_SUBSCRIPTION_ID` and `AZURE_LOCATION` immediately after initialization; verify environment values and
  keep `.azure/<environment>` secrets out of source control.
  **Depends on:** T17-401.
  **Traceability:** REQ-1702.

- [x] **T17-403 — Build the network baseline**
  Add Bicep for the VNet, Container Apps infrastructure subnet, PostgreSQL delegated subnet, private DNS zone, and
  required links. Disable public PostgreSQL access.
  **Depends on:** T17-401.
  **Traceability:** REQ-1704, REQ-1708.

- [x] **T17-404 — Build observability resources**
  Add Log Analytics and workspace-based Application Insights with parameterized retention and connect the Container
  Apps environment/application telemetry.
  **Depends on:** T17-401.
  **Traceability:** REQ-1707.

- [x] **T17-405 — Build Key Vault and RBAC**
  Add RBAC-enabled Key Vault with soft delete and purge protection. Grant the Container App identity only the
  required secret get/set/delete permissions using a stable, resource-scoped role assignment.
  **Depends on:** T17-401.
  **Traceability:** REQ-1705, REQ-1708.

- [x] **T17-406 — Build PostgreSQL Flexible Server**
  Add secure parameters and environment-specific sizing for SKU, storage, HA, backup, and database creation.
  Construct TLS-required runtime/migration connection secrets without outputting credentials.
  **Depends on:** T17-403.
  **Traceability:** REQ-1704, REQ-1708.

- [x] **T17-407 — Build ACR and Container App**
  Provision ACR with admin disabled, Container Apps environment, external HTTPS ingress, probes, scaling, managed
  identity, secrets, environment variables, and ACR pull RBAC. Avoid circular role-assignment dependencies.
  **Depends on:** T17-403, T17-404, T17-405, T17-406.
  **Traceability:** REQ-1702, REQ-1703, REQ-1708.

- [x] **T17-408 — Validate Bicep and AZD packaging**
  Run Bicep build/lint, `azd config validate` or current equivalent, Aspire manifest generation, and `azd package`
  without provisioning. Confirm no local Vault/Keycloak resources appear in cloud output.
  **Depends on:** T17-407.
  **Traceability:** REQ-1701, REQ-1702, REQ-1703.

## Phase 5 — Microsoft Entra ID

- [x] **T17-501 — Define the Microsoft Graph Bicep contract**
  Enable the Microsoft Graph Bicep extension and define the Entra application and service principal with redirect
  URI, logout URI, token version, delegated scopes, and account type. Support an existing-registration parameter and
  document the tenant-level Graph permissions required by the deployment identity.
  **Depends on:** T17-001.
  **Traceability:** REQ-1706.

- [x] **T17-502 — Bootstrap the confidential-client credential**
  Since Graph Bicep does not support `passwordCredentials`, implement an idempotent, separately permissioned step
  that adds/rotates the credential and writes it directly to Key Vault without returning or logging plaintext.
  Alternatively, implement and test certificate-based OIDC client authentication and provision `keyCredentials`
  through Graph Bicep.
  **Depends on:** T17-501, T17-405.
  **Traceability:** REQ-1706, REQ-1708.

- [x] **T17-503 — Inject Entra configuration securely**
  Parameterize tenant ID and client ID as non-secrets and client credential as a secure value/secret reference.
  Populate the existing `Authentication:Oidc` settings for the Container App.
  **Depends on:** T17-407, T17-502.
  **Traceability:** REQ-1706, REQ-1708.

- [x] **T17-504 — Add OIDC configuration tests**
  Verify Entra authority validation, required HTTPS metadata, scopes, signing algorithms, callback paths, secure
  cookies, and that the client secret is absent from frontend artifacts and logs.
  **Depends on:** T17-503.
  **Traceability:** REQ-1706.

## Phase 6 — Migration ownership and runtime hardening

- [x] **T17-601 — Make migration ownership deterministic**
  For a one-replica baseline, document and enforce the restriction. If min/max replicas permit concurrent startup,
  add an Azure Container Apps Job or equivalent single-owner migration step and disable per-replica startup DDL.
  **Depends on:** T17-406, T17-407.
  **Traceability:** REQ-1704.

- [x] **T17-602 — Harden the runtime container**
  Run as non-root, keep the filesystem immutable where compatible, preserve only required write paths, and confirm
  Serilog does not depend on durable local container storage in Azure.
  **Depends on:** T17-304.
  **Traceability:** REQ-1703, REQ-1708.

- [x] **T17-603 — Validate security boundaries**
  Run secret scanning and a focused security review for Bicep outputs, RBAC, network exposure, OIDC credential
  handling, query-string logging, and Container App configuration.
  **Depends on:** T17-405, T17-407, T17-503, T17-602.
  **Traceability:** REQ-1706, REQ-1708.

## Phase 7 — Documentation and validation

- [x] **T17-701 — Update README and runbook**
  Document local and Azure paths, prerequisites, AZD environment setup, Entra redirect configuration, deployment,
  verification, logs, rollback, and teardown.
  **Depends on:** T17-408, T17-503, T17-601.
  **Traceability:** REQ-1709.

- [x] **T17-702 — Run focused backend tests**
  Run secret adapter, API startup/configuration, health, and architecture tests.
  **Depends on:** T17-206, T17-504, T17-601.
  **Traceability:** REQ-1705, REQ-1706.

- [x] **T17-703 — Run complete repository validation**
  Run `dotnet test AIProxyPlus.sln`, frontend `npm test`, frontend `npm run build`, Docker image build, Bicep
  build/lint, Aspire manifest generation, and `azd package`.
  **Depends on:** T17-701, T17-702, T17-603.
  **Traceability:** all requirements.

- [x] **T17-704 — Hand off to Azure validation**
  Change `.azure/deployment-plan.md` status to `Ready for Validation`, invoke the `azure-validate` workflow, and
  resolve every failure before deployment.
  **Depends on:** T17-703.
  **Traceability:** all requirements.

## Phase 8 — Deployment and acceptance

- [x] **T17-801 — Deploy with AZD**
  After validation and explicit deployment approval, run the Azure deployment workflow for `azd up`.
  **Depends on:** T17-704.
  **Traceability:** REQ-1702.

- [x] **T17-802 — Run cloud smoke tests**
  Verify HTTPS, live/ready health, Entra login/logout, authenticated config access, Key Vault secret round trip,
  PostgreSQL persistence, SPA fallback, Realtime WebSocket handshake, telemetry, and revision replacement.
  **Depends on:** T17-801.
  **Traceability:** all requirements.

- [x] **T17-803 — Close the iteration**
  Record exact validation evidence, remaining operational gaps, deployed outputs, and rollback instructions; then
  mark completed tasks and create one focused commit.
  **Depends on:** T17-802.
  **Traceability:** all requirements.

## Local runtime follow-up — WSLC (2026-11-13)

- [x] **T17-306 — Verify opt-in WSLC local orchestration**
  Add a separate `wslc` launch profile without changing the existing Docker profiles or cloud topology. Install
  standalone DCP 0.27.4 locally and persist its executable path in AppHost user secrets; retain the existing Aspire
  packages because no stable Aspire release currently bundles WSLC-capable DCP.
  **Depends on:** T17-305.
  **Traceability:** REQ-1701.
  **Validation:** `dotnet build src\AIProxyPlus.AppHost\AIProxyPlus.AppHost.csproj --no-restore` passed;
  `dotnet test tests\AIProxyPlus.ArchitectureTests\AIProxyPlus.ArchitectureTests.csproj --no-restore` passed (9 tests).
  `dotnet run --project src\AIProxyPlus.AppHost --no-build --launch-profile wslc` started successfully using the
  persisted path override and WSLC 3.0.5.0. Postgres, Vault, and Keycloak ran with loopback-only published ports;
  restarting AppHost reused the same three persistent container IDs. `/health/live`, `/health/ready`, and Keycloak
  realm discovery returned HTTP 200 after restart. Pre-existing Aspire and MessagePack vulnerability warnings
  remain; no application dependency upgrades, Compose/Testcontainers runtime migration, or cloud deployment were
  performed.

## Dependency security follow-up (2026-11-13)

- [x] **T17-307 — Clear reported NuGet vulnerability warnings**
  Align the AppHost SDK and Aspire integration packages at 13.6.5 to fix the dashboard security-feature bypass
  (CVE-2026-118740) and hosting elevation of privilege (CVE-2026-120006). This also resolves MessagePack 2.5.307,
  fixing CVE-2026-92707 without a direct override. Upgrade Testcontainers and its PostgreSQL integration to 4.14.0,
  which carries SSH.NET 2026.0.0 and fixes CVE-2026-48798/CVE-2026-85756 through the supported parent dependency.
  Pin WebUtilities 2.3.14 only in the API test project to fix CVE-2026-118750 in WireMock's legacy transitive chain.
  Retain the standalone DCP 0.27.4 override and WSLC launch profile; update the README and architecture version
  references without changing topology or suppressing vulnerability warnings.
  **Depends on:** T17-306.
  **Traceability:** REQ-1701, REQ-1708.
  **Validation:** `dotnet restore AIProxyPlus.sln` and `dotnet build AIProxyPlus.sln --no-restore` passed.
  `dotnet list AIProxyPlus.sln package --vulnerable --include-transitive --no-restore` reported no vulnerable packages
  in all 11 projects; GitHub advisory re-scans of the six affected resolved dependencies were also clean.
  `dotnet test AIProxyPlus.sln --no-build --no-restore --filter "FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Persistence&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.VaultSecretResolverTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.ConsumerAuthenticatorEndToEndTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.PostgresEndpointCredentialInvalidationTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Usage.UsageRecordBackgroundWriterTests"`
  passed 485 tests, including all 9 architecture tests and all 139 API tests. Docker-API-dependent infrastructure
  tests were excluded because WSLC does not expose that API; the user declined starting Podman.
  `dotnet run --project src\AIProxyPlus.AppHost --no-build --launch-profile wslc` started Aspire 13.6.5 with WSLC;
  API live/ready, Vault readiness, Keycloak readiness, and realm discovery returned HTTP 200. The first startup
  attempt timed out checking DCP; the unchanged command succeeded on retry after DCP health was verified.
  Non-vulnerability compiler/analyzer, Testcontainers obsolete-constructor, and optional CLI-bundle warnings remain.
  No frontend dependencies or cloud resources changed.

## PR #49 deployment review follow-up (2026-11-13)

- [x] **T17-804 — Preserve browser sessions and safe credential rollout**
  Share Data Protection metadata through PostgreSQL and protect key material through the configured secret store.
  Keep the fixed application name across replicas/revisions and retain old key references. Make OIDC bootstrap
  idempotent and rotation append-only; document explicit retirement after replacement sign-in, old-replica shutdown,
  and callback drain. Preserve optional local PostgreSQL parameters and existing user-secret overrides, remove
  retirement defaults masking the legacy section, and document `azd env new` for fresh checkouts.
  **Depends on:** T17-205, T17-303, T17-502, T17-601, T17-307.
  **Traceability:** REQ-1701.6, REQ-1703.8, REQ-1705, REQ-1706.11.
  **Validation:** `dotnet restore AIProxyPlus.sln --configfile NuGet.config` and
  `dotnet build src\AIProxyPlus.AppHost\AIProxyPlus.AppHost.csproj --no-restore` passed.
  `dotnet test AIProxyPlus.sln --no-build --no-restore --filter "FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Persistence&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.VaultSecretResolverTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.ConsumerAuthenticatorEndToEndTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.PostgresEndpointCredentialInvalidationTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Usage.UsageRecordBackgroundWriterTests"`
  passed 491 tests, including 9 architecture tests, 4 new key-ring tests, and both retirement compatibility cases.
  An initial run hit the existing 300 ms circuit-breaker timing assumption under concurrent startup load; its
  isolated rerun and the full filtered rerun passed without modifying the test.
  `pwsh -NoProfile -File tests\infra\postprovision.Tests.ps1` passed all five credential lifecycle scenarios.
  `az bicep build --file infra\main.bicep --stdout` passed. Aspire manifest generation retained required publish
  credentials and excluded local Vault/Keycloak. `dotnet ef migrations has-pending-model-changes --project
  src\AIProxyPlus.Infrastructure --no-build` reported no pending changes; the migration SQL was generated.
  The solution transitive vulnerability scan remained clean after aligning Data Protection/EF dependencies at
  10.0.14. WSLC startup returned HTTP 200 for API live/ready; an OIDC challenge returned 302 and the migrated
  PostgreSQL ring contained a secret-store reference with no plaintext master key. The existing volume initially
  rejected its configured password; explicit operator-approved local password synchronization preserved all data
  and restored authentication. The temporary authentication-rule change was restored and password login verified.
  After replacing the API process, readiness remained 200 and another OIDC challenge returned 302 using the
  same single persisted key. The final vulnerability audit used the installed .NET 10.0.403 CLI explicitly because
  a concurrently installed .NET 11 preview had an incomplete workload manifest.
  Docker-API-dependent fixtures remain excluded because WSLC does not expose that API. No Azure rollout or real
  Entra sign-in was performed; replacement-revision sign-in/drain remains an operator deployment check.

- [x] **T17-805 — Provision the Azure consumer pepper before API startup**
  Initialize the mapped Key Vault pepper through AZD postprovision and predeploy hooks, preserving every existing
  value. Require the pepper during Azure API startup without replica-side creation; keep local single-instance
  HashiCorp Vault auto-creation. Document serialization of deployments to one environment and pepper retention.
  **Depends on:** T17-804.
  **Traceability:** REQ-1705.12.
  **Validation:** Using the installed .NET 10.0.403 CLI,
  `dotnet test AIProxyPlus.sln --no-restore --filter "FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Persistence&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.VaultSecretResolverTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.ConsumerAuthenticatorEndToEndTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Secrets.PostgresEndpointCredentialInvalidationTests&FullyQualifiedName!~AIProxyPlus.Infrastructure.Tests.Usage.UsageRecordBackgroundWriterTests"`
  passed 495 tests, including all 9 architecture tests. Focused hasher and configuration classes passed 11 and 7
  tests respectively. New cases verify missing Azure peppers fail startup without writes, replicas/restarts reuse
  the same pepper, and existing local first-use creation remains supported.
  `pwsh -NoProfile -File tests\infra\initialize-consumer-pepper.Tests.ps1` passed four bootstrap scenarios, including
  idempotent rerun, mapped naming, metadata errors, and write failures.
  `pwsh -NoProfile -File tests\infra\postprovision.Tests.ps1` passed all five OIDC lifecycle scenarios with the added
  bootstrap invocation. AZD configuration includes both deployment and provisioning bootstrap hooks.
  Docker-API-dependent fixtures remain excluded for WSLC. No Azure deployment or secret mutation was performed.
