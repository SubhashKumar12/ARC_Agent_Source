// ARC Key Vault

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${resourceNamePrefix}-kv-${uniqueSuffix}'
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true // Use RBAC instead of access policies
    enableSoftDelete: true
    softDeleteRetentionInDays: 7 // Minimum for dev
    enablePurgeProtection: true // Required by subscription policy; irreversible once enabled
  }
}

output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
