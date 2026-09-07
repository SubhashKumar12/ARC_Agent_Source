// ARC Blob Storage - Evidence, Legal Documents

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object
param legalRetentionDays int = 2920 // 8 years default
param legalWormEnabled bool = true

// Storage account names must be 3-24 chars, lowercase alphanumeric only (no hyphens).
var storageAccountName = toLower('${replace(resourceNamePrefix, '-', '')}st${uniqueSuffix}')

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: length(storageAccountName) > 24 ? substring(storageAccountName, 0, 24) : storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS' // Dev tier - change to ZRS/GRS for production
  }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true // Required for function bindings in dev - restrict in production
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow' // Change to 'Deny' with private endpoint in production
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// Evidence container - standard retention
resource evidenceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'evidence'
  properties: {
    publicAccess: 'None'
  }
}

// Legal container - WORM via container-level time-based retention.
// Container-level time-based immutability and version-level immutability are mutually exclusive;
// this module uses the container-level policy only. The policy is created Unlocked so DEV can be
// cleaned up; it must be Locked by Legal sign-off before UAT/PRODUCTION.
resource legalContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'legal-worm'
  properties: {
    publicAccess: 'None'
  }
}

// Immutability policy for legal container (time-based retention, Unlocked in DEV)
resource legalImmutabilityPolicy 'Microsoft.Storage/storageAccounts/blobServices/containers/immutabilityPolicies@2023-01-01' = if (legalWormEnabled) {
  parent: legalContainer
  name: 'default'
  properties: {
    immutabilityPeriodSinceCreationInDays: legalRetentionDays
    allowProtectedAppendWrites: false
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output storageBlobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output evidenceContainerName string = evidenceContainer.name
output legalContainerName string = legalContainer.name
