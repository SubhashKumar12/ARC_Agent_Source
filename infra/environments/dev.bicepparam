// ARC DEV Environment Parameters
// DO NOT commit secrets to source control

using '../main.bicep'

// Environment
param environment = 'dev'
param location = 'eastus' // Change to your preferred region
param projectPrefix = 'arc'

// Tags
param tags = {
  Project: 'ARC'
  Environment: 'dev'
  ManagedBy: 'Bicep'
  CostCenter: 'Engineering'
}

// Azure OpenAI / Foundry (configure post-deployment)
param azureOpenAiEndpoint = '' // e.g., 'https://your-openai.openai.azure.com/'
param azureOpenAiDeployments = {
  reasoning: '' // Deployment name for reasoning model
  extraction: '' // Deployment name for extraction model
  cheapNarration: '' // Deployment name for cheap narration model
}
param embeddingDeployment = '' // Deployment name for embeddings (3072-dim)

// Document Intelligence (configure post-deployment)
param documentIntelligenceEndpoint = '' // e.g., 'https://your-doc-intel.cognitiveservices.azure.com/'

// Cosmos DB (dev-friendly settings)
param cosmosFreeTier = true // Use free tier for dev (400 RU/s limit)
param cosmosThroughputMode = 'manual'
param cosmosManualThroughput = 400

// Azure SQL (DO NOT commit passwords - use Key Vault reference or parameter file override)
// For dev: use strong password, rotate regularly, migrate to MI post-setup
param sqlAdminLogin = 'arcadmin'
param sqlAdminPassword = '' // REQUIRED - pass via --parameters or Key Vault
param sqlDatabaseSku = 'Basic' // Dev tier
param sqlDatabaseMaxSizeBytes = 2147483648 // 2GB

// Storage
param legalBlobRetentionDays = 2920 // 8 years (assignment requirement)
param legalBlobWormEnabled = true // Enable WORM for legal container

// Networking
param enablePrivateEndpoints = false // False for dev cost control

// Container Apps
param containerRegistryName = '' // Configure if using private registry
param apiContainerImage = 'mcr.microsoft.com/dotnet/aspnet:9.0' // Placeholder - update with actual image
param apiCpuCores = '0.5'
param apiMemory = '1.0Gi'
