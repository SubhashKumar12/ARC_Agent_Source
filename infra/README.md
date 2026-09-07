# ARC Azure Infrastructure (Bicep)

**Phase 5A - DEV Environment Foundation**

This directory contains Bicep infrastructure-as-code for deploying ARC to Azure.

## ⚠️ IMPORTANT

**DO NOT deploy without review and approval.**

This infrastructure is for DEV environment only. Production deployment requires additional security hardening, networking, and Legal/Finance sign-off.

## Architecture

```
Azure Container Apps (ARC.Api)
   ↓
Azure Functions (ARC.Host.Functions)
   ↓
Azure SQL (master data, dealers, ledger, cases)
Cosmos DB (checkpoints, state, audit, conversation, documents/vector)
Blob Storage (evidence, legal WORM)
Service Bus (cycle fan-out, alerts, gate notifications)
   ↓
Key Vault (secrets)
Application Insights (telemetry)
```

All services use **Managed Identity** for service-to-service authentication.

## Files

```
infra/
├── main.bicep                              # Main orchestration
├── modules/
│   ├── observability.bicep                 # Log Analytics + App Insights
│   ├── keyvault.bicep                      # Key Vault
│   ├── cosmos.bicep                        # Cosmos DB + containers
│   ├── sql.bicep                           # Azure SQL
│   ├── storage.bicep                       # Blob Storage
│   ├── servicebus.bicep                    # Service Bus + queues
│   ├── functions.bicep                     # Azure Functions
│   ├── containerapps-environment.bicep     # Container Apps Environment
│   ├── containerapps-api.bicep             # ARC.Api Container App
│   ├── rbac-cosmos.bicep                   # Cosmos RBAC
│   ├── rbac-sql.bicep                      # SQL RBAC
│   ├── rbac-storage.bicep                  # Storage RBAC
│   ├── rbac-servicebus.bicep               # Service Bus RBAC
│   └── rbac-keyvault.bicep                 # Key Vault RBAC
├── environments/
│   └── dev.bicepparam                      # DEV parameters
└── README.md                               # This file
```

## Prerequisites

1. Azure CLI (`az`) installed
2. Bicep CLI installed (`az bicep install`)
3. Azure subscription with appropriate permissions
4. Resource group created

## Cosmos DB Containers

| Container | Partition Key | Purpose | TTL |
|-----------|--------------|---------|-----|
| `checkpoints` | `/cycleId` | MAF workflow checkpoints | 90 days |
| `cycleState` | `/cycleId` | Per-dealer run state | None |
| `auditEvents` | `/cycleId` | Immutable audit log | 8 years |
| `conversationState` | `/cycleId` | Conversation state | None |
| `documents` | `/documentType` | Knowledge chunks + 3072-dim vector | None |

**CRITICAL:** `documents` container has vector indexing:
- **Dimensions:** 3072 (assignment requirement - do not change)
- **Distance function:** Cosine
- **Index type:** DiskANN

## Service Bus Queues

- `arc-cycle-fanout` - Monthly ODOS cycle fan-out
- `arc-alerts` - Limitation/deadline alerts
- `arc-gate-notifications` - Human gate notifications
- `arc-gate-resume` - Gate resume triggers

## SQL Database

Database `arc` contains:
- Dealers, ledger, cheques, notices, cases
- Gate decisions (immutable audit)
- Dealer identity mappings

**Post-deployment:** Run schema migration scripts from `ARC.Data` project.

## Blob Storage Containers

- `evidence` - Standard retention
- `legal-worm` - **8-year immutable retention** (WORM enabled)

## Managed Identity & RBAC

Both **Azure Functions** and **Container Apps API** have System-Assigned Managed Identities with least-privilege roles:

| Service | Role | Scope |
|---------|------|-------|
| Cosmos DB | Cosmos DB Data Contributor | Account |
| Azure SQL | Reader (resource) + T-SQL user (data plane) | Server |
| Blob Storage | Storage Blob Data Contributor | Account |
| Service Bus | Azure Service Bus Data Owner | Namespace |
| Key Vault | Key Vault Secrets User | Vault |

**SQL Note:** Azure RBAC for SQL data plane requires T-SQL user creation post-deployment (see below).

## Deployment

### 1. Validate Bicep (local - no deployment)

```bash
# Build/validate syntax
az bicep build --file infra/main.bicep

# What-if (simulates deployment without executing)
az deployment group what-if \
  --resource-group <rg-name> \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam \
  --parameters sqlAdminPassword='<strong-password>'
```

### 2. Deploy to Azure (DEV only)

**⚠️ Review outputs and costs before deploying.**

```bash
# Create resource group
az group create --name arc-dev-rg --location eastus

# Deploy infrastructure
az deployment group create \
  --resource-group arc-dev-rg \
  --template-file infra/main.bicep \
  --parameters infra/environments/dev.bicepparam \
  --parameters sqlAdminPassword='<strong-password>' \
  --mode Incremental
```

**Deployment time:** ~10-15 minutes

### 3. Post-Deployment Configuration

#### SQL Managed Identity Setup

Connect to Azure SQL with Entra ID admin and create SQL users for Managed Identities:

```sql
-- Functions Managed Identity
CREATE USER [arc-dev-func-<suffix>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [arc-dev-func-<suffix>];
ALTER ROLE db_datawriter ADD MEMBER [arc-dev-func-<suffix>];

-- API Managed Identity
CREATE USER [arc-dev-api] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [arc-dev-api];
ALTER ROLE db_datawriter ADD MEMBER [arc-dev-api];
```

#### Run SQL Schema Migration

```bash
# From repository root
dotnet ef database update --project src/ARC.Data --startup-project src/ARC.Api
# Or apply migration scripts manually
```

#### Configure Secrets in Key Vault

```bash
KV_NAME=$(az deployment group show -g arc-dev-rg -n <deployment-name> --query properties.outputs.keyVaultName.value -o tsv)
COSMOS_ENDPOINT=$(az deployment group show -g arc-dev-rg -n <deployment-name> --query properties.outputs.cosmosAccountEndpoint.value -o tsv)
SQL_SERVER=$(az deployment group show -g arc-dev-rg -n <deployment-name> --query properties.outputs.sqlServerFqdn.value -o tsv)
SB_NS=$(az deployment group show -g arc-dev-rg -n <deployment-name> --query properties.outputs.serviceBusNamespaceFqdn.value -o tsv)

# Set secrets
az keyvault secret set --vault-name $KV_NAME --name CosmosAccountEndpoint --value "$COSMOS_ENDPOINT"
az keyvault secret set --vault-name $KV_NAME --name SqlConnectionString --value "Server=tcp:$SQL_SERVER,1433;Database=arc;Authentication=Active Directory Default;"
az keyvault secret set --vault-name $KV_NAME --name ServiceBusNamespace --value "$SB_NS"
```

#### Deploy Application Code

**Functions:**
```bash
func azure functionapp publish <function-app-name>
```

**Container Apps:**
```bash
# Build and push container image
az acr build --registry <acr-name> --image arc/api:latest ./src/ARC.Api

# Update Container App image
az containerapp update \
  --name arc-dev-api \
  --resource-group arc-dev-rg \
  --image <acr-name>.azurecr.io/arc/api:latest
```

## Configuration Mapping

| Bicep Output | ARC Configuration Key | Auth Mode |
|--------------|----------------------|-----------|
| `cosmosAccountEndpoint` | `ArcData:Cosmos:AccountEndpoint` | Managed Identity |
| `sqlServerFqdn` | `ArcData:Sql:ConnectionString` | Managed Identity (`Authentication=Active Directory Default`) |
| `storageBlobEndpoint` | `ArcData:Blob:ServiceUri` | Managed Identity |
| `serviceBusNamespaceFqdn` | `ArcData:ServiceBus:FullyQualifiedNamespace` | Managed Identity |
| `appInsightsConnectionString` | `APPLICATIONINSIGHTS_CONNECTION_STRING` | Connection String |
| `keyVaultUri` | Key Vault references in app settings | Managed Identity |

## Cost Controls (DEV)

- **Cosmos DB:** Free tier (400 RU/s limit)
- **Azure SQL:** Basic tier (2GB, ~$5/month)
- **Functions:** Consumption plan (pay-per-execution)
- **Container Apps:** Scale-to-zero enabled
- **Service Bus:** Basic tier (~$0.05/month)
- **Storage:** Standard LRS
- **Log Analytics:** 30-day retention

**Estimated monthly cost (dev):** ~$10-20 USD (excluding Azure OpenAI usage)

## Security Notes

### DEV Environment

- ✅ Managed Identity for all service-to-service auth
- ✅ RBAC least-privilege roles
- ✅ TLS 1.2 minimum
- ✅ HTTPS only
- ✅ Key Vault for secrets
- ✅ WORM storage for legal documents
- ⚠️ Public network access enabled (for dev convenience)
- ⚠️ SQL admin password required for bootstrap

### Production Hardening (TBC)

- [ ] Private endpoints for all services
- [ ] Network isolation / VNet integration
- [ ] Remove SQL admin password (MI only)
- [ ] Disable Blob shared key access
- [ ] Enable Defender for Cloud
- [ ] Increase retention periods
- [ ] Zone redundancy
- [ ] Geo-redundancy for critical data
- [ ] Key Vault purge protection

## AI Services Configuration

**Azure OpenAI / Foundry:**
- Not provisioned by Bicep (use existing)
- Configure `azureOpenAiEndpoint` and `azureOpenAiDeployments` in parameters
- Embeddings must be **3072 dimensions** (assignment requirement)

**Document Intelligence:**
- Not provisioned by Bicep (use existing)
- Configure `documentIntelligenceEndpoint` in parameters

**Azure AI Speech:**
- Not provisioned (Phase 5B integration)

## Troubleshooting

### Deployment Fails: "Cosmos free tier already used"

Only one free-tier Cosmos account per subscription. Set `cosmosFreeTier = false` and accept Standard pricing.

### Functions can't connect to Cosmos

1. Verify MI role assignment completed
2. Check `UseManagedIdentity = true` in Functions app settings
3. Wait 5-10 minutes for RBAC propagation

### SQL connection fails with MI

1. Verify SQL users created (see post-deployment steps)
2. Check connection string includes `Authentication=Active Directory Default`
3. Verify Functions/API MI has Reader role on SQL Server resource

### Container App fails to start

1. Check logs: `az containerapp logs show --name arc-dev-api -g arc-dev-rg`
2. Verify container image exists and is accessible
3. Check app settings reference correct Key Vault secrets

## Cleanup

**⚠️ This deletes ALL resources.**

```bash
az group delete --name arc-dev-rg --yes --no-wait
```

## Next Steps

After successful deployment:

1. ✅ Validate infrastructure
2. ✅ Run SQL schema migration
3. ✅ Deploy application code
4. ✅ Test S1-S9 scenarios in Azure
5. ✅ Verify telemetry in Application Insights
6. ✅ Test Cosmos vector search with real embeddings
7. ⏳ Document Intelligence integration (Phase 5B)
8. ⏳ Azure AI Speech integration (Phase 5B)
9. ⏳ Production-scale data generation
10. ⏳ Golden set evaluation

## Support

For issues or questions:
- Review Bicep validation errors
- Check Azure Portal deployment logs
- Review Application Insights logs
- Verify RBAC propagation (can take 5-10 min)

---

**Phase 5A Status:** Bicep foundation complete, awaiting deployment approval.
