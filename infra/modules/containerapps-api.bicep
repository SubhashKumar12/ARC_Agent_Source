// ARC.Api Container App
//
// Configuration values below are endpoints/namespaces, not secrets, so they are supplied as plain
// env vars. Anything genuinely secret must be added as a Key Vault reference secret after the
// container app identity has been granted Key Vault access.

param location string
param resourceNamePrefix string
param tags object
param containerAppsEnvironmentId string
param containerImage string
param cpuCores string = '0.5'
param memory string = '1.0Gi'
param appInsightsConnectionString string
param keyVaultName string

@description('Cosmos DB account endpoint (not a secret).')
param cosmosAccountEndpoint string = ''

@description('Service Bus fully qualified namespace (not a secret).')
param serviceBusNamespaceFqdn string = ''

@description('Blob service URI (not a secret).')
param blobServiceUri string = ''

@description('Managed-identity SQL connection string (no password).')
param sqlConnectionString string = ''

@description('Outbound run mode. Must remain Shadow for DEV/UAT.')
@allowed(['Shadow', 'Live'])
param defaultRunMode string = 'Shadow'

@description('Entra JWT authority for API authentication.')
param jwtAuthority string = ''

@description('Entra JWT audience for API authentication.')
param jwtAudience string = ''

resource apiContainerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${resourceNamePrefix}-api'
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [] // Configure post-deployment if using private registry
    }
    template: {
      containers: [
        {
          name: 'arc-api'
          image: containerImage // Placeholder - update with actual ARC.Api image
          resources: {
            cpu: json(cpuCores)
            memory: memory
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Development'
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsightsConnectionString
            }
            {
              name: 'ArcApi__DefaultRunMode'
              value: defaultRunMode
            }
            {
              name: 'ArcApi__JwtAuthority'
              value: jwtAuthority
            }
            {
              name: 'ArcApi__JwtAudience'
              value: jwtAudience
            }
            {
              name: 'KeyVaultName'
              value: keyVaultName
            }
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
        }
      ]
      scale: {
        minReplicas: 0 // Scale to zero in dev
        maxReplicas: 3
        rules: [
          {
            name: 'http-scaling'
            http: {
              metadata: {
                concurrentRequests: '10'
              }
            }
          }
        ]
      }
    }
  }
}

output containerAppName string = apiContainerApp.name
output containerAppId string = apiContainerApp.id
output containerAppPrincipalId string = apiContainerApp.identity.principalId
output containerAppFqdn string = apiContainerApp.properties.configuration.ingress.fqdn
