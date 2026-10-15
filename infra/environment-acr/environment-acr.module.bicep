@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource environment_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: take('environmentacr${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
  }
  tags: {
    'aspire-resource-name': 'environment-acr'
  }
}

output name string = environment_acr.name

output loginServer string = environment_acr.properties.loginServer

output id string = environment_acr.id