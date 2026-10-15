targetScope = 'subscription'

extension microsoftGraphV1

@minLength(1)
@maxLength(64)
@description('Name of the environment that can be used as part of naming resource convention, the name of the resource group for your application will use this name, prefixed with rg-')
param environmentName string

@minLength(1)
@description('The location used for all deployed resources')
param location string

@description('Id of the user or app to assign application roles')
param principalId string = ''

@metadata({azd: {
  type: 'generate'
  config: {length:22}
  }
})
@secure()
param postgres_password string
@metadata({azd: {
  type: 'generate'
  config: {length:10,noNumeric:true,noSpecial:true}
  }
})
param postgres_username string

param postgresSkuName string = 'Standard_B1ms'
param postgresSkuTier string = 'Burstable'
param postgresStorageSizeGB int = 32
param postgresBackupRetentionDays int = 7
param postgresHighAvailabilityMode string = 'Disabled'

@description('Client ID of an existing Entra application. Leave empty to provision one through Microsoft Graph Bicep.')
param existingEntraClientId string = ''

var tags = {
  'azd-env-name': environmentName
}

resource rg 'Microsoft.Resources/resourceGroups@2022-09-01' = {
  name: 'rg-${environmentName}'
  location: location
  tags: tags
}

module api_identity 'api-identity/api-identity.module.bicep' = {
  name: 'api-identity'
  scope: rg
  params: {
    location: location
  }
}
module network 'network/network.module.bicep' = {
  name: 'network'
  scope: rg
  params: {
    location: location
    tags: tags
  }
}
module api_roles_keyvault 'api-roles-keyvault/api-roles-keyvault.module.bicep' = {
  name: 'api-roles-keyvault'
  scope: rg
  params: {
    keyvault_outputs_name: keyvault.outputs.name
    principalId: api_identity.outputs.principalId
  }
}
module api_roles_postgres_kv 'api-roles-postgres-kv/api-roles-postgres-kv.module.bicep' = {
  name: 'api-roles-postgres-kv'
  scope: rg
  params: {
    postgres_kv_outputs_name: postgres_kv.outputs.name
    principalId: api_identity.outputs.principalId
  }
}
module environment 'environment/environment.module.bicep' = {
  name: 'environment'
  scope: rg
  params: {
    environment_acr_outputs_name: environment_acr.outputs.name
    infrastructureSubnetId: network.outputs.containerAppsSubnetId
    location: location
  }
}
module environment_acr 'environment-acr/environment-acr.module.bicep' = {
  name: 'environment-acr'
  scope: rg
  params: {
    location: location
  }
}
module keyvault 'keyvault/keyvault.module.bicep' = {
  name: 'keyvault'
  scope: rg
  params: {
    deploymentPrincipalId: principalId
    location: location
  }
}
module postgres 'postgres/postgres.module.bicep' = {
  name: 'postgres'
  scope: rg
  params: {
    administratorLogin: postgres_username
    administratorLoginPassword: postgres_password
    backupRetentionDays: postgresBackupRetentionDays
    delegatedSubnetResourceId: network.outputs.postgresSubnetId
    highAvailabilityMode: postgresHighAvailabilityMode
    location: location
    privateDnsZoneArmResourceId: network.outputs.postgresPrivateDnsZoneId
    postgres_kv_outputs_name: postgres_kv.outputs.name
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    storageSizeGB: postgresStorageSizeGB
  }
}
module postgres_kv 'postgres-kv/postgres-kv.module.bicep' = {
  name: 'postgres-kv'
  scope: rg
  params: {
    location: location
  }
}

resource entraApplication 'Microsoft.Graph/applications@v1.0' = if (empty(existingEntraClientId)) {
  uniqueName: 'aiproxyplus-${environmentName}'
  displayName: 'AIProxyPlus ${environmentName}'
  signInAudience: 'AzureADMyOrg'
  web: {
    redirectUris: [
      'https://api.${environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN}/signin-oidc'
    ]
    logoutUrl: 'https://api.${environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN}/signout-callback-oidc'
  }
}

resource entraServicePrincipal 'Microsoft.Graph/servicePrincipals@v1.0' = if (empty(existingEntraClientId)) {
  appId: entraApplication!.appId
}
output API_IDENTITY_CLIENTID string = api_identity.outputs.clientId
output API_IDENTITY_ID string = api_identity.outputs.id
output APPLICATIONINSIGHTS_CONNECTION_STRING string = environment.outputs.APPLICATIONINSIGHTS_CONNECTION_STRING
output AZURE_ENTRA_AUTHORITY string = '${az.environment().authentication.loginEndpoint}${tenant().tenantId}/v2.0'
output AZURE_ENTRA_CLIENT_ID string = empty(existingEntraClientId) ? entraApplication!.appId : existingEntraClientId
output API_URL string = 'https://api.${environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN}'
output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = environment.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output ENVIRONMENT_AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN
output ENVIRONMENT_AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = environment.outputs.AZURE_CONTAINER_APPS_ENVIRONMENT_ID
output ENVIRONMENT_AZURE_CONTAINER_REGISTRY_ENDPOINT string = environment.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output ENVIRONMENT_AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = environment.outputs.AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID
output KEYVAULT_NAME string = keyvault.outputs.name
output KEYVAULT_VAULTURI string = keyvault.outputs.vaultUri
output POSTGRES_KV_NAME string = postgres_kv.outputs.name
output POSTGRES_KV_VAULTURI string = postgres_kv.outputs.vaultUri
output POSTGRES_HOSTNAME string = postgres.outputs.hostName
