// RBAC - Azure SQL (requires post-deployment SQL user creation)

param principalId string
param sqlServerName string

// Note: Azure RBAC for SQL data plane access requires:
// 1. Entra ID admin configured on SQL Server (done in sql.bicep)
// 2. SQL user creation via T-SQL (post-deployment step)
//
// Post-deployment SQL commands:
// CREATE USER [<function-app-name>] FROM EXTERNAL PROVIDER;
// ALTER ROLE db_datareader ADD MEMBER [<function-app-name>];
// ALTER ROLE db_datawriter ADD MEMBER [<function-app-name>];
// ALTER ROLE db_ddladmin ADD MEMBER [<function-app-name>]; -- if schema updates needed
//
// Connection string format for Managed Identity:
// Server=tcp:<server>.database.windows.net,1433;Database=arc;Authentication=Active Directory Default;

resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' existing = {
  name: sqlServerName
}

// Reader role on SQL Server resource (not data plane)
resource sqlServerReaderRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId, sqlServer.id, 'Reader')
  scope: sqlServer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'acdd72a7-3385-48ef-bd42-f606fba81ae7') // Reader
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output roleAssignmentId string = sqlServerReaderRole.id
output postDeploymentNote string = 'SQL data plane access requires T-SQL user creation - see module comments'
