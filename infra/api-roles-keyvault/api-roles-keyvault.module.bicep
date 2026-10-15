param keyvault_outputs_name string

param principalId string

resource keyvault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyvault_outputs_name
}

resource keyvault_KeyVaultSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyvault.id, principalId, subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'))
  properties: {
    principalId: principalId
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7')
    principalType: 'ServicePrincipal'
  }
  scope: keyvault
}