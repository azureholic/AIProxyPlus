@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

#disable-next-line no-unused-params
param environment_outputs_azure_container_apps_environment_default_domain string

param environment_outputs_azure_container_apps_environment_id string

param api_containerimage string

param api_identity_client_id_value string

param api_identity_outputs_id string

param postgres_kv_outputs_name string

#disable-next-line no-unused-params
param postgres_outputs_hostname string

#disable-next-line no-unused-params
param postgres_username_value string

@secure()
#disable-next-line no-unused-params
param postgres_password_value string

param keyvault_outputs_vaulturi string

param entra_authority_value string

param entra_client_id_value string

param keyvault_outputs_name string

param api_identity_outputs_clientid string

param environment_outputs_azure_container_registry_endpoint string

param environment_outputs_azure_container_registry_managed_identity_id string

param applicationinsights_connection_string_value string

@minValue(1)
param minReplicas int = 1

@minValue(1)
param maxReplicas int = 3

resource postgres_kv 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: postgres_kv_outputs_name
}

resource postgres_kv_connectionstrings__aiproxyplusdb 'Microsoft.KeyVault/vaults/secrets@2024-11-01' existing = {
  name: 'connectionstrings--aiproxyplusdb'
  parent: postgres_kv
}

resource keyvault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyvault_outputs_name
}

resource keyvault_oidc_client_secret 'Microsoft.KeyVault/vaults/secrets@2024-11-01' existing = {
  name: 'oidc-client-secret'
  parent: keyvault
}

resource api 'Microsoft.App/containerApps@2025-07-01' = {
  name: 'api'
  location: location
  properties: {
    configuration: {
      secrets: [
        {
          name: 'connectionstrings--aiproxyplusdb'
          identity: api_identity_outputs_id
          keyVaultUrl: postgres_kv_connectionstrings__aiproxyplusdb.properties.secretUri
        }
        {
          name: 'authentication--oidc--clientsecret'
          identity: api_identity_outputs_id
          keyVaultUrl: keyvault_oidc_client_secret.properties.secretUri
        }
      ]
      activeRevisionsMode: 'Single'
      ingress: {
        allowInsecure: false
        external: true
        targetPort: 8080
        transport: 'http'
      }
      registries: [
        {
          server: environment_outputs_azure_container_registry_endpoint
          identity: environment_outputs_azure_container_registry_managed_identity_id
        }
      ]
    }
    environmentId: environment_outputs_azure_container_apps_environment_id
    template: {
      containers: [
        {
          image: api_containerimage
          name: 'api'
          env: [
            {
              name: 'OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY'
              value: 'in_memory'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: applicationinsights_connection_string_value
            }
            {
              name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
              value: 'true'
            }
            {
              name: 'HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'ConnectionStrings__aiproxyplusdb'
              secretRef: 'connectionstrings--aiproxyplusdb'
            }
            {
              name: 'ConnectionStrings__aiproxyplusdb-migrations'
              secretRef: 'connectionstrings--aiproxyplusdb'
            }
            {
              name: 'ConnectionStrings__keyvault'
              value: keyvault_outputs_vaulturi
            }
            {
              name: 'KEYVAULT_URI'
              value: keyvault_outputs_vaulturi
            }
            {
              name: 'Secrets__Provider'
              value: 'AzureKeyVault'
            }
            {
              name: 'AzureKeyVault__UseManagedIdentity'
              value: 'true'
            }
            {
              name: 'AzureKeyVault__ManagedIdentityClientId'
              value: api_identity_client_id_value
            }
            {
              name: 'Logging__File__Enabled'
              value: 'false'
            }
            {
              name: 'Authentication__Oidc__Enabled'
              value: 'true'
            }
            {
              name: 'Authentication__Oidc__DisplayName'
              value: 'Microsoft Entra ID'
            }
            {
              name: 'Authentication__Oidc__Authority'
              value: entra_authority_value
            }
            {
              name: 'Authentication__Oidc__ClientId'
              value: entra_client_id_value
            }
            {
              name: 'Authentication__Oidc__ClientSecret'
              secretRef: 'authentication--oidc--clientsecret'
            }
            {
              name: 'Authentication__Oidc__Scopes__0'
              value: 'openid'
            }
            {
              name: 'Authentication__Oidc__Scopes__1'
              value: 'profile'
            }
            {
              name: 'Authentication__Oidc__UsernameClaim'
              value: 'preferred_username'
            }
            {
              name: 'Authentication__Oidc__GetClaimsFromUserInfo'
              value: 'false'
            }
            {
              name: 'Authentication__Oidc__CallbackPath'
              value: '/signin-oidc'
            }
            {
              name: 'Authentication__Oidc__SignedOutCallbackPath'
              value: '/signout-callback-oidc'
            }
            {
              name: 'Authentication__Oidc__SessionLifetimeMinutes'
              value: '480'
            }
            {
              name: 'Authentication__Oidc__AllowedSigningAlgorithms__0'
              value: 'RS256'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: api_identity_outputs_clientid
            }
            {
              name: 'AZURE_TOKEN_CREDENTIALS'
              value: 'ManagedIdentityCredential'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 15
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${api_identity_outputs_id}': { }
      '${environment_outputs_azure_container_registry_managed_identity_id}': { }
    }
  }
}