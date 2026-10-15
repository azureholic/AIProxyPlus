# Azure Deployment Plan

> **Status:** Planning

Generated: 2026-10-15

---

## 1. Project Overview

**Goal:** Prepare the existing AIProxyPlus Aspire solution for repeatable `azd up` deployment to Azure while
preserving its current local-development topology.

**Path:** Modernize Existing

The implementation is specified by
[`specs/iterations/iteration-17/specification.md`](../specs/iterations/iteration-17/specification.md) and decomposed
in [`specs/iterations/iteration-17/tasks.md`](../specs/iterations/iteration-17/tasks.md). This plan remains in
Planning until that iteration is approved for implementation.

---

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Production-ready baseline |
| Scale | Small, horizontally scalable API |
| Budget | Balanced |
| Subscription | Select and confirm before implementation |
| Location | Select and confirm before implementation; must support Container Apps and PostgreSQL Flexible Server |

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

Quota and availability checks are intentionally deferred until a subscription and region are selected for the
implementation/deployment phase.

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
- [ ] Approve Iteration 17 for implementation.
- [ ] Confirm Azure subscription and location.
- [ ] Check regional availability and subscription quotas.

### Implementation

- [ ] Add configurable HashiCorp Vault/Azure Key Vault adapters.
- [ ] Refactor AppHost into explicit local and publish topologies.
- [ ] Generate AZD artifacts using `azd init --from-code`.
- [ ] Customize generated Bicep under `infra/`.
- [ ] Configure Entra OIDC inputs and redirect URIs.
- [ ] Add tests and synchronize architecture, diagrams, and README.
- [ ] Set this plan to `Ready for Validation`.
- [ ] Invoke the `azure-validate` workflow before any deployment.

### Deployment

- [ ] Run only after validation and explicit deployment intent.
- [ ] Use `azd up` through the Azure deployment workflow.
- [ ] Verify frontend login, health, database migrations, Key Vault operations, and an authenticated proxy request.

---

## 8. Security Decisions

- Azure Key Vault uses RBAC, soft delete, purge protection, and no static application credential.
- The Container App uses a managed identity with the narrow Key Vault data-plane role required by the
  read/write/delete `ISecretResolver` contract.
- PostgreSQL is reachable privately from the Container Apps environment; public database access is disabled.
- OIDC remains a same-origin BFF flow. Entra client credentials are never exposed to the React application.
- Plaintext endpoint and consumer pepper secrets never enter Bicep outputs, logs, PostgreSQL, or source control.
- Local HashiCorp Vault and Keycloak resources are excluded from the cloud manifest.

## 9. Blocking Decisions Before Implementation

1. Select the Azure subscription and a supported region.
2. Authorize the deployment identity to create the Entra application/service principal through the Microsoft Graph
   Bicep extension and choose the client-credential bootstrap method.
3. Confirm the initial PostgreSQL SKU and backup-retention requirements.
4. Confirm whether production must start at one replica or two; database migration ownership must remain
   deterministic either way.
