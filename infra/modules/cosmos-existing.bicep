// ARC Cosmos DB - REUSE of an existing DEV account (no account-level changes)
//
// Phase 13B: rg-arc-dev already contains 'arc-dev-cosmos-subhash' (serverless, Continuous backup,
// local auth disabled). This module intentionally does NOT declare the databaseAccount resource so
// that account-level properties (capabilities/freeTier/backup) are never replaced or downgraded.
//
// Serverless accounts must NOT specify container throughput options.

param existingAccountName string
param databaseName string = 'arc'

@description('Partition key for the state/checkpoint/audit containers (existing DEV layout).')
param statePartitionKeyPath string = '/cycleId'

@description('Create the documents container with a vector embedding policy. Requires EnableNoSQLVectorSearch on the account.')
param enableVectorDocuments bool = false

@description('Embedding dimensions. Assignment requirement is 3072 - do not change.')
param vectorDimensions int = 3072

@description('Partition key for the documents container when vector search is enabled.')
param documentsPartitionKeyPath string = '/documentType'

var stateIndexingPolicy = {
  indexingMode: 'consistent'
  automatic: true
  includedPaths: [
    {
      path: '/*'
    }
  ]
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: existingAccountName
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// Checkpoints - 90 day TTL (assignment requirement)
resource checkpointsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'checkpoints'
  properties: {
    resource: {
      id: 'checkpoints'
      partitionKey: {
        paths: [statePartitionKeyPath]
        kind: 'Hash'
      }
      indexingPolicy: stateIndexingPolicy
      defaultTtl: 7776000
    }
  }
}

// Cycle state
resource cycleStateContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'cycleState'
  properties: {
    resource: {
      id: 'cycleState'
      partitionKey: {
        paths: [statePartitionKeyPath]
        kind: 'Hash'
      }
      indexingPolicy: stateIndexingPolicy
    }
  }
}

// Audit events - 8 year TTL (assignment requirement)
resource auditContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'auditEvents'
  properties: {
    resource: {
      id: 'auditEvents'
      partitionKey: {
        paths: [statePartitionKeyPath]
        kind: 'Hash'
      }
      indexingPolicy: stateIndexingPolicy
      defaultTtl: 252288000
    }
  }
}

// Conversation state
resource conversationContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: cosmosDatabase
  name: 'conversationState'
  properties: {
    resource: {
      id: 'conversationState'
      partitionKey: {
        paths: [statePartitionKeyPath]
        kind: 'Hash'
      }
      indexingPolicy: stateIndexingPolicy
    }
  }
}

// Documents - vector container. Vector policy can only be set at container creation time.
resource documentsVectorContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = if (enableVectorDocuments) {
  parent: cosmosDatabase
  name: 'documents'
  properties: {
    resource: {
      id: 'documents'
      partitionKey: {
        paths: [documentsPartitionKeyPath]
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
          {
            path: '/embedding/*'
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
            dimensions: vectorDimensions
          }
        ]
      }
    }
  }
}

// Documents - non-vector fallback when the account lacks EnableNoSQLVectorSearch.
resource documentsPlainContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = if (!enableVectorDocuments) {
  parent: cosmosDatabase
  name: 'documents'
  properties: {
    resource: {
      id: 'documents'
      partitionKey: {
        paths: [statePartitionKeyPath]
        kind: 'Hash'
      }
      indexingPolicy: stateIndexingPolicy
    }
  }
}

output cosmosAccountName string = cosmosAccount.name
output cosmosAccountId string = cosmosAccount.id
output cosmosAccountEndpoint string = cosmosAccount.properties.documentEndpoint
output cosmosDatabaseName string = cosmosDatabase.name
output vectorDocumentsEnabled bool = enableVectorDocuments
