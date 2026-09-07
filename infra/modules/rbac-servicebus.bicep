// RBAC - Service Bus Data Owner role assignment

param principalId string
param serviceBusNamespaceName string

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

// Azure Service Bus Data Owner role
resource serviceBusDataOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId, serviceBusNamespace.id, 'ServiceBusDataOwner')
  scope: serviceBusNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '090c5cfd-751d-490a-894a-3ce6f1109419') // Azure Service Bus Data Owner
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = serviceBusDataOwnerRole.id
