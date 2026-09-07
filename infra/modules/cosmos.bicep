// ARC Cosmos DB - Checkpoints, State, Audit, Conversation, Documents (Vector)

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object
param freeTier bool = true
param throughputMode string = 'manual'
param manualThroughput int = 400

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: '${resourceNamePrefix}-cosmos-${uniqueSuffix}'
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    enableFreeTier: freeTier
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableNoSQLVectorSearch'
      }
    ]
    disableKeyBasedMetadataWriteAccess: true // Enforce managed identity for data plane
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: 'arc'
  properties: {
    resource: {
      id: 'arc'
    }
  }
}

// Checkpoints container - partition key /cycleId
resource checkpointsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'checkpoints'
  properties: {
    resource: {
      id: 'checkpoints'
      partitionKey: {
        paths: ['/cycleId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
      defaultTtl: 7776000 // 90 days (assignment requirement)
    }
    options: throughputMode == 'manual' ? {
      throughput: manualThroughput
    } : {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

// Cycle State container - partition key /cycleId
resource cycleStateContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'cycleState'
  properties: {
    resource: {
      id: 'cycleState'
      partitionKey: {
        paths: ['/cycleId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
    options: throughputMode == 'manual' ? {
      throughput: manualThroughput
    } : {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

// Audit Events container - partition key /cycleId, 8-year retention
resource auditContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'auditEvents'
  properties: {
    resource: {
      id: 'auditEvents'
      partitionKey: {
        paths: ['/cycleId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
      defaultTtl: 252288000 // 8 years (assignment requirement)
    }
    options: throughputMode == 'manual' ? {
      throughput: manualThroughput
    } : {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

// Conversation State container - partition key /cycleId
resource conversationContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'conversationState'
  properties: {
    resource: {
      id: 'conversationState'
      partitionKey: {
        paths: ['/cycleId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
      }
    }
    options: throughputMode == 'manual' ? {
      throughput: manualThroughput
    } : {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

// Documents container - partition key /documentType, 3072-dim vector with DiskANN/cosine
resource documentsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'documents'
  properties: {
    resource: {
      id: 'documents'
      partitionKey: {
        paths: ['/documentType']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/documentType/?'
          }
          {
            path: '/status/?'
          }
          {
            path: '/documentCategory/?'
          }
          {
            path: '/regionScope/?'
          }
          {
            path: '/effectiveFrom/?'
          }
          {
            path: '/version/?'
          }
        ]
        excludedPaths: [
          {
            path: '/*'
          }
        ]
        vectorIndexes: [
          {
            path: '/embedding'
            type: 'diskANN'
          }
        ]
      }
      vectorEmbeddingPolicy: {
        vectorEmbeddings: [
          {
            path: '/embedding'
            dataType: 'float32'
            distanceFunction: 'cosine'
            dimensions: 3072 // Assignment requirement - do not change
          }
        ]
      }
    }
    options: throughputMode == 'manual' ? {
      throughput: manualThroughput
    } : {
      autoscaleSettings: {
        maxThroughput: 1000
      }
    }
  }
}

output cosmosAccountName string = cosmosAccount.name
output cosmosAccountId string = cosmosAccount.id
output cosmosAccountEndpoint string = cosmosAccount.properties.documentEndpoint
output cosmosDatabaseName string = cosmosDatabase.name
