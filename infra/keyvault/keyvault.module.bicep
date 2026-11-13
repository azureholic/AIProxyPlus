@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param deploymentPrincipalId string = ''

resource keyvault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: take('keyvault-${uniqueString(resourceGroup().id)}', 24)
  location: location
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }

    enableRbacAuthorization: true
    enablePurgeProtection: true
    enableSoftDelete: true
    publicNetworkAccess: 'Enabled'
    softDeleteRetentionInDays: 90
  }
  tags: {
    'aspire-resource-name': 'keyvault'
  }
}

resource deploymentPrincipalSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deploymentPrincipalId)) {
  name: guid(keyvault.id, deploymentPrincipalId, 'KeyVaultSecretsOfficer')
  scope: keyvault
  properties: {
    principalId: deploymentPrincipalId
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
  }
}

output vaultUri string = keyvault.properties.vaultUri

output name string = keyvault.name

output id string = keyvault.id