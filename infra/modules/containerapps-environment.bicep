// ARC Container Apps Environment

param location string
param resourceNamePrefix string
param tags object
param logAnalyticsWorkspaceId string

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${resourceNamePrefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2022-10-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2022-10-01').primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

output containerAppsEnvironmentName string = containerAppsEnvironment.name
output containerAppsEnvironmentId string = containerAppsEnvironment.id
