// ARC Azure SQL - Master Data, Ledger, Dealers, Cases
//
// Entra-only authentication: no SQL admin login/password is provisioned, so no secret needs to be
// generated, committed, or stored. The Entra admin is supplied as an object id by the deployer.

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object

@description('Entra (Azure AD) admin object id for the SQL logical server.')
param aadAdminObjectId string

@description('Display name for the Entra admin assignment.')
param aadAdminLogin string

@description('Entra tenant id for the SQL logical server admin.')
param aadTenantId string = subscription().tenantId

param databaseSku string = 'Basic'
param databaseMaxSizeBytes int = 2147483648

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: '${resourceNamePrefix}-sql-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled' // Change to 'Disabled' with private endpoint in production
    administrators: {
      administratorType: 'ActiveDirectory'
      principalType: 'User'
      login: aadAdminLogin
      sid: aadAdminObjectId
      tenantId: aadTenantId
      azureADOnlyAuthentication: true
    }
  }
}

// Allow Azure services (Functions, Container Apps with MI)
resource sqlServerFirewall 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: 'arc'
  location: location
  tags: tags
  sku: {
    name: databaseSku
    tier: databaseSku == 'Basic' ? 'Basic' : 'Standard'
  }
  properties: {
    maxSizeBytes: databaseMaxSizeBytes
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    catalogCollation: 'SQL_Latin1_General_CP1_CI_AS'
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: 'Local'
  }
}

output sqlServerName string = sqlServer.name
output sqlServerId string = sqlServer.id
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
