@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

param administratorLogin string

@secure()
param administratorLoginPassword string

param postgres_kv_outputs_name string

param delegatedSubnetResourceId string

param privateDnsZoneArmResourceId string

param skuName string = 'Standard_B1ms'

param skuTier string = 'Burstable'

@minValue(32)
param storageSizeGB int = 32

@minValue(7)
@maxValue(35)
param backupRetentionDays int = 7

@allowed([
  'Disabled'
  'ZoneRedundant'
  'SameZone'
])
param highAvailabilityMode string = 'Disabled'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: take('postgres-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    availabilityZone: '1'
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: highAvailabilityMode
    }
    network: {
      delegatedSubnetResourceId: delegatedSubnetResourceId
      privateDnsZoneArmResourceId: privateDnsZoneArmResourceId
      publicNetworkAccess: 'Disabled'
    }
    storage: {
      storageSizeGB: storageSizeGB
    }
    version: '16'
  }
  sku: {
    name: skuName
    tier: skuTier
  }
  tags: {
    'aspire-resource-name': 'postgres'
  }
}

resource requireSecureTransport 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  name: 'require_secure_transport'
  parent: postgres
  properties: {
    source: 'user-override'
    value: 'on'
  }
}

resource aiproxyplusdb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  name: 'aiproxyplusdb'
  parent: postgres
}

resource keyVault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: postgres_kv_outputs_name
}

resource connectionString 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'connectionstrings--postgres'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Username=${administratorLogin};Password=${administratorLoginPassword}'
  }
  parent: keyVault
}

resource aiproxyplusdb_connectionString 'Microsoft.KeyVault/vaults/secrets@2024-11-01' = {
  name: 'connectionstrings--aiproxyplusdb'
  properties: {
    value: 'Host=${postgres.properties.fullyQualifiedDomainName};Username=${administratorLogin};Password=${administratorLoginPassword};Database=aiproxyplusdb'
  }
  parent: keyVault
}

output name string = postgres.name

output id string = postgres.id

output hostName string = postgres.properties.fullyQualifiedDomainName