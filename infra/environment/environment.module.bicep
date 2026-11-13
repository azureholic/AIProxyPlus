@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param tags object = { }

param environment_acr_outputs_name string

param infrastructureSubnetId string

resource environment_mi 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('environment_mi-${uniqueString(resourceGroup().id)}', 128)
  location: location
  tags: tags
}

resource environment_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' existing = {
  name: environment_acr_outputs_name
}

resource environment_acr_environment_mi_AcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(environment_acr.id, environment_mi.id, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d'))
  properties: {
    principalId: environment_mi.properties.principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalType: 'ServicePrincipal'
  }
  scope: environment_acr
}

resource environment_law 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: take('environmentlaw-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
  }
  tags: tags
}

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: take('environment${uniqueString(resourceGroup().id)}', 24)
  location: location
  properties: {
    vnetConfiguration: {
      infrastructureSubnetId: infrastructureSubnetId
      internal: false
    }
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: environment_law.properties.customerId
        sharedKey: environment_law.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'consumption'
        workloadProfileType: 'Consumption'
      }
    ]
  }
  tags: tags
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: take('appi-${uniqueString(resourceGroup().id)}', 260)
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: environment_law.id
  }
  tags: tags
}

resource aspireDashboard 'Microsoft.App/managedEnvironments/dotNetComponents@2025-10-02-preview' = {
  name: 'aspire-dashboard'
  properties: {
    componentType: 'AspireDashboard'
  }
  parent: environment
}

output AZURE_LOG_ANALYTICS_WORKSPACE_NAME string = environment_law.name

output AZURE_LOG_ANALYTICS_WORKSPACE_ID string = environment_law.id

output AZURE_CONTAINER_REGISTRY_NAME string = environment_acr.name

output AZURE_CONTAINER_REGISTRY_ENDPOINT string = environment_acr.properties.loginServer

output AZURE_CONTAINER_REGISTRY_MANAGED_IDENTITY_ID string = environment_mi.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = environment.name

output AZURE_CONTAINER_APPS_ENVIRONMENT_ID string = environment.id

output AZURE_CONTAINER_APPS_ENVIRONMENT_DEFAULT_DOMAIN string = environment.properties.defaultDomain

output APPLICATIONINSIGHTS_CONNECTION_STRING string = applicationInsights.properties.ConnectionString