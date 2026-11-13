# Iteration 17 — Azure Container Apps Deployment with AZD and Aspire

Companion to [the architecture specification](../../architecture/architecture.md). This document defines the
target design; it does not mark any cloud capability as implemented.

## Goal

Make the existing Aspire solution deployable with `azd up` to Azure Container Apps while preserving the current
local developer experience:

- local runs continue to use containerized PostgreSQL, HashiCorp Vault, and Keycloak;
- Azure runs use PostgreSQL Flexible Server, Azure Key Vault, and Microsoft Entra ID;
- `AIProxyPlus.AppHost` remains the topology source for both environments;
- Azure infrastructure is represented by Bicep under `infra/` and orchestrated by Azure Developer CLI.

## Scope

### In scope

- One externally accessible Azure Container App containing the ASP.NET Core API and bundled React SPA.
- Azure Container Registry for the application image.
- Azure Database for PostgreSQL Flexible Server with private connectivity.
- Azure Key Vault as the cloud implementation of `ISecretResolver`.
- A system-assigned managed identity for the Container App.
- Microsoft Entra ID configuration for browser OIDC login through the existing BFF.
- Log Analytics, Application Insights, and OpenTelemetry wiring.
- Environment-aware AppHost topology and AZD/Bicep artifacts.
- Automated validation for local behavior and generated cloud artifacts.

### Non-goals

- Deploying resources as part of this iteration's specification work.
- Moving the SPA to Static Web Apps or a separate origin.
- Replacing provider-neutral OIDC code with Microsoft.Identity.Web.
- Entra group/role authorization for frontend administrators.
- Converting Consumer API-key authentication to Entra.
- Removing HashiCorp Vault or Keycloak from local development.
- Cross-protocol translation, quotas, rate limiting, or unrelated proxy behavior.
- Automatic creation of model-provider endpoints or their credentials.

## Requirements

### REQ-1701 — Dual-environment Aspire topology

`AIProxyPlus.AppHost` SHALL define both local and publish behavior without requiring developers to edit source or
configuration when switching environments.

1. Local execution SHALL start PostgreSQL, HashiCorp Vault, Keycloak, and the API as it does today.
2. Publish mode SHALL omit local Vault and Keycloak from the deployment manifest.
3. Publish mode SHALL describe PostgreSQL Flexible Server, Azure Key Vault, and the API dependency graph.
4. Resource names and configuration SHALL flow through Aspire references or explicit environment variables; cloud
   endpoints SHALL NOT be hardcoded in application code.
5. The application SHALL keep a single production ingress origin so OIDC cookies, antiforgery, API routes,
   WebSockets, and SPA fallback retain their current semantics.
6. Local PostgreSQL credentials SHALL be optional, using Aspire defaults when absent and preserving supplied
   AppHost parameters for existing persistent volumes. Publish mode SHALL require both deployment credentials.

### REQ-1702 — AZD and Bicep deployment contract

The repository SHALL support a clean deployment from the repository root.

1. `azure.yaml` SHALL be generated from the AppHost with `azd init --from-code`; it SHALL NOT be independently
   hand-authored.
2. Deployable Bicep SHALL live under `infra/`.
3. `azd provision`, `azd package`, and `azd deploy` SHALL be non-interactive after environment values are supplied.
4. Infrastructure parameters SHALL cover environment name, location, PostgreSQL SKU/storage/backup retention,
   Container App scaling, Entra tenant/client configuration, and optional user-assigned identity settings if added.
5. Bicep outputs SHALL expose only non-secret operational values such as application URL, Key Vault URI, PostgreSQL
   host, and resource names.
6. A new environment SHALL be deployable with `azd up` after the documented prerequisites are satisfied.

### REQ-1703 — Container Apps hosting

1. The API and compiled SPA SHALL be built into one Linux image from the existing multi-stage Dockerfile or an
   equivalently verified Aspire container build.
2. The Container App SHALL expose port 8080 over external HTTPS ingress.
3. HTTP traffic SHALL redirect to HTTPS at the platform or application boundary.
4. Liveness SHALL use `/health/live`; readiness SHALL use `/health/ready`.
5. The initial scale SHALL be parameterized with a safe default of one minimum replica and three maximum replicas.
6. Revisions SHALL use single-revision mode unless a later rollout requirement deliberately changes this.
7. WebSocket support SHALL remain enabled for Realtime ingress.
8. Replicas and replacement revisions SHALL share a durable Data Protection ring in PostgreSQL with application
   name `AIProxyPlus`; secret key material SHALL remain in the configured secret store, referenced immutably from
   ring metadata. Store failures SHALL not fall back to replica-local keys. Both stores SHALL be backed up together.

### REQ-1704 — PostgreSQL Flexible Server

1. Azure SHALL use PostgreSQL Flexible Server, not a PostgreSQL container.
2. The database SHALL use private network access from the Container Apps environment, with private DNS resolution.
3. Public PostgreSQL access SHALL be disabled.
4. TLS SHALL be required by the application connection string.
5. Database name and runtime/migration connection strings SHALL retain the existing
   `aiproxyplusdb`/`aiproxyplusdb-migrations` configuration contract.
6. Credentials SHALL be generated or supplied as secure deployment values and SHALL NOT appear in Bicep outputs,
   source, logs, or plain Container App environment variables.
7. Startup migration behavior SHALL be deterministic for multiple replicas. The initial implementation MAY retain
   startup migration with one minimum replica, but scaling beyond one SHALL require a migration job or equivalent
   single-owner mechanism.
8. SKU, storage, high availability, availability zone, backup retention, and geo-redundant backup SHALL be
   parameterized so development and production environments can differ without template edits.

### REQ-1705 — Configurable secret-store provider

The existing application-layer `ISecretResolver` SHALL remain the only secret-store port.

1. `Secrets:Provider` SHALL select `HashiCorpVault` or `AzureKeyVault`.
2. Local defaults SHALL select HashiCorp Vault and preserve the existing VaultSharp behavior.
3. Azure defaults SHALL select Azure Key Vault and use `Azure.Security.KeyVault.Secrets`.
4. Unsupported provider values and missing required provider configuration SHALL fail startup with a clear,
   non-secret-bearing error.
5. Azure-hosted authentication SHALL use `ManagedIdentityCredential` (system-assigned by default); local Azure Key
   Vault development MAY use `DefaultAzureCredential`.
6. The Container App identity SHALL receive only the Key Vault data-plane permissions needed to get, set, and
   delete secrets.
7. Secret references persisted in PostgreSQL SHALL remain opaque, immutable application references. The Azure
   adapter SHALL map them deterministically to Key Vault-compatible secret names and SHALL avoid collisions.
8. Missing secrets SHALL return `null`; authorization, transport, throttling, and service failures SHALL surface.
9. Logs SHALL include only the opaque reference or mapped name, never secret values.
10. Health readiness SHALL report a provider-neutral `secretStore` component rather than `vault`.
11. Retirement configuration SHALL move to provider-neutral `Secrets:Retirement` keys, with a documented
    compatibility window for existing `Vault:Retirement` configuration.

### REQ-1706 — Microsoft Entra browser identity

1. The existing ASP.NET Core cookie + OIDC BFF SHALL remain the browser authentication architecture.
2. Azure configuration SHALL set the OIDC authority to
   `https://login.microsoftonline.com/{tenantId}/v2.0`.
3. The Entra application SHALL be a confidential web client with redirect URI
   `https://{containerAppFqdn}/signin-oidc` and front-channel logout URI compatible with
   `/signout-callback-oidc`.
4. The client secret or certificate SHALL be supplied through a Container App secret/Key Vault reference and SHALL
   never be exposed to frontend JavaScript.
5. `openid` and `profile` SHALL be requested; `preferred_username` SHALL remain the default display-name claim.
6. Issuer, audience, nonce, correlation, state, signature algorithm, and HTTPS metadata validation SHALL remain
   enabled.
7. OIDC callback paths, cookie security, same-origin checks, and antiforgery protections SHALL preserve current
   behavior.
8. Bicep SHALL use the
   [Microsoft Graph Bicep extension](https://learn.microsoft.com/graph/templates/bicep/overview-bicep-templates-for-graph)
   to create the Entra application and service principal by default, with a parameterized option to use an existing
   registration.
9. The deployment identity SHALL have the documented Microsoft Graph permissions required to create and update
   those tenant-level objects. Resource-group Contributor rights alone are not sufficient.
10. Because Graph Bicep does not support application `passwordCredentials`, credential creation SHALL use an
    idempotent, separately permissioned bootstrap step that writes the returned secret directly to Key Vault without
    exposing it in deployment outputs or logs. The implementation MAY instead add certificate-based confidential
    client authentication if the OIDC composition root is extended and tested for it.
11. Provisioning SHALL reuse an existing OIDC secret. Explicit rotation SHALL append a credential without deleting
    old credentials. Retirement SHALL be an operator-owned post-rollout step after new-revision sign-in validation,
    old-replica shutdown, and draining in-flight authentication; rollback SHALL preserve the previous credential.

### REQ-1707 — Azure observability

1. The Container Apps environment SHALL send platform logs to Log Analytics.
2. Application Insights SHALL be workspace-based.
3. Existing OpenTelemetry instrumentation SHALL export to the Azure monitor path selected by the implementation
   without removing generic OTLP support.
4. Logs and telemetry SHALL not include consumer keys, endpoint credentials, OIDC client secrets, authorization
   headers, or browser query-string credentials.
5. Diagnostic settings and retention SHALL be parameterized where the target resource supports them.

### REQ-1708 — Security baseline

1. Key Vault SHALL use RBAC authorization, soft delete, purge protection, and TLS 1.2 or newer.
2. The application SHALL use managed identity for Key Vault; no Azure service-principal secret SHALL be stored in
   application configuration.
3. PostgreSQL SHALL be isolated through delegated subnet/private DNS networking.
4. Container Registry administrative credentials SHALL be disabled; image pull SHALL use managed identity/RBAC.
5. The Container App SHALL run as a non-root user. If the current image does not satisfy this, the Dockerfile SHALL
   be updated and verified.
6. All secret Bicep parameters SHALL use `@secure()` and SHALL not be returned as outputs.
7. Role assignments SHALL be scoped to the narrowest resource and use stable GUID names.
8. Local development tokens and passwords SHALL never be reused in Azure.

### REQ-1709 — Documentation and operability

1. `README.md` SHALL document local startup and Azure prerequisites without conflating the two.
2. Documentation SHALL cover `azd auth login`, `azd env` values, Entra redirect URIs, `azd up`, verification, and
   teardown.
3. The architecture specification SHALL be updated to describe configurable secret stores and Azure deployment
   topology without making the vendor-neutral core Azure-dependent.
4. A deployment diagram source and exported image SHALL show Container Apps, PostgreSQL, Key Vault, Entra,
   Container Registry, networking, and observability.
5. Troubleshooting SHALL cover OIDC redirect mismatch, Key Vault RBAC propagation, PostgreSQL DNS/TLS, migrations,
   unhealthy readiness, and stale local volumes.

## Target topology

```text
Internet
   |
   v
Azure Container Apps external HTTPS ingress
   |
   v
AIProxyPlus API + React SPA (system-assigned managed identity)
   |                    |                         |
   | private TLS        | managed identity        | OIDC redirects
   v                    v                         v
PostgreSQL Flexible   Azure Key Vault         Microsoft Entra ID
Server                (endpoint/admin          (confidential web app)
                      secrets + pepper)
   |
Private DNS + delegated subnet

Container image: Azure Container Registry
Telemetry: Application Insights + Log Analytics
```

## Configuration contract

| Setting | Local value | Azure value |
|---------|-------------|-------------|
| `Secrets:Provider` | `HashiCorpVault` | `AzureKeyVault` |
| `Vault:Address` | Aspire Vault endpoint | unset |
| `Vault:Token` | development-only token | unset |
| `AzureKeyVault:VaultUri` | optional developer vault | provisioned vault URI |
| `AzureKeyVault:UseManagedIdentity` | `false` | `true` |
| `Authentication:Oidc:Authority` | local Keycloak realm | Entra tenant v2 authority |
| `Authentication:Oidc:ClientId` | local client ID | Entra application client ID |
| `Authentication:Oidc:ClientSecret` | Aspire secret parameter | Container App secret reference |
| `ConnectionStrings:aiproxyplusdb` | Aspire PostgreSQL container | Flexible Server TLS connection |

## Architectural impact

- **Domain:** no changes.
- **Application:** keep `ISecretResolver`; update provider-neutral XML documentation only.
- **Infrastructure:** add `AzureKeyVaultSecretResolver`, provider options, deterministic reference-to-name mapping,
  and focused tests.
- **API:** select one secret-store adapter at composition time, make readiness provider-neutral, and validate
  configuration.
- **AppHost:** separate local-only resources from publish resources while preserving one dependency graph.
- **Web:** no authentication implementation change; only the server-side OIDC provider configuration changes.

This preserves dependency inversion: Azure SDK types remain in Infrastructure/Api composition and never enter
Domain or Application.

## Acceptance scenarios

1. **Local parity:** `dotnet run --project src/AIProxyPlus.AppHost` starts PostgreSQL, Vault, Keycloak, and the API;
   frontend login and readiness continue to work.
2. **Manifest parity:** Aspire publish output contains the API and Azure resources but no Keycloak or HashiCorp
   Vault container.
3. **Secret round trip:** the Azure adapter stores, resolves, rotates, and deletes a secret without logging its
   value; missing secret returns `null`.
4. **Cloud login:** a user signs in through Entra and reaches the SPA through the existing BFF cookie flow.
5. **Cloud readiness:** `/health/ready` reports healthy `postgres` and `secretStore` checks.
6. **Persistence:** configuration survives Container App revision replacement and scale events.
7. **Realtime:** a WebSocket connection reaches the Container App and preserves current authentication behavior.
8. **Fresh deployment:** after prerequisites and environment values are supplied, `azd up` provisions and deploys
   without manual portal edits.
9. **No secret exposure:** repository scan, Bicep outputs, Container App environment, logs, and API responses contain
   no plaintext deployment or endpoint credentials.

## Open decisions before implementation

1. Azure subscription and deployment region.
2. Development versus production PostgreSQL SKU, HA, and backup retention.
3. Graph Bicep-managed versus existing Entra registration, plus password bootstrap versus certificate-based client
   authentication. Graph Bicep manages the application/service principal in the default path but cannot directly
   populate `passwordCredentials`.
4. Initial replica minimum; two or more replicas require a single-owner migration mechanism in the same iteration.
5. Whether Key Vault should use a private endpoint in the first release. Managed identity and RBAC are mandatory
   either way; public network access, if temporarily retained, must be explicitly justified and restricted.
