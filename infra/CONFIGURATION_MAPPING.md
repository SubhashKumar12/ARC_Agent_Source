# ARC Azure Infrastructure - Configuration Mapping

## DEV Environment Configuration Reference

This document maps Bicep infrastructure outputs to ARC application configuration settings.

### Quick Reference Table

| Bicep Output | ARC Config Key | Value Type | Auth Method |
|--------------|----------------|------------|-------------|
| `cosmosAccountEndpoint` | `ArcData:Cosmos:AccountEndpoint` | URI | Managed Identity |
| `cosmosDatabaseName` | `ArcData:Cosmos:DatabaseId` | String | N/A |
| - | `ArcData:Cosmos:UseManagedIdentity` | Boolean | `true` |
| `sqlServerFqdn` | `ArcData:Sql:ConnectionString` | Connection String | Managed Identity |
| - | `ArcData:Sql:UseManagedIdentity` | Boolean | `true` |
| `storageBlobEndpoint` | `ArcData:Blob:ServiceUri` | URI | Managed Identity |
| - | `ArcData:Blob:UseManagedIdentity` | Boolean | `true` |
| `serviceBusNamespaceFqdn` | `ArcData:ServiceBus:FullyQualifiedNamespace` | FQDN | Managed Identity |
| - | `ArcData:ServiceBus:UseManagedIdentity` | Boolean | `true` |
| `keyVaultUri` | Key Vault references in env vars | URI | Managed Identity |
| `appInsightsConnectionString` | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Connection String | Connection String |
| `azureOpenAiEndpoint` | `ArcAi:Endpoint` | URI | Managed Identity |
| - | `ArcAi:UseManagedIdentity` | Boolean | `true` |
| `embeddingDeployment` | `ArcKnowledge:Embeddings:Deployment` | String | N/A |
| - | `ArcKnowledge:Embeddings:Dimensions` | Integer | `3072` (required) |
| - | `ArcKnowledge:Embeddings:UseManagedIdentity` | Boolean | `true` |
| `documentIntelligenceEndpoint` | `ArcKnowledge:DocumentIntelligenceEndpoint` | URI | Managed Identity |
| - | `ArcKnowledge:UseManagedIdentity` | Boolean | `true` |

## Azure Functions (ARC.Host.Functions)

### Application Settings

```json
{
  "AzureWebJobsStorage__accountName": "<storage-account-name>",
  "FUNCTIONS_EXTENSION_VERSION": "~4",
  "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
  "APPLICATIONINSIGHTS_CONNECTION_STRING": "<from-bicep-output>",
  
  "ArcHost__DefaultRunMode": "Shadow",
  
  "ArcData__Cosmos__AccountEndpoint": "<from-key-vault-or-bicep>",
  "ArcData__Cosmos__UseManagedIdentity": "true",
  "ArcData__Cosmos__DatabaseId": "arc",
  
  "ArcData__Sql__ConnectionString": "Server=tcp:<server-fqdn>,1433;Database=arc;Authentication=Active Directory Default;",
  "ArcData__Sql__UseManagedIdentity": "true",
  
  "ArcData__ServiceBus__FullyQualifiedNamespace": "<namespace>.servicebus.windows.net",
  "ArcData__ServiceBus__UseManagedIdentity": "true",
  
  "ArcKnowledge__UseManagedIdentity": "true",
  "ArcKnowledge__Embeddings__UseManagedIdentity": "true",
  "ArcKnowledge__Embeddings__Dimensions": "3072",
  
  "ArcAi__UseManagedIdentity": "true"
}
```

### Key Vault References (Optional)

```json
{
  "ArcData__Cosmos__AccountEndpoint": "@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/CosmosAccountEndpoint/)",
  "ArcData__Sql__ConnectionString": "@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/SqlConnectionString/)",
  "ArcData__ServiceBus__FullyQualifiedNamespace": "@Microsoft.KeyVault(SecretUri=https://<vault-name>.vault.azure.net/secrets/ServiceBusNamespace/)"
}
```

## Container Apps (ARC.Api)

### Environment Variables

```bash
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_HTTP_PORTS=8080
APPLICATIONINSIGHTS_CONNECTION_STRING=<from-bicep>

ArcApi__DefaultRunMode=Shadow

# Cosmos DB
ArcData__Cosmos__AccountEndpoint=<endpoint>
ArcData__Cosmos__UseManagedIdentity=true
ArcData__Cosmos__DatabaseId=arc

# Azure SQL
ArcData__Sql__ConnectionString=Server=tcp:<fqdn>,1433;Database=arc;Authentication=Active Directory Default;
ArcData__Sql__UseManagedIdentity=true

# Service Bus
ArcData__ServiceBus__FullyQualifiedNamespace=<namespace>.servicebus.windows.net
ArcData__ServiceBus__UseManagedIdentity=true

# Knowledge & AI
ArcKnowledge__UseManagedIdentity=true
ArcKnowledge__Embeddings__UseManagedIdentity=true
ArcKnowledge__Embeddings__Dimensions=3072
ArcAi__UseManagedIdentity=true
```

### Container Secrets (via Key Vault or Container Apps secrets)

```bash
cosmos-endpoint=<from-output>
sql-connection=Server=tcp:<fqdn>,1433;Database=arc;Authentication=Active Directory Default;
servicebus-namespace=<namespace>.servicebus.windows.net
```

## Cosmos DB Containers

All containers use database `arc`:

| Container Name | Partition Key | Purpose | TTL |
|----------------|--------------|---------|-----|
| `checkpoints` | `/cycleId` | MAF workflow checkpoints | 90 days |
| `cycleState` | `/cycleId` | Per-dealer cycle state | None |
| `auditEvents` | `/cycleId` | Immutable audit log | 8 years |
| `conversationState` | `/cycleId` | Agent conversation history | None |
| `documents` | `/documentType` | Knowledge chunks with 3072-dim vector | None |

## SQL Connection String Format

For Managed Identity authentication:

```
Server=tcp:<server-fqdn>,1433;Database=arc;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;
```

## Blob Storage Containers

| Container Name | Purpose | Access | Retention |
|----------------|---------|--------|-----------|
| `evidence` | Supporting documents | Private | Standard |
| `legal-worm` | Legal notices (immutable) | Private | 8 years (WORM) |

## Service Bus Queues

| Queue Name | Purpose | Max Delivery Count | Lock Duration |
|------------|---------|-------------------|---------------|
| `arc-cycle-fanout` | Monthly ODOS cycle fan-out | 10 | 5 minutes |
| `arc-alerts` | Limitation/deadline alerts | 5 | 1 minute |
| `arc-gate-notifications` | Human gate notifications | 10 | 5 minutes |
| `arc-gate-resume` | Gate resume triggers | 10 | 5 minutes |

## Managed Identity Roles

### Azure Functions Identity

| Resource | Role | Purpose |
|----------|------|---------|
| Cosmos DB | Cosmos DB Data Contributor | Read/write checkpoints, state, documents |
| Azure SQL | Reader (resource) + SQL user (data plane) | Read/write relational data |
| Blob Storage | Storage Blob Data Contributor | Read/write evidence, legal documents |
| Service Bus | Azure Service Bus Data Owner | Send/receive queue messages |
| Key Vault | Key Vault Secrets User | Read configuration secrets |

### Container Apps API Identity

Same roles as Functions (above).

## Local Development Configuration

For local development (appsettings.Development.json or user secrets):

```json
{
  "ArcData": {
    "Sql": {
      "ConnectionString": "Server=(localdb)\\mssqllocaldb;Database=arc;Integrated Security=true;",
      "UseManagedIdentity": false
    },
    "Cosmos": {
      "ConnectionString": "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
      "UseManagedIdentity": false
    },
    "ServiceBus": {
      "ConnectionString": "Endpoint=sb://localhost/...",
      "UseManagedIdentity": false
    }
  }
}
```

## Post-Deployment Configuration Steps

### 1. Extract Bicep Outputs

```bash
az deployment group show \
  --resource-group arc-dev-rg \
  --name <deployment-name> \
  --query properties.outputs \
  --output json > outputs.json
```

### 2. Populate Key Vault Secrets

```bash
# Get outputs
COSMOS_ENDPOINT=$(jq -r '.cosmosAccountEndpoint.value' outputs.json)
SQL_SERVER=$(jq -r '.sqlServerFqdn.value' outputs.json)
SB_NS=$(jq -r '.serviceBusNamespaceFqdn.value' outputs.json)
KV_NAME=$(jq -r '.keyVaultName.value' outputs.json)

# Set secrets
az keyvault secret set --vault-name $KV_NAME \
  --name CosmosAccountEndpoint \
  --value "$COSMOS_ENDPOINT"

az keyvault secret set --vault-name $KV_NAME \
  --name SqlConnectionString \
  --value "Server=tcp:$SQL_SERVER,1433;Database=arc;Authentication=Active Directory Default;"

az keyvault secret set --vault-name $KV_NAME \
  --name ServiceBusNamespace \
  --value "$SB_NS"
```

### 3. Update Function App Settings

```bash
FUNC_APP=$(jq -r '.functionAppName.value' outputs.json)
APP_INSIGHTS=$(jq -r '.appInsightsConnectionString.value' outputs.json)

az functionapp config appsettings set \
  --name $FUNC_APP \
  --resource-group arc-dev-rg \
  --settings \
    "ArcData__Cosmos__AccountEndpoint=@Microsoft.KeyVault(SecretUri=https://$KV_NAME.vault.azure.net/secrets/CosmosAccountEndpoint/)" \
    "ArcData__Sql__ConnectionString=@Microsoft.KeyVault(SecretUri=https://$KV_NAME.vault.azure.net/secrets/SqlConnectionString/)" \
    "ArcData__ServiceBus__FullyQualifiedNamespace=@Microsoft.KeyVault(SecretUri=https://$KV_NAME.vault.azure.net/secrets/ServiceBusNamespace/)" \
    "APPLICATIONINSIGHTS_CONNECTION_STRING=$APP_INSIGHTS"
```

### 4. Update Container App Configuration

```bash
API_APP=$(jq -r '.apiContainerAppName.value' outputs.json)

az containerapp update \
  --name $API_APP \
  --resource-group arc-dev-rg \
  --set-env-vars \
    "ArcData__Cosmos__AccountEndpoint=$COSMOS_ENDPOINT" \
    "ArcData__Sql__ConnectionString=Server=tcp:$SQL_SERVER,1433;Database=arc;Authentication=Active Directory Default;" \
    "ArcData__ServiceBus__FullyQualifiedNamespace=$SB_NS" \
    "APPLICATIONINSIGHTS_CONNECTION_STRING=$APP_INSIGHTS"
```

## Validation Checklist

After configuration:

- [ ] Functions can resolve Cosmos DB via Managed Identity
- [ ] Functions can connect to SQL via Managed Identity
- [ ] Functions can send/receive Service Bus messages
- [ ] API can query Cosmos DB documents container
- [ ] API can execute SQL queries
- [ ] Telemetry flows to Application Insights
- [ ] Key Vault references resolve correctly
- [ ] Blob Storage read/write works via Managed Identity
- [ ] Vector search returns results (after knowledge ingestion)

## Troubleshooting

### "Unable to authenticate" errors

- Wait 5-10 minutes for RBAC role assignments to propagate
- Verify `UseManagedIdentity=true` in configuration
- Check Managed Identity principal ID matches RBAC assignments

### SQL Connection Failures

- Verify T-SQL user created for Managed Identity (see infra/README.md)
- Ensure connection string includes `Authentication=Active Directory Default`
- Check firewall rules allow Azure services

### Cosmos DB 401 Unauthorized

- Verify role assignment completed: `Cosmos DB Data Contributor`
- Check account endpoint is correct
- Ensure `disableKeyBasedMetadataWriteAccess=true` matches MI-only policy

### Service Bus Connection Issues

- Verify `Azure Service Bus Data Owner` role assigned
- Use FQDN format: `<namespace>.servicebus.windows.net`
- Do not include protocol prefix (sb://)

---

**Last Updated:** Phase 5A completion  
**Environment:** DEV  
**Managed Identity:** Preferred authentication for all services
