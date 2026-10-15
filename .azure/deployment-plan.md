# Azure Deployment Plan

> **Status:** Validated

Generated: 2026-10-15

---

## 1. Project Overview

**Goal:** Prepare the existing AIProxyPlus Aspire solution for repeatable `azd up` deployment to Azure while
preserving its current local-development topology.

**Path:** Modernize Existing

The implementation is specified by
[`specs/iterations/iteration-17/specification.md`](../specs/iterations/iteration-17/specification.md) and decomposed
in [`specs/iterations/iteration-17/tasks.md`](../specs/iterations/iteration-17/tasks.md). The iteration was approved
by the request to execute its task graph.

---

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Production-ready baseline |
| Scale | Small, horizontally scalable API |
| Budget | Balanced |
| Subscription | `azureholic-demo` (`a430e454-7c45-45f6-b037-9d0a9bfe6326`) |
| Tenant | `1d3fe57e-e849-4872-abee-c41ab915ef28` |
| Location | Poland Central (`polandcentral`) |

---

## 3. Components Detected

| Component | Type | Technology | Path |
|-----------|------|------------|------|
| API + bundled SPA | Web/API | ASP.NET Core 10, React 19 | `src/AIProxyPlus.Api`, `src/AIProxyPlus.Web` |
| AppHost | Orchestrator | .NET Aspire 13 | `src/AIProxyPlus.AppHost` |
| Persistence | Database | EF Core 10, Npgsql, PostgreSQL | `src/AIProxyPlus.Infrastructure/Persistence` |
| Secret store | Infrastructure adapter | HashiCorp Vault through `ISecretResolver` | `src/AIProxyPlus.Infrastructure/Secrets` |
| Browser identity | Authentication | ASP.NET Core OIDC BFF | `src/AIProxyPlus.Api/Authentication` |

---

## 4. Recipe Selection

**Selected:** AZD + Aspire + Bicep

`AIProxyPlus.AppHost` remains the topology source. Initialization must use
`azd init --from-code -e <environment>` rather than a hand-authored `azure.yaml`. Generated/customized Bicep lives
under `infra/` and is validated as part of the iteration. No deployment command is in scope for specification work.

---

## 5. Architecture

**Stack:** Containers

### Service Mapping

| Component | Azure Service | Initial sizing |
|-----------|---------------|----------------|
| API + SPA | Azure Container Apps | 0.5 vCPU, 1 GiB, min 1, max 3 |
| Container images | Azure Container Registry | Basic |
| Database | Azure Database for PostgreSQL Flexible Server | Burstable development SKU by default; parameterized |
| Endpoint/admin secrets | Azure Key Vault | Standard, RBAC, soft delete, purge protection |
| Browser login | Microsoft Entra ID | Graph Bicep-managed application and service principal |

### Supporting Services

| Service | Purpose |
|---------|---------|
| Log Analytics | Container Apps platform and application logs |
| Application Insights | OpenTelemetry traces, metrics, and application monitoring |
| Managed identity | Key Vault access and optional Azure endpoint authentication |
| Virtual network/private DNS | Private PostgreSQL connectivity and workload isolation |

### Environment Matrix

| Concern | Local | Azure |
|---------|-------|-------|
| Orchestration | Aspire AppHost | Aspire manifest through AZD |
| Hosting | Local process/containers | Azure Container Apps |
| Database | PostgreSQL container | PostgreSQL Flexible Server |
| Secret store | HashiCorp Vault container | Azure Key Vault |
| Browser identity | Local Keycloak | Microsoft Entra ID |
| Workload credential | Developer/local configuration | Container App managed identity |

---

## 6. Provisioning Inventory

The selected subscription and Poland Central support the required resource types. `Microsoft.DBforPostgreSQL` was
registered during preparation. Capacity remains subject to Azure validation and provisioning-time availability.

| Resource type | Quantity |
|---------------|----------|
| `Microsoft.App/managedEnvironments` | 1 |
| `Microsoft.App/containerApps` | 1 |
| `Microsoft.ContainerRegistry/registries` | 1 |
| `Microsoft.DBforPostgreSQL/flexibleServers` | 1 |
| `Microsoft.KeyVault/vaults` | 1 |
| `Microsoft.OperationalInsights/workspaces` | 1 |
| `Microsoft.Insights/components` | 1 |
| `Microsoft.Network/virtualNetworks` | 1 |
| `Microsoft.Network/privateDnsZones` | 1 |

---

## 7. Execution Checklist

### Planning

- [x] Analyze workspace and architecture.
- [x] Identify local/cloud topology differences.
- [x] Select AZD + Aspire + Bicep recipe.
- [x] Write iteration specification and task breakdown.
- [x] Approve Iteration 17 for implementation.
- [x] Confirm Azure subscription and location.
- [x] Check regional availability and required provider registration.

### Implementation

- [x] Add configurable HashiCorp Vault/Azure Key Vault adapters.
- [x] Refactor AppHost into explicit local and publish topologies.
- [x] Generate AZD artifacts using `azd init --from-code`.
- [x] Customize generated Bicep under `infra/`.
- [x] Configure Entra OIDC inputs and redirect URIs.
- [x] Add focused tests and synchronize architecture, diagrams, and README.
- [x] Set this plan to `Ready for Validation`.
- [x] Invoke the `azure-validate` workflow before any deployment.

### Deployment

- [x] Run only after validation and explicit deployment intent.
- [x] Use the validated AZD/Aspire workflow to provision and deploy.
- [x] Verify frontend authentication wiring, health, database migrations, Key Vault operations, and authenticated request behavior.

### Azure validation steps

- [x] 1. AZD installation.
- [x] 2. `azure.yaml` schema validation.
- [x] 3. Environment setup.
- [x] 4. Authentication check.
- [x] 5. Subscription/location check.
- [x] 6. Aspire pre-provisioning checks (no Azure Functions resources).
- [x] 7. AZD provision preview.
- [x] 8. Build verification.
- [x] 9. Docker build-context validation.
- [x] 10. Package validation.
- [x] 11. Azure policy validation.
- [x] 12. Aspire post-provisioning checks.

### Validation proof

| Check | Command/evidence | Result |
|-------|------------------|--------|
| AZD schema | Azure MCP `validate_azure_yaml` on `azure.yaml` | Passed against stable schema |
| Environment | `azd env list`; `azd env get-values` | `aiproxyplus-dev`, target subscription, `polandcentral` |
| Azure context | `az account show` | Subscription and tenant match this plan |
| Bicep local compile | `az bicep build --file .\infra\main.bicep --stdout`; API module equivalent | Passed with no warnings |
| Azure control-plane validation | `az deployment sub validate ... --template-file .\infra\main.bicep` with ephemeral secure parameters | Passed |
| Policy review | Azure MCP `policy_assignment_list` | West Europe deny identified; target changed to compliant Poland Central |
| Aspire manifest | `dotnet run ... -- --publisher manifest --output-path <session>/aspire-manifest.json` | Passed; no local Keycloak/HashiCorp resources |
| AZD package | `azd package --all --no-prompt` | Passed |
| Backend | `dotnet test .\AIProxyPlus.sln --no-restore --verbosity minimal` | Passed: 531 tests |
| Frontend tests | `npm test` in `src/AIProxyPlus.Web` | Passed: 45 tests; one transient first-run failure passed focused and full reruns |
| Frontend build | `npm run build` in `src/AIProxyPlus.Web` | Passed |
| Image build | `docker build --file .\src\AIProxyPlus.Api\Dockerfile --tag aiproxyplus:iteration-17 .` | Passed |
| Image smoke | live/root requests plus `id -u` | HTTP 200; non-root UID 1654 |
| Local Aspire | AppHost run plus `/health/live`, `/health/ready`, `/` | HTTP 200; `secretStore` and `postgres` healthy |
| Security review | Focused changed-code review | No vulnerabilities found |
| Secret scan | `gitleaks git/dir --redact` | Only known false positive on C# property name `apiKey2`; no secret value |
| AZD preview | `azd provision --preview --no-prompt` | Passed in Poland Central |
| Azure provisioning | `azd provision --no-prompt` | Passed; PostgreSQL, Key Vault, ACR, Container Apps environment, monitoring, identities, and Entra resources provisioned |
| Application deployment | `azd deploy --no-prompt` | Passed; API deployed at `https://api.bluemoss-e600bc4d.polandcentral.azurecontainerapps.io/` |
| Container App topology | `az containerapp show`; revision list | Running in single-revision mode; latest revision receives 100% traffic; min 1/max 3 replicas; liveness and readiness probes target port 8080 |
| Cloud health and SPA | HTTPS requests to `/health/live`, `/health/ready`, and `/` | HTTP 200; `secretStore` and `postgres` healthy; SPA served |
| Entra configuration | Request to `/api/v1/auth/login`; Key Vault secret metadata | HTTP 302 to the selected tenant; `oidc-client-secret` exists and is enabled |
| Realtime boundary | WebSocket upgrade request without a credential | HTTP 401 with the expected missing-credential response |
| Telemetry | Application Insights query over the last 30 minutes | Request telemetry received |
| Live RBAC | `az role assignment list --all` for workload identities | API: Key Vault Secrets Officer/User; environment: AcrPull, all scoped to the intended resources |
| Browser identity | Integrated browser sign-in action | Deployed SPA displayed the Entra sign-in action and redirected to the configured tenant and application |
| Authenticated API behavior | Filtered `SessionAuthenticationTests` and `ModelConfigTests` | Passed: 17 tests covering authenticated session, configuration access, consumer provisioning, antiforgery, and configuration/secret lifecycle |
| Key Vault adapter behavior | Filtered `AzureKeyVaultSecretResolverTests` | Passed: 7 tests covering store/resolve mapping, missing secrets, authorization failures, deletion, and log redaction |
| Frontend authentication behavior | `npm test -- --run src/test/client.test.ts` | Passed: 9 tests, including the antiforgery-protected logout navigation |

No blocking deployment gaps remain. Interactive credential entry and MFA were intentionally not automated; the live
Entra redirect and cloud configuration are combined with authenticated session, configuration, secret-lifecycle, and
logout contract tests. A human user-acceptance sign-in remains recommended after tenant users are assigned according
to the organization's access policy.

---

## 8. Security Decisions

- Azure Key Vault uses RBAC, soft delete, purge protection, and no static application credential.
- The Container App uses a managed identity with the narrow Key Vault data-plane role required by the
  read/write/delete `ISecretResolver` contract.
- PostgreSQL is reachable privately from the Container Apps environment; public database access is disabled.
- OIDC remains a same-origin BFF flow. Entra client credentials are never exposed to the React application.
- Plaintext endpoint and consumer pepper secrets never enter Bicep outputs, logs, PostgreSQL, or source control.
- Local HashiCorp Vault and Keycloak resources are excluded from the cloud manifest.
- Key Vault public network access is retained for the first release so a non-network-integrated AZD deployment
  identity can bootstrap the Entra client credential before application deployment. Data-plane access remains
  identity/RBAC controlled; a private endpoint requires a network-integrated deployment runner.
- PostgreSQL migrations remain startup-owned but are serialized across replicas by a PostgreSQL advisory lock.
- Azure disables the local rolling-file sink and uses Container Apps console/Azure Monitor telemetry.

## 9. Resolved Implementation Decisions

1. Use subscription `a430e454-7c45-45f6-b037-9d0a9bfe6326` in Poland Central. West Europe is denied by an
   inherited tenant policy.
2. Create the Entra application/service principal through stable Microsoft Graph Bicep by default; accept
   `existingEntraClientId` for an existing registration. Bootstrap a one-year password credential after
   provisioning and write it directly to Key Vault before deploying the app.
3. Default PostgreSQL to `Standard_B1ms`, 32 GiB, seven-day retention, disabled HA/geo-redundancy; all are
   deployment parameters.
4. Default Container Apps to one minimum and three maximum replicas. Serialize startup migrations with a
   PostgreSQL advisory lock.
5. Retain Key Vault's public endpoint for initial AZD bootstrap, protected by Entra identity and RBAC. Private
   endpoint support is deferred until deployment runs from a network-integrated agent.

## 10. Role Assignment Verification

- **Status:** statically and live verified.
- **API identity:** Key Vault Secrets Officer on the application vault for get/set/delete operations; Key Vault
  Secrets User on the generated PostgreSQL credential vault; both assignments are resource-scoped.
- **Container Apps environment identity:** AcrPull on the specific registry.
- **AZD deployment principal:** Key Vault Secrets Officer on the application vault so the post-provision hook can
  store the Entra confidential-client credential.
- **Stable names:** every role assignment name is derived with `guid(resource, principal, role)`.
- **Live result:** the API identity has Key Vault Secrets Officer on the application vault and Key Vault Secrets User
  on the PostgreSQL credential vault; the Container Apps environment identity has AcrPull on the registry.
