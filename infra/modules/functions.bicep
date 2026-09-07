// ARC Azure Functions - ARC.Host.Functions (Durable Workflows)
//
// All data access (Cosmos/SQL/Service Bus/Blob) uses Managed Identity.
//
// Plan: Basic (B1) App Service plan rather than Y1 Dynamic consumption. The Edureka_AZ
// AllowOnlyCoreServices policy restricts Microsoft.Web/serverFarms to B1/B2/S1, so Y1 is denied.
// A dedicated plan also removes the consumption-only content-share requirement, so no storage
// account key is needed anywhere in this template.

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object
param storageAccountName string
param appInsightsConnectionString string
param keyVaultName string

@description('Cosmos DB account endpoint (not a secret).')
param cosmosAccountEndpoint string = ''

@description('Service Bus fully qualified namespace (not a secret).')
param serviceBusNamespaceFqdn string = ''

@description('Blob service URI for evidence/legal containers (not a secret).')
param blobServiceUri string = ''

@description('Managed-identity SQL connection string (no password).')
param sqlConnectionString string = ''

@description('Outbound run mode. Must remain Shadow for DEV/UAT.')
@allowed(['Shadow', 'Live'])
param defaultRunMode string = 'Shadow'

@description('App Service plan SKU. Policy allows B1/B2/S1 only.')
@allowed(['B1', 'B2', 'S1'])
param appServicePlanSku string = 'B1'

resource functionAppServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${resourceNamePrefix}-func-plan'
  location: location
  tags: tags
  sku: {
    name: appServicePlanSku
    tier: appServicePlanSku == 'S1' ? 'Standard' : 'Basic'
  }
  kind: 'functionapp'
  properties: {
    reserved: false // Windows
  }
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: '${resourceNamePrefix}-func-${uniqueSuffix}'
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionAppServicePlan.id
    siteConfig: {
      appSettings: [
        // Runtime storage via Managed Identity (no key)
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__blobServiceUri'
          value: 'https://${storageAccountName}.blob.${az.environment().suffixes.storage}'
        }
        {
          name: 'AzureWebJobsStorage__queueServiceUri'
          value: 'https://${storageAccountName}.queue.${az.environment().suffixes.storage}'
        }
        {
          name: 'AzureWebJobsStorage__tableServiceUri'
          value: 'https://${storageAccountName}.table.${az.environment().suffixes.storage}'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        // Outbound safety - Shadow only
        {
          name: 'ArcHost__DefaultRunMode'
          value: defaultRunMode
        }
        {
          name: 'ArcApi__DefaultRunMode'
          value: defaultRunMode
        }
        {
          name: 'KeyVaultName'
          value: keyVaultName
        }
        // Managed Identity data plane configuration (endpoints are not secrets)
        {
          name: 'ArcData__Cosmos__AccountEndpoint'
          value: cosmosAccountEndpoint
        }
        {
          name: 'ArcData__Cosmos__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'ArcData__Sql__ConnectionString'
          value: sqlConnectionString
        }
        {
          name: 'ArcData__Sql__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'ArcData__ServiceBus__FullyQualifiedNamespace'
          value: serviceBusNamespaceFqdn
        }
        {
          name: 'ArcData__ServiceBus__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'ArcData__Blob__ServiceUri'
          value: blobServiceUri
        }
        {
          name: 'ArcData__Blob__EvidenceContainer'
          value: 'evidence'
        }
        {
          name: 'ArcData__Blob__LegalContainer'
          value: 'legal-worm'
        }
        {
          name: 'ArcData__Blob__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'ArcKnowledge__UseManagedIdentity'
          value: 'true'
        }
        {
          name: 'ArcAi__UseManagedIdentity'
          value: 'true'
        }
      ]
      netFrameworkVersion: 'v9.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      alwaysOn: true // Supported on B1+; required for timer triggers on a dedicated plan
    }
    httpsOnly: true
    clientAffinityEnabled: false
  }
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppHostName string = functionApp.properties.defaultHostName
