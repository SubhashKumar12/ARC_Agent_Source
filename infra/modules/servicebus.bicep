// ARC Service Bus - Workflow Events, Cycle Fan-Out, Gate Resume

param location string
param resourceNamePrefix string
param uniqueSuffix string
param tags object

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${resourceNamePrefix}-sb-${uniqueSuffix}'
  location: location
  tags: tags
  // Standard is the minimum tier that supports duplicate detection (used by the gate queues)
  // and topics. Basic rejects requiresDuplicateDetection.
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    zoneRedundant: false
    disableLocalAuth: false // Allow connection strings for dev - restrict in production with MI only
  }
}

// Cycle fan-out queue
resource cycleFanOutQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'arc-cycle-fanout'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
    requiresDuplicateDetection: false
    requiresSession: false
  }
}

// Alert queue
resource alertQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'arc-alerts'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
    requiresDuplicateDetection: false
    requiresSession: false
  }
}

// Gate notification queue
resource gateNotificationQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'arc-gate-notifications'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P14D' // 2 weeks for gate response window
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    requiresSession: false
  }
}

// Gate resume queue
resource gateResumeQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'arc-gate-resume'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    enablePartitioning: false
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
    requiresSession: false
  }
}

output serviceBusNamespaceName string = serviceBusNamespace.name
output serviceBusNamespaceId string = serviceBusNamespace.id
output serviceBusNamespaceFqdn string = '${serviceBusNamespace.name}.servicebus.windows.net'
