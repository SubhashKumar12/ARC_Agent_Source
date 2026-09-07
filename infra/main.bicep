// ARC Azure DEV Infrastructure
//
// Phase 13B: deployed incrementally into an existing resource group (rg-arc-dev) that already
// contains a Cosmos DB account. Set existingCosmosAccountName to reuse it; the reuse path never
// declares the databaseAccount resource, so account-level settings cannot be replaced.
//
// Outbound must remain Shadow. No secret values are declared in this template.

targetScope = 'resourceGroup'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Environment name (dev, test, prod)')
param environment string = 'dev'

@description('Azure region for resources')
param location string = resourceGroup().location

@description('Project prefix for resource naming')
@minLength(2)
@maxLength(6)
param projectPrefix string = 'arc'

@description('Tags applied to all resources')
param tags object = {
  Project: 'ARC'
  Environment: environment
  ManagedBy: 'Bicep'
}

// Outbound safety
@description('Outbound run mode. Must remain Shadow until an explicit Live cutover decision.')
@allowed(['Shadow'])
param defaultRunMode string = 'Shadow'

// Staged deployment toggles (cost control / provider availability)
@description('Deploy Azure SQL logical server and database.')
param deploySql bool = true

@description('Deploy the Functions host.')
param deployFunctions bool = true

@description('Deploy the Container Apps environment and ARC.Api container app.')
param deployApi bool = true

@description('Deploy Service Bus. The Edureka_AZ AllowOnlyCoreServices policy does not permit Microsoft.ServiceBus/namespaces, so this is false for that subscription.')
param deployServiceBus bool = true

// AI Services (configuration placeholders - deployed separately)
@description('Azure OpenAI endpoint (empty = not configured)')
param azureOpenAiEndpoint string = ''

@description('Azure OpenAI embedding deployment name')
param embeddingDeployment string = ''

@description('Document Intelligence endpoint (empty = not configured)')
param documentIntelligenceEndpoint string = ''

// Cosmos DB
@description('Name of an existing Cosmos DB account to reuse. Empty = provision a new account.')
param existingCosmosAccountName string = ''

@description('Create the Cosmos documents container with a 3072-dim vector policy. Requires EnableNoSQLVectorSearch on the account.')
param enableCosmosVectorDocuments bool = false

@description('Cosmos DB free tier (new accounts only)')
param cosmosFreeTier bool = false

@description('Cosmos DB throughput mode (new accounts only)')
@allowed(['autoscale', 'manual'])
param cosmosThroughputMode string = 'manual'

@description('Cosmos DB manual throughput (new accounts only)')
param cosmosManualThroughput int = 400

// SQL - Entra-only authentication, no admin password
@description('Entra admin object id for the SQL logical server.')
param sqlAadAdminObjectId string = ''

@description('Entra admin display name for the SQL logical server.')
param sqlAadAdminLogin string = ''

@description('SQL Database SKU (Basic, S0, S1, etc.)')
param sqlDatabaseSku string = 'Basic'

@description('SQL Database max size in bytes (2GB for Basic)')
param sqlDatabaseMaxSizeBytes int = 2147483648

// Storage
@description('Blob storage legal container retention days (TBC with Legal)')
param legalBlobRetentionDays int = 2920 // 8 years

@description('Blob storage legal container WORM enabled')
param legalBlobWormEnabled bool = true

// Entra / API auth
@description('Entra JWT authority for ARC.Api.')
param apiJwtAuthority string = ''

@description('Entra JWT audience for ARC.Api.')
param apiJwtAudience string = ''

// Container Apps
@description('ARC.Api container image')
param apiContainerImage string = 'mcr.microsoft.com/dotnet/aspnet:9.0'

@description('API container CPU cores')
param apiCpuCores string = '0.5'

@description('API container memory')
param apiMemory string = '1.0Gi'

// ============================================================================
// VARIABLES
// ============================================================================

var resourceNamePrefix = '${projectPrefix}-${environment}'
var uniqueSuffix = uniqueString(resourceGroup().id, projectPrefix, environment)

var reuseCosmos = !empty(existingCosmosAccountName)

// Names are derived deterministically so conditional module outputs never need to be dereferenced.
var cosmosAccountNameResolved = reuseCosmos ? existingCosmosAccountName : '${resourceNamePrefix}-cosmos-${uniqueSuffix}'
var cosmosAccountEndpointResolved = 'https://${cosmosAccountNameResolved}.documents.azure.com:443/'
var sqlServerNameResolved = '${resourceNamePrefix}-sql-${uniqueSuffix}'
var sqlConnectionStringResolved = deploySql
  ? 'Server=tcp:${sqlServerNameResolved}${az.environment().suffixes.sqlServerHostname},1433;Database=arc;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;'
  : ''
var serviceBusNamespaceFqdnResolved = deployServiceBus
  ? '${resourceNamePrefix}-sb-${uniqueSuffix}.servicebus.windows.net'
  : ''

// ============================================================================
// MODULES
// ============================================================================

// 1. Log Analytics & Application Insights
module observability './modules/observability.bicep' = {
  name: 'observability-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    tags: tags
  }
}

// 2. Key Vault
module keyVault './modules/keyvault.bicep' = {
  name: 'keyvault-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
  }
}

// 3. Blob Storage
module storage './modules/storage.bicep' = {
  name: 'storage-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
    legalRetentionDays: legalBlobRetentionDays
    legalWormEnabled: legalBlobWormEnabled
  }
}

// 4a. Cosmos DB - reuse existing DEV account (no account-level changes)
module cosmosExisting './modules/cosmos-existing.bicep' = if (reuseCosmos) {
  name: 'cosmos-existing-deployment'
  params: {
    existingAccountName: existingCosmosAccountName
    enableVectorDocuments: enableCosmosVectorDocuments
  }
}

// 4b. Cosmos DB - greenfield account (not used when reusing)
module cosmosNew './modules/cosmos.bicep' = if (!reuseCosmos) {
  name: 'cosmos-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
    freeTier: cosmosFreeTier
    throughputMode: cosmosThroughputMode
    manualThroughput: cosmosManualThroughput
  }
}

// 5. Azure SQL (Entra-only authentication)
module sql './modules/sql.bicep' = if (deploySql) {
  name: 'sql-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
    aadAdminObjectId: sqlAadAdminObjectId
    aadAdminLogin: sqlAadAdminLogin
    databaseSku: sqlDatabaseSku
    databaseMaxSizeBytes: sqlDatabaseMaxSizeBytes
  }
}

// 6. Service Bus
module serviceBus './modules/servicebus.bicep' = if (deployServiceBus) {
  name: 'servicebus-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
  }
}

// 9. Azure Functions
module functions './modules/functions.bicep' = if (deployFunctions) {
  name: 'functions-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    uniqueSuffix: uniqueSuffix
    tags: tags
    storageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    cosmosAccountEndpoint: cosmosAccountEndpointResolved
    serviceBusNamespaceFqdn: serviceBusNamespaceFqdnResolved
    blobServiceUri: storage.outputs.storageBlobEndpoint
    sqlConnectionString: sqlConnectionStringResolved
    defaultRunMode: defaultRunMode
  }
}

// 10. Container Apps Environment
module containerAppsEnv './modules/containerapps-environment.bicep' = if (deployApi) {
  name: 'container-apps-env-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    tags: tags
    logAnalyticsWorkspaceId: observability.outputs.logAnalyticsWorkspaceId
  }
}

// 10. ARC.Api Container App
module apiContainerApp './modules/containerapps-api.bicep' = if (deployApi) {
  name: 'api-container-app-deployment'
  params: {
    location: location
    resourceNamePrefix: resourceNamePrefix
    tags: tags
    containerAppsEnvironmentId: containerAppsEnv!.outputs.containerAppsEnvironmentId
    containerImage: apiContainerImage
    cpuCores: apiCpuCores
    memory: apiMemory
    appInsightsConnectionString: observability.outputs.appInsightsConnectionString
    keyVaultName: keyVault.outputs.keyVaultName
    cosmosAccountEndpoint: cosmosAccountEndpointResolved
    serviceBusNamespaceFqdn: serviceBusNamespaceFqdnResolved
    blobServiceUri: storage.outputs.storageBlobEndpoint
    sqlConnectionString: sqlConnectionStringResolved
    defaultRunMode: defaultRunMode
    jwtAuthority: apiJwtAuthority
    jwtAudience: apiJwtAudience
  }
}

// ============================================================================
// 7. RBAC - Managed Identity Role Assignments
// ============================================================================

// Functions -> Cosmos DB
module functionsCosmosRbac './modules/rbac-cosmos.bicep' = if (deployFunctions) {
  name: 'functions-cosmos-rbac'
  params: {
    principalId: functions!.outputs.functionAppPrincipalId
    cosmosAccountName: cosmosAccountNameResolved
  }
  dependsOn: [
    cosmosExisting
    cosmosNew
  ]
}

// Functions -> SQL
module functionsSqlRbac './modules/rbac-sql.bicep' = if (deployFunctions && deploySql) {
  name: 'functions-sql-rbac'
  params: {
    principalId: functions!.outputs.functionAppPrincipalId
    sqlServerName: sqlServerNameResolved
  }
  dependsOn: [
    sql
  ]
}

// Functions -> Storage
module functionsStorageRbac './modules/rbac-storage.bicep' = if (deployFunctions) {
  name: 'functions-storage-rbac'
  params: {
    principalId: functions!.outputs.functionAppPrincipalId
    storageAccountName: storage.outputs.storageAccountName
  }
}

// Functions -> Service Bus
module functionsServiceBusRbac './modules/rbac-servicebus.bicep' = if (deployFunctions && deployServiceBus) {
  name: 'functions-servicebus-rbac'
  params: {
    principalId: functions!.outputs.functionAppPrincipalId
    serviceBusNamespaceName: serviceBus!.outputs.serviceBusNamespaceName
  }
}

// Functions -> Key Vault
module functionsKeyVaultRbac './modules/rbac-keyvault.bicep' = if (deployFunctions) {
  name: 'functions-keyvault-rbac'
  params: {
    principalId: functions!.outputs.functionAppPrincipalId
    keyVaultName: keyVault.outputs.keyVaultName
  }
}

// API -> Cosmos DB
module apiCosmosRbac './modules/rbac-cosmos.bicep' = if (deployApi) {
  name: 'api-cosmos-rbac'
  params: {
    principalId: apiContainerApp!.outputs.containerAppPrincipalId
    cosmosAccountName: cosmosAccountNameResolved
  }
  dependsOn: [
    cosmosExisting
    cosmosNew
  ]
}

// API -> SQL
module apiSqlRbac './modules/rbac-sql.bicep' = if (deployApi && deploySql) {
  name: 'api-sql-rbac'
  params: {
    principalId: apiContainerApp!.outputs.containerAppPrincipalId
    sqlServerName: sqlServerNameResolved
  }
  dependsOn: [
    sql
  ]
}

// API -> Storage
module apiStorageRbac './modules/rbac-storage.bicep' = if (deployApi) {
  name: 'api-storage-rbac'
  params: {
    principalId: apiContainerApp!.outputs.containerAppPrincipalId
    storageAccountName: storage.outputs.storageAccountName
  }
}

// API -> Service Bus
module apiServiceBusRbac './modules/rbac-servicebus.bicep' = if (deployApi && deployServiceBus) {
  name: 'api-servicebus-rbac'
  params: {
    principalId: apiContainerApp!.outputs.containerAppPrincipalId
    serviceBusNamespaceName: serviceBus!.outputs.serviceBusNamespaceName
  }
}

// API -> Key Vault
module apiKeyVaultRbac './modules/rbac-keyvault.bicep' = if (deployApi) {
  name: 'api-keyvault-rbac'
  params: {
    principalId: apiContainerApp!.outputs.containerAppPrincipalId
    keyVaultName: keyVault.outputs.keyVaultName
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceGroupName string = resourceGroup().name
output location string = location
output outboundRunMode string = defaultRunMode

// Observability
output logAnalyticsWorkspaceId string = observability.outputs.logAnalyticsWorkspaceId
output appInsightsConnectionString string = observability.outputs.appInsightsConnectionString

// Key Vault
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri

// Cosmos DB
output cosmosAccountName string = cosmosAccountNameResolved
output cosmosAccountEndpoint string = cosmosAccountEndpointResolved
output cosmosReusedExisting bool = reuseCosmos
output cosmosVectorDocumentsEnabled bool = enableCosmosVectorDocuments

// Azure SQL
output sqlServerName string = deploySql ? sqlServerNameResolved : ''
output sqlDatabaseName string = deploySql ? 'arc' : ''

// Storage
output storageAccountName string = storage.outputs.storageAccountName
output storageBlobEndpoint string = storage.outputs.storageBlobEndpoint

// Service Bus
output serviceBusNamespaceName string = deployServiceBus ? serviceBus!.outputs.serviceBusNamespaceName : ''
output serviceBusNamespaceFqdn string = serviceBusNamespaceFqdnResolved

// Functions
output functionAppName string = deployFunctions ? functions!.outputs.functionAppName : ''

// Container Apps
output apiContainerAppName string = deployApi ? apiContainerApp!.outputs.containerAppName : ''
output apiContainerAppFqdn string = deployApi ? apiContainerApp!.outputs.containerAppFqdn : ''

// AI Services (configuration placeholders)
output azureOpenAiEndpoint string = azureOpenAiEndpoint
output embeddingDeployment string = embeddingDeployment
output documentIntelligenceEndpoint string = documentIntelligenceEndpoint
